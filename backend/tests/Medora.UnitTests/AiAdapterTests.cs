using System.Text.Json.Nodes;
using Medora.Application.Features.Ai;
using Medora.Infrastructure.Ai;

namespace Medora.UnitTests;

public class CitationResolutionTests
{
    private static readonly KnowledgeChunk[] Sources =
    [
        new("kb_a", "warfarin", "Warfarin - Interactions", "NSAIDs increase bleeding risk. Clarithromycin can raise the INR markedly.", "demo"),
        new("kb_b", "simvastatin", "Simvastatin - Interactions", "Clarithromycin is contraindicated. Suspend simvastatin during the course.", "demo"),
    ];

    [Fact]
    public void Renumbers_markers_by_first_use_and_quotes_the_best_supporting_sentence()
    {
        var (text, citations) = OpenAiClinicalLlm.ResolveCitations(
            "Don't dispense yet. Clarithromycin is contraindicated with simvastatin [S2]. It also raises the INR on warfarin [S1].", Sources);

        Assert.Equal("Don't dispense yet. Clarithromycin is contraindicated with simvastatin [1]. It also raises the INR on warfarin [2].", text);
        Assert.Equal(["kb_b", "kb_a"], citations.Select(c => c.SourceId));
        Assert.Equal("Clarithromycin is contraindicated.", citations[0].Quote);
        Assert.Equal("Clarithromycin can raise the INR markedly.", citations[1].Quote);
    }

    [Fact]
    public void Markers_pointing_at_missing_sources_are_dropped_not_trusted()
    {
        var (text, citations) = OpenAiClinicalLlm.ResolveCitations("Use paracetamol instead [S7].", Sources);
        Assert.Equal("Use paracetamol instead .", text);
        Assert.Empty(citations);
    }

    [Fact]
    public void Markdown_is_flattened_to_plain_text()
    {
        const string md = "**Sales:**\n\n| Period | Revenue |\n|---|---|\n| This week | **$447.43** |\n## Note\n*Refunds excluded.*";
        Assert.Equal("Sales:\n\nPeriod · Revenue\nThis week · $447.43\nNote\nRefunds excluded.", PlainText.From(md));
    }

    [Theory]
    [InlineData("{\"limit\": 5}")]
    public void Tool_arguments_accept_json_strings(string json) =>
        Assert.Equal(5, OpenAiAnalyticsLlm.ParseArguments(JsonValue.Create(json)).GetProperty("limit").GetInt32());

    [Fact]
    public void Tool_arguments_accept_objects_and_tolerate_garbage()
    {
        Assert.Equal(3, OpenAiAnalyticsLlm.ParseArguments(new JsonObject { ["limit"] = 3 }).GetProperty("limit").GetInt32());
        Assert.Equal(System.Text.Json.JsonValueKind.Object, OpenAiAnalyticsLlm.ParseArguments(JsonValue.Create("{not json")).ValueKind);
    }
}
