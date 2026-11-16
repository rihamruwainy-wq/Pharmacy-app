using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Medora.Application.Contracts;
using Medora.Application.Features.Ai;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Medora.Infrastructure.Ai;

/// <summary>
/// Which LLM backs the staff AI features. Switching provider is configuration only:
/// <list type="bullet">
/// <item><c>openai</c>: any OpenAI-compatible chat-completions endpoint. Free and local: Ollama
/// (<c>http://localhost:11434/v1</c>), LM Studio, vLLM. Hosted: OpenAI, Azure OpenAI, Groq, OpenRouter, Together...</item>
/// <item><c>claude</c>: Anthropic's API via the official SDK (native citations, server-side refusal fallbacks).</item>
/// <item><c>none</c>: AI disabled; features return their deterministic results only.</item>
/// </list>
/// </summary>
public sealed class AiSettings
{
    public const string Section = "Ai";
    public string Provider { get; set; } = "none";
    public string BaseUrl { get; set; } = "http://localhost:11434/v1";
    public string Model { get; set; } = "qwen2.5:3b";
    /// <summary>Optional (local runners need none). From user-secrets / Key Vault, never source control.</summary>
    public string? ApiKey { get; set; }
    public int MaxTokens { get; set; } = 1500;
    public double Temperature { get; set; } = 0.1;
    public int MaxToolTurns { get; set; } = 6;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(4);
}

/// <summary>Thin typed client for POST {BaseUrl}/chat/completions (the de-facto standard LLM API).</summary>
internal sealed class ChatCompletionsClient(HttpClient http, IOptions<AiSettings> options)
{
    private readonly AiSettings _settings = options.Value;

    public async Task<JsonObject> CompleteAsync(JsonArray messages, JsonArray? tools, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = _settings.Model,
            ["messages"] = messages.DeepClone(),
            ["max_tokens"] = _settings.MaxTokens,
            ["temperature"] = _settings.Temperature,
            ["stream"] = false,
        };
        if (tools is { Count: > 0 })
        {
            body["tools"] = tools.DeepClone();
            body["tool_choice"] = "auto";
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_settings.BaseUrl.TrimEnd('/')}/chat/completions") { Content = JsonContent.Create(body) };
        if (!string.IsNullOrWhiteSpace(_settings.ApiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);
        using var response = await http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new LlmProviderException($"LLM provider returned {(int)response.StatusCode}: {(text.Length > 300 ? text[..300] : text)}", (int)response.StatusCode);
        }
        return JsonNode.Parse(text)?.AsObject() ?? throw new LlmProviderException("LLM provider returned an empty body.", 502);
    }

    public static LlmUsage Usage(JsonObject response)
    {
        var u = response["usage"];
        return new LlmUsage(
            u?["prompt_tokens"]?.GetValue<long>() ?? 0,
            u?["completion_tokens"]?.GetValue<long>() ?? 0,
            u?["prompt_tokens_details"]?["cached_tokens"]?.GetValue<long>() ?? 0,
            0);
    }
}

/// <summary>Provider failure (unreachable, overloaded, bad config). Mapped to 503 AI_UNAVAILABLE by the API.</summary>
public sealed class LlmProviderException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

/// <summary>
/// Grounded clinical answers on any OpenAI-compatible model. These APIs have no native citations, so the
/// sources are numbered [S1..Sn] and the model is told to cite them inline. Citations are then VERIFIED:
/// only markers that point at a supplied source survive, and each one is paired with the sentence from that
/// source that best matches the claim (so the pharmacist sees the actual supporting text).
/// </summary>
internal sealed partial class OpenAiClinicalLlm(ChatCompletionsClient client, IOptions<AiSettings> options, ILogger<OpenAiClinicalLlm> logger) : IClinicalLlm
{
    internal const string SystemPrompt = """
        You are the clinical reference assistant for licensed pharmacists in Medora pharmacies. You support, never replace, the pharmacist's judgement.

        Rules:
        - Answer ONLY from the numbered sources in the user's message. After every sentence that uses a source, cite it like [S2]. Never cite a number that isn't listed.
        - If the sources don't cover what the question needs, say so and name what to check (the product label/SmPC, the prescriber). Never invent doses, contraindications or interactions.
        - The CASE block includes an interaction screen computed by the pharmacy system. Treat those alerts as authoritative: explain them and their management; never downgrade or dismiss one. If you notice another concern (allergy, age), flag it for the pharmacist to verify.

        Format: first a one or two sentence direct answer (e.g. "Don't dispense without contacting the prescriber."), then short "- " bullet points (dose/limits, interactions and management, counselling, monitoring). Plain text only (the app does not render Markdown: no **bold**, no tables, no headings), under 220 words, generic drug names.
        """;

    public bool IsConfigured => true;

