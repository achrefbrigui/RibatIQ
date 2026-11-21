using RibatIQ.Application.Ai;
using RibatIQ.Application.Reviewing;
using RibatIQ.Application.Sources;
using RibatIQ.Domain.Findings;

namespace RibatIQ.Application.Tests;

public sealed class AiFindingParserTests
{
    private static readonly AiOptions Options = AiOptions.Default;

    // a.cs has 30 lines, all shown (scan).
    private static readonly AiContext ScanContext = AiContextBuilder.ForScan(
        [new SourceDocument("src/a.cs", string.Join('\n', Enumerable.Range(1, 30).Select(i => $"line {i}")))], Options);

    [Fact]
    public void Parse_ValidFinding_BecomesAnAiFinding()
    {
        var result = Parse(Item(startLine: 5, endLine: 7, confidence: "0.8", fix: "\"Use a using statement.\""));

        var finding = Assert.Single(result.Findings);
        Assert.Equal("Resource leak", finding.Title);
        Assert.Equal(FindingSeverity.High, finding.Severity);
        Assert.Equal(FindingCategory.Reliability, finding.Category);
        Assert.Equal("src/a.cs", finding.FilePath);
        Assert.Equal(5, finding.StartLine);
        Assert.Equal(7, finding.EndLine);
        Assert.Equal("Use a using statement.", finding.SuggestedFix);
        Assert.Equal(0.8, finding.Confidence);
        Assert.Equal(FindingOrigin.Ai, finding.Origin);
        Assert.Equal("openai", finding.Source);
        Assert.Null(finding.RuleId);
        Assert.Equal(0, result.Rejected);
    }

