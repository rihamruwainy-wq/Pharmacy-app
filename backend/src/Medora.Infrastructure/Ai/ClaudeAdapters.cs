using System.Text.Json;
using Anthropic;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;
using Medora.Application.Contracts;
using Medora.Application.Features.Ai;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Medora.Infrastructure.Ai;

public sealed class ClaudeSettings
{
    public const string Section = "Claude";
    /// <summary>Opus is used for both features: clinical answers and multi-step analytics both reward the strongest model.</summary>
    public string Model { get; set; } = "claude-opus-5-5";
    /// <summary>Optional; falls back to the ANTHROPIC_API_KEY environment variable (user-secrets / Key Vault in production).</summary>
    public string? ApiKey { get; set; }
    public int MaxTokens { get; set; } = 16000;
    /// <summary>Upper bound on model round trips per assistant question (each tool round trip is one).</summary>
    public int MaxToolTurns { get; set; } = 8;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(90);
}

/// <summary>Single shared client (it pools HTTP connections); null when no credentials are configured.</summary>
internal sealed class ClaudeClientHolder
{
    public ClaudeClientHolder(IOptions<ClaudeSettings> options)
    {
        Settings = options.Value;
        var key = string.IsNullOrWhiteSpace(Settings.ApiKey) ? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") : Settings.ApiKey;
        Client = string.IsNullOrWhiteSpace(key) ? null : new AnthropicClient { ApiKey = key, Timeout = Settings.Timeout, MaxRetries = 2 };
    }

    public ClaudeSettings Settings { get; }
    public AnthropicClient? Client { get; }
}

/// <summary>
/// Pharmacist copilot on Claude. Retrieved monograph sections are sent as DOCUMENT blocks with citations
/// enabled, so every claim in the answer comes back linked to the exact sentence it relies on. The stable
/// system prompt is the cached prefix; the per-question sources and case follow it.
/// </summary>
internal sealed partial class ClaudeClinicalLlm(ClaudeClientHolder holder, ILogger<ClaudeClinicalLlm> logger) : IClinicalLlm
{
    internal const string SystemPrompt = """
        You are the clinical reference assistant for licensed pharmacists working in Medora pharmacies.
        You support - never replace - the pharmacist's professional judgement; they make every dispensing decision.

        Grounding:
        - Answer from the documents provided in the user's message (sections of the pharmacy's drug monographs). Cite them for every clinical statement.
        - If the documents don't cover something the question needs, say so plainly and name what to check (e.g. the product's SmPC, a renal dosing reference, the prescriber). Don't fill gaps from memory for doses, contraindications or interactions.
        - The <case> block contains the order under review and an interaction screen computed by the pharmacy system from its curated table. Treat those alerts as authoritative: explain them and their management, never downgrade or dismiss one. If you notice a concern the screen did not flag (e.g. an allergy or age issue), raise it clearly as something for the pharmacist to verify.

        Answer shape:
        - Lead with the direct answer in one or two sentences (e.g. "Don't dispense without contacting the prescriber" or "Safe to dispense with counselling").
        - Then the key points as short bullets: dose/limits, interactions and their management, counselling points for the patient, what to monitor.
        - Plain text and simple bullets only; no tables or headings. Keep it under about 250 words unless the question needs more.
        - Use generic drug names. Write for a pharmacist, not a patient.
        """;

    public bool IsConfigured => holder.Client is not null;