    public async Task<ClinicalAnswer> AnswerAsync(string question, string caseContext, IReadOnlyList<KnowledgeChunk> sources, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder("SOURCES\n");
        for (var i = 0; i < sources.Count; i++) sb.Append("[S").Append(i + 1).Append("] ").Append(sources[i].Title).Append('\n').Append(sources[i].Content).Append("\n\n");
        sb.Append("CASE\n").Append(caseContext).Append("\n\nQUESTION\n").Append(question);

        var response = await client.CompleteAsync(
            [new JsonObject { ["role"] = "system", ["content"] = SystemPrompt }, new JsonObject { ["role"] = "user", ["content"] = sb.ToString() }],
            null, cancellationToken);
        var message = response["choices"]?[0]?["message"];
        var raw = message?["content"]?.GetValue<string>() ?? string.Empty;
        if (response["choices"]?[0]?["finish_reason"]?.GetValue<string>() == "length") raw += "\n\n[Answer truncated - ask a narrower question.]";
        LogAnswered(logger, options.Value.Model, raw.Length);

        var (text, citations) = ResolveCitations(PlainText.From(raw), sources);
        return new ClinicalAnswer(text, citations, ChatCompletionsClient.Usage(response));
    }

    /// <summary>
    /// Keeps only markers that reference a real source, renumbers them in order of first use ([S4] -> [1]) so
    /// the answer matches the app's citation list, and attaches the best-supporting sentence from each source.
    /// </summary>
    internal static (string Text, IReadOnlyList<LlmCitation> Citations) ResolveCitations(string raw, IReadOnlyList<KnowledgeChunk> sources)
    {
        var order = new List<int>();
        var claimFor = new Dictionary<int, string>();
        foreach (var sentence in SentenceSplit().Split(raw))
        {
            foreach (Match m in Marker().Matches(sentence))
            {
                var n = int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                if (n < 1 || n > sources.Count) continue;
                if (!order.Contains(n)) order.Add(n);
                claimFor[n] = claimFor.GetValueOrDefault(n) + " " + sentence;
            }
        }

        var text = Marker().Replace(raw, m =>
        {
            var n = int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            var idx = order.IndexOf(n);
            return idx < 0 ? string.Empty : $"[{idx + 1}]"; // drop markers that point at nothing
        });

        var citations = order.Select(n => new LlmCitation(sources[n - 1].Id, BestSentence(sources[n - 1].Content, claimFor[n]))).ToList();
        return (text.Trim(), citations);
    }

    private static string BestSentence(string source, string claim)
    {
        var claimTerms = KnowledgeIndex.Tokenize(claim).ToHashSet(StringComparer.Ordinal);
        return SentenceSplit().Split(source)
            .Where(s => s.Length > 10)
            .OrderByDescending(s => KnowledgeIndex.Tokenize(s).Count(claimTerms.Contains))
            .FirstOrDefault()?.Trim() ?? source;
    }

    [GeneratedRegex(@"\[S(\d{1,2})\]")]
    private static partial Regex Marker();

    [GeneratedRegex(@"(?<=[.!?])\s+|\n+")]
    private static partial Regex SentenceSplit();

    [LoggerMessage(Level = LogLevel.Information, Message = "Clinical answer from {Model} ({Length} chars)")]
    private static partial void LogAnswered(ILogger logger, string model, int length);
}

/// <summary>
/// Admin analytics assistant on any OpenAI-compatible model with function calling (Ollama: qwen2.5, llama3.1+,
/// mistral-nemo...). Same contract as the Claude adapter: bounded loop, read-only tools, failed tools reported back.
/// </summary>
internal sealed partial class OpenAiAnalyticsLlm(ChatCompletionsClient client, IOptions<AiSettings> options, ILogger<OpenAiAnalyticsLlm> logger) : IAnalyticsLlm
{
    private static readonly JsonSerializerOptions ToolJson = new(JsonSerializerDefaults.Web);

    public bool IsConfigured => true;

    public async Task<AssistantRun> RunAsync(string systemPrompt, string dynamicContext, IReadOnlyList<ChatTurn> history, IReadOnlyList<AssistantTool> tools, CancellationToken cancellationToken)
    {
        var byName = tools.ToDictionary(t => t.Name, StringComparer.Ordinal);
        var toolDefs = new JsonArray(tools.Select(t => (JsonNode)new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject { ["name"] = t.Name, ["description"] = t.Description, ["parameters"] = JsonNode.Parse(t.InputSchemaJson) },
        }).ToArray());