    [Fact]
    public void Parse_EmptyFindings_IsValid() =>
        Assert.Empty(AiFindingParser.Parse("""{ "findings": [] }""", "openai", ScanContext, Options).Findings);

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{ \"findings\": [ ")]
    [InlineData("[]")]
    [InlineData("""{ "results": [] }""")]
    [InlineData("""{ "findings": "none" }""")]
    public void Parse_MalformedResponse_ThrowsMalformedResponse(string text)
    {
        var ex = Assert.Throws<LlmProviderException>(() => AiFindingParser.Parse(text, "openai", ScanContext, Options));
        Assert.Equal(LlmErrorKind.MalformedResponse, ex.Kind);
    }

    [Fact]
    public void Parse_JsonInsideCodeFence_IsAccepted()
    {
        var text = "```json\n{ \"findings\": [" + Item() + "] }\n```";

        Assert.Single(AiFindingParser.Parse(text, "openai", ScanContext, Options).Findings);
    }

    [Theory]
    [InlineData("""{ "title": "t" }""")]
    [InlineData("42")]
    [InlineData("""{ "title": "", "description": "d", "severity": "high", "category": "security", "file": "src/a.cs", "startLine": 1, "endLine": null, "suggestedFix": null, "confidence": 0.5 }""")]
    [InlineData("""{ "title": "t", "description": "d", "severity": "catastrophic", "category": "security", "file": "src/a.cs", "startLine": 1, "endLine": null, "suggestedFix": null, "confidence": 0.5 }""")]
    [InlineData("""{ "title": "t", "description": "d", "severity": "high", "category": "style", "file": "src/a.cs", "startLine": 1, "endLine": null, "suggestedFix": null, "confidence": 0.5 }""")]
    [InlineData("""{ "title": "t", "description": "d", "severity": "high", "category": "security", "file": "src/a.cs", "startLine": "1", "endLine": null, "suggestedFix": null, "confidence": 0.5 }""")]
    [InlineData("""{ "title": "t", "description": "d", "severity": "high", "category": "security", "file": "src/a.cs", "startLine": 1.5, "endLine": null, "suggestedFix": null, "confidence": 0.5 }""")]
    public void Parse_MalformedEntries_AreRejectedIndividually(string badItem)
    {
        var result = Parse(badItem, Item());

        Assert.Single(result.Findings);
        Assert.Equal(1, result.Rejected);
    }

    [Theory]
    [InlineData("src/other.cs")]
    [InlineData("/etc/passwd")]
    [InlineData("C:/repo/src/a.cs")]
    [InlineData("../src/a.cs")]
    [InlineData("")]
    public void Parse_FileNotSentToTheModel_IsRejected(string file) =>
        Assert.Equal(1, Parse(Item(file: file)).Rejected);

    [Fact]
    public void Parse_EquivalentPathSpelling_IsAccepted() =>
        Assert.Single(Parse(Item(file: @".\src\a.cs")).Findings);

    [Theory]
    [InlineData(0, null)]
    [InlineData(-1, null)]
    [InlineData(31, null)]
    [InlineData(29, 31)]
    [InlineData(10, 9)]
    public void Parse_LinesOutsideWhatWasShown_AreRejected(int startLine, int? endLine) =>
        Assert.Equal(1, Parse(Item(startLine: startLine, endLine: endLine)).Rejected);

    [Fact]
    public void Parse_EndLineEqualToStartLine_IsStoredAsSingleLine() =>
        Assert.Null(Assert.Single(Parse(Item(startLine: 4, endLine: 4)).Findings).EndLine);

    [Fact]
    public void Parse_CriticalSeverity_IsCappedAtHigh() =>
        Assert.Equal(FindingSeverity.High, Assert.Single(Parse(Item(severity: "critical")).Findings).Severity);

    [Theory]
    [InlineData("1.5")]
    [InlineData("-0.1")]
    [InlineData("\"0.9\"")]
    [InlineData("null")]
    public void Parse_InvalidConfidence_IsDropped_NotTheFinding(string confidence) =>
        Assert.Null(Assert.Single(Parse(Item(confidence: confidence)).Findings).Confidence);

    [Fact]
    public void Parse_OverlongTitle_IsRejected() =>
        Assert.Equal(1, Parse(Item(title: new string('x', ReviewFinding.MaxTitleLength + 1))).Rejected);

    [Fact]
    public void Parse_MoreFindingsThanAllowed_AreTruncated()
    {
        var items = Enumerable.Range(1, 5).Select(i => Item(startLine: i)).ToArray();

        var result = AiFindingParser.Parse(
            "{ \"findings\": [" + string.Join(',', items) + "] }", "openai", ScanContext, new AiOptions(MaxFindings: 3));

        Assert.Equal(3, result.Findings.Count);
        Assert.Equal(2, result.Truncated);
    }

    [Fact]
    public void Parse_ForReview_RequiresATouchedChangedLine()
    {
        var text = string.Join('\n', Enumerable.Range(1, 60).Select(i => $"line {i}"));
        var change = new FileChange("src/a.cs", null, ChangeKind.Modified, [new LineRange(30, 31)]);
        var context = AiContextBuilder.ForReview([new SourceDocument("src/a.cs", text)], [change], new AiOptions(ReviewContextLines: 5));

        var onChange = Item(startLine: 31);
        var spanningChange = Item(startLine: 27, endLine: 30, title: "Spanning");
        var contextOnly = Item(startLine: 26, title: "Context only");
        var notShown = Item(startLine: 50, title: "Not shown");

        var result = AiFindingParser.Parse(
            "{ \"findings\": [" + string.Join(',', onChange, spanningChange, contextOnly, notShown) + "] }", "anthropic", context, Options);

        Assert.Equal([31, 27], result.Findings.Select(f => f.StartLine!.Value));
        Assert.Equal(2, result.Rejected);
    }

    private static AiParseResult Parse(params string[] items) =>
        AiFindingParser.Parse("{ \"findings\": [" + string.Join(',', items) + "] }", "openai", ScanContext, Options);

    private static string Item(
        string title = "Resource leak",
        string severity = "high",
        string file = "src/a.cs",
        int startLine = 5,
        int? endLine = null,
        string confidence = "0.7",
        string fix = "null") =>
        $$"""
        {
          "title": {{System.Text.Json.JsonSerializer.Serialize(title)}},
          "description": "The stream is never disposed.",
          "severity": "{{severity}}",
          "category": "reliability",
          "file": {{System.Text.Json.JsonSerializer.Serialize(file)}},
          "startLine": {{startLine}},
          "endLine": {{(endLine?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null")}},
          "suggestedFix": {{fix}},
          "confidence": {{confidence}}
        }
        """;
}