    public async Task<ClinicalAnswer> AnswerAsync(string question, string caseContext, IReadOnlyList<KnowledgeChunk> sources, CancellationToken cancellationToken)
    {
        var client = holder.Client ?? throw new InvalidOperationException("Claude is not configured.");
        var content = new List<BetaContentBlockParam>(sources.Count + 1);
        foreach (var s in sources)
        {
            content.Add(new BetaRequestDocumentBlock
            {
                Source = new BetaPlainTextSource { Data = s.Content },
                Title = s.Title,
                Context = $"Source id {s.Id}. {s.Provenance}",
                Citations = new BetaCitationsConfigParam { Enabled = true },
            });
        }
        content.Add(new BetaTextBlockParam { Text = $"<case>\n{caseContext}\n</case>\n\n<question>\n{question}\n</question>" });

        var response = await client.Beta.Messages.Create(new MessageCreateParams
        {
            Model = holder.Settings.Model,
            MaxTokens = holder.Settings.MaxTokens,
            // Refusal fallback: a policy decline is re-served by a fallback model inside the same call.
            Betas = [AnthropicBeta.ServerSideFallback2026_07_01],
            Fallbacks = new Default(),
            OutputConfig = new BetaOutputConfig { Effort = Effort.High }, // clinical accuracy over latency
            System = new List<BetaTextBlockParam> { new() { Text = SystemPrompt, CacheControl = new BetaCacheControlEphemeral() } },
            Messages = [new BetaUserMessageParam { Content = content }],
        }, cancellationToken);

        var usage = ClaudeUsage.From(response);
        if (response.StopReason == "refusal")
        {
            LogRefusal(logger, "copilot");
            return new ClinicalAnswer("I can't answer that request. Please consult the product's SmPC or a senior pharmacist.", [], usage);
        }

        var text = new System.Text.StringBuilder();
        var citations = new List<LlmCitation>();
        foreach (var block in response.Content)
        {
            if (!block.TryPickText(out var t)) continue;
            text.Append(t.Text);
            if (t.Citations is null) continue;
            foreach (var c in t.Citations)
            {
                if (c.TryPickCitationCharLocation(out var loc) && loc.DocumentIndex >= 0 && loc.DocumentIndex < sources.Count)
                {
                    citations.Add(new LlmCitation(sources[(int)loc.DocumentIndex].Id, loc.CitedText));
                }
            }
        }
        if (response.StopReason == "max_tokens") text.Append("\n\n[Answer truncated - ask a narrower question.]");
        return new ClinicalAnswer(text.ToString().Trim(), citations.DistinctBy(c => (c.SourceId, c.Quote)).ToList(), usage);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Claude refused a {Feature} request")]
    private static partial void LogRefusal(ILogger logger, string feature);
}

/// <summary>
/// Admin analytics assistant: a manual, bounded tool-use loop over read-only server functions.
/// Independent tool calls in one turn run in parallel and all results go back in ONE user message.
/// Thinking blocks are passed back unchanged (required for preserved thinking).
/// </summary>
internal sealed partial class ClaudeAnalyticsLlm(ClaudeClientHolder holder, ILogger<ClaudeAnalyticsLlm> logger) : IAnalyticsLlm
{
    private static readonly JsonSerializerOptions ToolJson = new(JsonSerializerDefaults.Web);

    public bool IsConfigured => holder.Client is not null;

