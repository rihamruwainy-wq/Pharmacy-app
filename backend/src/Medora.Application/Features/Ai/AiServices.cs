using System.Globalization;
using System.Text;
using System.Text.Json;
using Medora.Application.Abstractions;
using Medora.Application.Common;
using Medora.Application.Contracts;
using Medora.Application.Features.Admin;
using Medora.Application.Features.Pharmacist;
using Medora.Domain.Common;
using Medora.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Medora.Application.Features.Ai;

// ------------------------------------------------------------------ Ports (implemented with Claude in Infrastructure)

public sealed record LlmUsage(long InputTokens, long OutputTokens, long CacheReadTokens, long CacheWriteTokens);
public sealed record LlmCitation(string SourceId, string Quote);
public sealed record ClinicalAnswer(string Text, IReadOnlyList<LlmCitation> Citations, LlmUsage Usage);

/// <summary>Grounded clinical Q&amp;A: answers ONLY from the supplied sources and returns citations into them.</summary>
public interface IClinicalLlm
{
    bool IsConfigured { get; }
    Task<ClinicalAnswer> AnswerAsync(string question, string caseContext, IReadOnlyList<KnowledgeChunk> sources, CancellationToken cancellationToken);
}

/// <summary>A read-only server function the analytics model may call. <c>InputSchemaJson</c> is a JSON Schema object.</summary>
public sealed record AssistantTool(string Name, string Description, string InputSchemaJson, Func<JsonElement, CancellationToken, Task<object>> Execute);
public sealed record AssistantRun(string Answer, IReadOnlyList<ToolCallDto> ToolCalls, LlmUsage Usage);

/// <summary>Tool-using analytics assistant (agentic loop bounded by the implementation).</summary>
public interface IAnalyticsLlm
{
    bool IsConfigured { get; }
    Task<AssistantRun> RunAsync(string systemPrompt, string dynamicContext, IReadOnlyList<ChatTurn> history, IReadOnlyList<AssistantTool> tools, CancellationToken cancellationToken);
}

public interface IKnowledgeIndexProvider
{
    ValueTask<KnowledgeIndex> GetAsync(CancellationToken cancellationToken);
}

// ------------------------------------------------------------------ Pharmacist copilot (RAG)