        var messages = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = $"{systemPrompt}\n\n{dynamicContext}" } };
        foreach (var turn in history) messages.Add(new JsonObject { ["role"] = turn.Role, ["content"] = turn.Content });

        var calls = new List<ToolCallDto>();
        var usage = new LlmUsage(0, 0, 0, 0);
        for (var turn = 0; turn < options.Value.MaxToolTurns; turn++)
        {
            var response = await client.CompleteAsync(messages, toolDefs, cancellationToken);
            var u = ChatCompletionsClient.Usage(response);
            usage = new LlmUsage(usage.InputTokens + u.InputTokens, usage.OutputTokens + u.OutputTokens, usage.CacheReadTokens + u.CacheReadTokens, 0);

            var message = response["choices"]?[0]?["message"]?.AsObject() ?? throw new LlmProviderException("LLM provider returned no message.", 502);
            var toolCalls = message["tool_calls"]?.AsArray();
            if (toolCalls is null || toolCalls.Count == 0)
            {
                var answer = PlainText.From(message["content"]?.GetValue<string>() ?? string.Empty).Trim();
                return new AssistantRun(answer.Length == 0 ? "I couldn't produce an answer. Try rephrasing the question." : answer, calls, usage);
            }

            messages.Add(message.DeepClone()); // assistant turn with its tool_calls, echoed back verbatim
            var results = await Task.WhenAll(toolCalls.Select(async (node, i) =>
            {
                var id = node?["id"]?.GetValue<string>() ?? $"call_{turn}_{i}";
                var name = node?["function"]?["name"]?.GetValue<string>() ?? string.Empty;
                var args = ParseArguments(node?["function"]?["arguments"]);
                calls.Add(new ToolCallDto(name, args.GetRawText()));
                return new JsonObject { ["role"] = "tool", ["tool_call_id"] = id, ["name"] = name, ["content"] = await ExecuteAsync(byName, name, args, cancellationToken) };
            }));
            foreach (var r in results) messages.Add(r);
        }

        LogTurnLimit(logger, options.Value.MaxToolTurns);
        return new AssistantRun("That question needed more steps than allowed. Try asking for one figure at a time.", calls, usage);
    }

    /// <summary>OpenAI sends arguments as a JSON string; some local runners send an object. Accept both; bad JSON becomes {}.</summary>
    internal static JsonElement ParseArguments(JsonNode? node)
    {
        try
        {
            return node switch
            {
                JsonValue v when v.TryGetValue<string>(out var s) => JsonDocument.Parse(string.IsNullOrWhiteSpace(s) ? "{}" : s).RootElement.Clone(),
                JsonObject o => JsonDocument.Parse(o.ToJsonString()).RootElement.Clone(),
                _ => JsonDocument.Parse("{}").RootElement.Clone(),
            };
        }
        catch (JsonException)
        {
            return JsonDocument.Parse("{}").RootElement.Clone();
        }
    }

    private static async Task<string> ExecuteAsync(Dictionary<string, AssistantTool> tools, string name, JsonElement input, CancellationToken ct)
    {
        if (!tools.TryGetValue(name, out var tool)) return $"Error: unknown tool '{name}'. Available: {string.Join(", ", tools.Keys)}.";
        try
        {
            return JsonSerializer.Serialize(await tool.Execute(input, ct), ToolJson);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return $"Error: tool failed ({ex.GetType().Name}).";
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Admin assistant hit the tool-turn limit ({Limit})")]
    private static partial void LogTurnLimit(ILogger logger, int limit);
}

/// <summary>
/// Safety net for models that ignore "plain text": strips Markdown emphasis/headings and turns table rows into
/// readable lines, so the mobile app (which renders plain text) never shows raw ** or | characters.
/// </summary>
internal static partial class PlainText
{
    public static string From(string markdown)
    {
        var lines = new List<string>();
        foreach (var raw in markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = raw.TrimEnd();
            if (TableSeparator().IsMatch(line)) continue;                       // |---|---|
            if (line.TrimStart().StartsWith('|'))                               // | a | b |  ->  a · b
            {
                line = string.Join(" · ", line.Trim().Trim('|').Split('|').Select(c => c.Trim()).Where(c => c.Length > 0));
            }
            line = Heading().Replace(line, string.Empty);                       // ## Title -> Title
            line = Emphasis().Replace(line, "$2");                              // **x** / __x__ / *x* -> x
            lines.Add(line);
        }
        return string.Join('\n', lines).Trim();
    }

    [GeneratedRegex(@"^\s*\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?\s*$")]
    private static partial Regex TableSeparator();

    [GeneratedRegex(@"^\s{0,3}#{1,6}\s+")]
    private static partial Regex Heading();

    [GeneratedRegex(@"(\*\*|__|\*(?=\S))(.+?)\1")]
    private static partial Regex Emphasis();
}

/// <summary>Used when Ai:Provider is "none": the services return their deterministic results with aiAvailable=false.</summary>
internal sealed class DisabledLlm : IClinicalLlm, IAnalyticsLlm
{
    public bool IsConfigured => false;
    public Task<ClinicalAnswer> AnswerAsync(string question, string caseContext, IReadOnlyList<KnowledgeChunk> sources, CancellationToken cancellationToken) => throw new InvalidOperationException("AI is disabled.");
    public Task<AssistantRun> RunAsync(string systemPrompt, string dynamicContext, IReadOnlyList<ChatTurn> history, IReadOnlyList<AssistantTool> tools, CancellationToken cancellationToken) => throw new InvalidOperationException("AI is disabled.");
}