    public async Task<AssistantRun> RunAsync(string systemPrompt, string dynamicContext, IReadOnlyList<ChatTurn> history, IReadOnlyList<AssistantTool> tools, CancellationToken cancellationToken)
    {
        var client = holder.Client ?? throw new InvalidOperationException("Claude is not configured.");
        var byName = tools.ToDictionary(t => t.Name, StringComparer.Ordinal);
        var toolDefs = tools.Select(ToBetaTool).ToList();

        var messages = new List<BetaMessageParam>(history.Count + 8);
        foreach (var turn in history)
        {
            messages.Add(turn.Role == "user" ? new BetaUserMessageParam { Content = turn.Content } : new BetaAssistantMessageParam { Content = turn.Content });
        }

        var calls = new List<ToolCallDto>();
        var usage = new LlmUsage(0, 0, 0, 0);
        for (var turn = 0; turn < holder.Settings.MaxToolTurns; turn++)
        {
            var response = await client.Beta.Messages.Create(new MessageCreateParams
            {
                Model = holder.Settings.Model,
                MaxTokens = holder.Settings.MaxTokens,
                Betas = [AnthropicBeta.ServerSideFallback2026_07_01],
                Fallbacks = new Default(),
                OutputConfig = new BetaOutputConfig { Effort = Effort.Medium },
                // Stable prompt + tools form the cached prefix; the volatile date goes AFTER the breakpoint.
                System = new List<BetaTextBlockParam>
                {
                    new() { Text = systemPrompt, CacheControl = new BetaCacheControlEphemeral() },
                    new() { Text = dynamicContext },
                },
                Tools = toolDefs,
                Messages = messages,
            }, cancellationToken);
            usage = ClaudeUsage.Add(usage, ClaudeUsage.From(response));

            if (response.StopReason == "refusal")
            {
                LogRefusal(logger, "assistant");
                return new AssistantRun("I can't help with that request.", calls, usage);
            }

            var assistantBlocks = new List<BetaContentBlockParam>();
            var pending = new List<(string Id, string Name, JsonElement Input)>();
            var text = new System.Text.StringBuilder();
            foreach (var block in response.Content)
            {
                if (block.TryPickText(out var t))
                {
                    text.Append(t.Text);
                    assistantBlocks.Add(new BetaTextBlockParam { Text = t.Text });
                }
                else if (block.TryPickThinking(out var thinking))
                {
                    assistantBlocks.Add(new BetaThinkingBlockParam { Thinking = thinking.Thinking, Signature = thinking.Signature });
                }
                else if (block.TryPickRedactedThinking(out var redacted))
                {
                    assistantBlocks.Add(new BetaRedactedThinkingBlockParam { Data = redacted.Data });
                }
                else if (block.TryPickToolUse(out var use))
                {
                    assistantBlocks.Add(new BetaToolUseBlockParam { ID = use.ID, Name = use.Name, Input = use.Input });
                    pending.Add((use.ID, use.Name, JsonSerializer.SerializeToElement(use.Input)));
                }
            }

            if (pending.Count == 0 || response.StopReason != "tool_use")
            {
                var answer = text.ToString().Trim();
                if (response.StopReason == "max_tokens") answer += "\n\n[Answer truncated.]";
                return new AssistantRun(answer.Length == 0 ? "I couldn't produce an answer. Try rephrasing the question." : answer, calls, usage);
            }

            messages.Add(new BetaAssistantMessageParam { Content = assistantBlocks });
            var results = await Task.WhenAll(pending.Select(p => ExecuteAsync(byName, p.Id, p.Name, p.Input, cancellationToken)));
            calls.AddRange(pending.Select(p => new ToolCallDto(p.Name, p.Input.GetRawText())));
            messages.Add(new BetaUserMessageParam { Content = results.Select(r => (BetaContentBlockParam)r).ToList() });
        }

        LogTurnLimit(logger, holder.Settings.MaxToolTurns);
        return new AssistantRun("That question needed more steps than allowed. Try asking for one figure at a time.", calls, usage);
    }

    private static async Task<BetaToolResultBlockParam> ExecuteAsync(Dictionary<string, AssistantTool> tools, string id, string name, JsonElement input, CancellationToken ct)
    {
        if (!tools.TryGetValue(name, out var tool))
        {
            return new BetaToolResultBlockParam { ToolUseID = id, Content = $"Unknown tool '{name}'.", IsError = true };
        }
        try
        {
            var result = await tool.Execute(input, ct);
            return new BetaToolResultBlockParam { ToolUseID = id, Content = JsonSerializer.Serialize(result, ToolJson) };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A failed tool is reported to the model (it can retry or explain), never dropped.
            return new BetaToolResultBlockParam { ToolUseID = id, Content = $"Tool failed: {ex.GetType().Name}.", IsError = true };
        }
    }

    private static BetaToolUnion ToBetaTool(AssistantTool tool)
    {
        using var schema = JsonDocument.Parse(tool.InputSchemaJson);
        var root = schema.RootElement;
        var properties = root.TryGetProperty("properties", out var p)
            ? p.EnumerateObject().ToDictionary(x => x.Name, x => x.Value.Clone(), StringComparer.Ordinal)
            : [];
        var required = root.TryGetProperty("required", out var r) ? r.EnumerateArray().Select(x => x.GetString()!).ToList() : [];
        return new BetaTool
        {
            Name = tool.Name,
            Description = tool.Description,
            InputSchema = new() { Properties = properties, Required = required },
        };
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Claude refused a {Feature} request")]
    private static partial void LogRefusal(ILogger logger, string feature);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Admin assistant hit the tool-turn limit ({Limit})")]
    private static partial void LogTurnLimit(ILogger logger, int limit);
}

internal static class ClaudeUsage
{
    public static LlmUsage From(BetaMessage m) =>
        new(m.Usage.InputTokens, m.Usage.OutputTokens, m.Usage.CacheReadInputTokens ?? 0, m.Usage.CacheCreationInputTokens ?? 0);

    public static LlmUsage Add(LlmUsage a, LlmUsage b) =>
        new(a.InputTokens + b.InputTokens, a.OutputTokens + b.OutputTokens, a.CacheReadTokens + b.CacheReadTokens, a.CacheWriteTokens + b.CacheWriteTokens);
}