/// <summary>
/// Pharmacist copilot. Pipeline: scope check -> deterministic interaction screen -> BM25 retrieval
/// (boosted toward the case's drugs) -> grounded, cited answer -> audit. The interaction ALERTS are
/// computed here from the curated table and returned verbatim: the model explains, it never decides.
/// The model sees a DE-IDENTIFIED case (age, allergies, medicines) - never names or contact details.
/// </summary>
public sealed class PharmacistCopilotService(PharmacistService pharmacist, IKnowledgeIndexProvider knowledge, IClinicalLlm llm, IAppDbContext db, ICurrentUser user, TimeProvider clock)
{
    public async Task<Result<CopilotResponse>> AskAsync(CopilotRequest request, CancellationToken ct)
    {
        if (request.Question is not { Length: >= 3 and <= 2000 }) return Errors.Validation("Ask a question (3-2000 characters).");

        VerificationItemDto? item = null;
        if (request.OrderId is not null)
        {
            var detail = await pharmacist.DetailAsync(request.OrderId, ct); // enforces branch scope
            if (!detail.IsSuccess) return detail.Error!;
            item = detail.Value;
        }

        // Retrieval is boosted toward the drugs IN THIS ORDER. The patient's other medicines reach the model
        // through the case summary and the deterministic alerts; boosting them too crowds out the order's own sections.
        var caseDrugs = item is null
            ? []
            : await db.Products.AsNoTracking().Where(p => item.Order.Lines.Select(l => l.ProductId).Contains(p.Id)).Select(p => p.GenericName).Distinct().ToListAsync(ct);
        var alertDrugs = (item?.Alerts ?? []).SelectMany(a => new[] { a.DrugA, a.DrugB }).Where(d => !d.Contains(' ', StringComparison.Ordinal));
        var query = $"{request.Question} {string.Join(' ', alertDrugs.Distinct(StringComparer.Ordinal))}"; // alerted pairs pull in their interaction sections

        var index = await knowledge.GetAsync(ct);
        var sources = index.Search(query, caseDrugs, take: 7);
        var alerts = item?.Alerts ?? [];
        var sourceDtos = sources.Select(s => new SourceDto(s.Id, s.Title)).ToList();

        if (!llm.IsConfigured)
        {
            return new CopilotResponse(
                "The AI copilot is turned off on this server (Ai:Provider). The interaction alerts and the most relevant monograph sections are listed below.",
                [], alerts, sourceDtos, false);
        }

        var answer = await llm.AnswerAsync(request.Question.Trim(), BuildCaseContext(item, alerts), sources, ct);
        var byId = sources.ToDictionary(s => s.Id, StringComparer.Ordinal);
        var citations = answer.Citations
            .Where(c => byId.ContainsKey(c.SourceId))
            .Select(c => new CitationDto(c.SourceId, byId[c.SourceId].Title, c.Quote, byId[c.SourceId].Provenance))
            .ToList();

        db.AuditEntries.Add(new AuditEntry(user.RequireUserId(), "ai.copilot", request.OrderId ?? "-",
            $"q=\"{Truncate(request.Question, 300)}\" sources={string.Join(',', sources.Select(s => s.Id))} in={answer.Usage.InputTokens} out={answer.Usage.OutputTokens} cache_read={answer.Usage.CacheReadTokens}",
            clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        return new CopilotResponse(answer.Text, citations, alerts, sourceDtos, true);
    }

    internal static string BuildCaseContext(VerificationItemDto? item, IReadOnlyList<InteractionAlertDto> alerts)
    {
        if (item is null) return "No order is attached; answer the general question.";
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Patient age: {item.CustomerAge}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Declared allergies: {item.CustomerAllergies ?? "none declared"}");
        sb.AppendLine("Items in this order:");
        foreach (var l in item.Order.Lines) sb.AppendLine(CultureInfo.InvariantCulture, $"- {l.Quantity} x {l.Name}{(l.RequiresPrescription ? " (Rx)" : string.Empty)}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Other medicines dispensed in the last 180 days: {(item.RecentMedicines.Count == 0 ? "none" : string.Join(", ", item.RecentMedicines))}");
        sb.AppendLine("Interaction screen (authoritative, from the pharmacy's curated table):");
        if (alerts.Count == 0) sb.AppendLine("- no listed interactions");
        foreach (var a in alerts) sb.AppendLine(CultureInfo.InvariantCulture, $"- {a.Severity.ToUpperInvariant()}: {a.DrugA} + {a.DrugB} - {a.Effect} Management: {a.Management}");
        return sb.ToString();
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}

// ------------------------------------------------------------------ Admin assistant (tool use)

/// <summary>
/// Admin analytics assistant. The model can ONLY call the read-only, bounded functions in
/// <see cref="AdminInsights"/> (no SQL, no writes); every call and its token cost is audited.
/// </summary>
public sealed class AdminAssistantService(AdminInsights insights, IAnalyticsLlm llm, IAppDbContext db, ICurrentUser user, TimeProvider clock)
{
    public const int MaxTurns = 20;

    internal const string SystemPrompt = """
        You are the analytics assistant inside Medora, a pharmacy chain's back office. You answer questions
        from administrators about sales, orders, stock levels and expiring stock.

        How to work:
        - Get every number from the tools. Never estimate or invent figures; if a tool can't answer the question, say so and say what data would be needed.
        - Money values from tools are integer minor units (cents) in USD; present them as dollars with two decimals.
        - Dates: tools take ISO-8601 timestamps; interpret relative ranges ("last week", "this month") against the current date given below, in UTC.
        - When several independent figures are needed, call the tools in parallel.
        - Keep answers short and scannable: lead with the direct answer, then short lines or "- " bullets (e.g. "Revenue: $447.43 from 21 orders"), then one line on any caveat (e.g. refunds excluded, branch filter applied).
        - Plain text only: the app does not render Markdown, so no tables, no **bold**, no headings.
        - You can't change prices, stock or orders. If asked to, explain which screen in the admin app does it.
        """;

    public async Task<Result<AssistantResponse>> AskAsync(AssistantRequest request, CancellationToken ct)
    {
        var messages = request.Messages;
        if (messages is null || messages.Count is 0 or > MaxTurns) return Errors.Validation($"Send 1 to {MaxTurns} messages.");
        if (messages.Any(m => m.Role is not ("user" or "assistant") || m.Content is not { Length: > 0 and <= 4000 })) return Errors.Validation("Each message needs a role (user/assistant) and 1-4000 characters.");
        if (messages[^1].Role != "user") return Errors.Validation("The last message must be from the user.");
        if (!llm.IsConfigured) return new AssistantResponse("The admin assistant is turned off on this server (Ai:Provider). The dashboard and inventory screens still work.", [], false);

        var now = clock.GetUtcNow();
        var context = $"Current date and time (UTC): {now:yyyy-MM-dd HH:mm}. Week starts Monday.";
        var run = await llm.RunAsync(SystemPrompt, context, messages, BuildTools(), ct);

        db.AuditEntries.Add(new AuditEntry(user.RequireUserId(), "ai.assistant", "-",
            $"q=\"{(messages[^1].Content.Length > 300 ? messages[^1].Content[..300] : messages[^1].Content)}\" tools={string.Join(',', run.ToolCalls.Select(t => t.Name))} in={run.Usage.InputTokens} out={run.Usage.OutputTokens} cache_read={run.Usage.CacheReadTokens}",
            now));
        await db.SaveChangesAsync(ct);
        return new AssistantResponse(run.Answer, run.ToolCalls, true);
    }

    internal IReadOnlyList<AssistantTool> BuildTools()
    {
        const string range = """
            "from": {"type": "string", "description": "Inclusive start, ISO-8601 UTC timestamp, e.g. 2026-11-01T00:00:00Z"},
            "to": {"type": "string", "description": "Exclusive end, ISO-8601 UTC timestamp. Use the current time for 'until now'."},
            "branch_id": {"type": "string", "description": "Optional branch id (brn_...) to filter by; omit for all branches. Use list_branches to find ids."}
            """;
        return
        [
            new("get_sales_summary",
                "Revenue (paid, not cancelled/rejected), number of paid orders, average order value and refunded amount for a time range. Amounts are integer cents (USD).",
                $$"""{"type":"object","properties":{ {{range}} },"required":["from","to"],"additionalProperties":false}""",
                async (i, ct) => await insights.SalesSummaryAsync(Date(i, "from"), Date(i, "to"), Str(i, "branch_id"), ct)),
            new("get_order_status_breakdown",
                "Count of orders created in a time range, grouped by status (pending_payment, awaiting_verification, preparing, ready_for_pickup, out_for_delivery, completed, expired, cancelled, rejected).",
                $$"""{"type":"object","properties":{ {{range}} },"required":["from","to"],"additionalProperties":false}""",
                async (i, ct) => await insights.OrderStatusBreakdownAsync(Date(i, "from"), Date(i, "to"), Str(i, "branch_id"), ct)),
            new("get_top_products",
                "Best-selling products by revenue in a time range, with units sold. Amounts are integer cents (USD).",
                $$"""{"type":"object","properties":{ {{range}}, "limit": {"type":"integer","minimum":1,"maximum":50,"description":"How many products (default 10)."} },"required":["from","to"],"additionalProperties":false}""",
                async (i, ct) => await insights.TopProductsAsync(Date(i, "from"), Date(i, "to"), Str(i, "branch_id"), Int(i, "limit", 10), ct)),
            new("get_low_stock",
                "Products whose available stock (on hand minus reserved for open orders) is at or below the reorder level, most urgent first.",
                """{"type":"object","properties":{"branch_id":{"type":"string","description":"Optional branch id (brn_...)."},"limit":{"type":"integer","minimum":1,"maximum":200,"description":"Max rows (default 50)."}},"additionalProperties":false}""",
                async (i, ct) => await insights.LowStockAsync(Str(i, "branch_id"), Int(i, "limit", 50), ct)),
            new("get_expiring_stock",
                "Stock lines whose earliest batch expires within N days (still on the shelf), soonest first.",
                """{"type":"object","properties":{"within_days":{"type":"integer","minimum":1,"maximum":365,"description":"Look-ahead window in days (default 60)."},"branch_id":{"type":"string","description":"Optional branch id (brn_...)."},"limit":{"type":"integer","minimum":1,"maximum":200,"description":"Max rows (default 50)."}},"additionalProperties":false}""",
                async (i, ct) => await insights.ExpiringStockAsync(Int(i, "within_days", 60), Str(i, "branch_id"), Int(i, "limit", 50), ct)),
            new("list_branches",
                "All pharmacy branches with their ids, names and cities.",
                """{"type":"object","properties":{},"additionalProperties":false}""",
                async (_, ct) => await insights.BranchesAsync(ct)),
        ];
    }

    // Tolerant argument readers: a model-supplied value that is missing or malformed falls back to a safe default.
    private static string? Str(JsonElement i, string name) =>
        i.ValueKind == JsonValueKind.Object && i.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 and <= 64 } s ? s : null;

    private static int Int(JsonElement i, string name, int fallback) =>
        i.ValueKind == JsonValueKind.Object && i.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : fallback;

    private DateTimeOffset Date(JsonElement i, string name) =>
        Str(i, name) is { } s && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : clock.GetUtcNow();
}
