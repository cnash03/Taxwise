using TaxWise.Rag.Answering;

namespace TaxWise.Rag.Tests.Answering;

public sealed class AnswerPromptTests
{
    [Fact]
    public void SystemPrompt_FillsYearAndStyle_AndIsByteStable()
    {
        var a = AnswerPrompt.SystemPrompt(2025, plainLanguage: false);

        Assert.Contains("for tax year 2025.", a);
        Assert.Contains("Use the passage's terminology.", a);
        Assert.DoesNotContain("{", a);
        Assert.Equal(a, AnswerPrompt.SystemPrompt(2025, plainLanguage: false));
    }

    [Fact]
    public void PlainLanguage_ChangesOnlyTheWritingStyle()
    {
        var normal = AnswerPrompt.SystemPrompt(2025, false);
        var plain = AnswerPrompt.SystemPrompt(2025, true);

        Assert.Contains("8th-grade reading level", plain);
        var sharedPrefix = normal[..normal.IndexOf("Write clearly", StringComparison.Ordinal)];
        Assert.StartsWith(sharedPrefix, plain);
    }

    [Fact]
    public void UserMessage_LabelsPassagesInOrder_ThenTheQuestion()
    {
        var msg = AnswerPrompt.UserMessage("Is my scholarship taxable?", [TestChunks.Make(7, "Seven."), TestChunks.Make(3, "Three.")]);

        Assert.Contains("<passage id=\"P1\" source=\"Pub 970 · Ch. 1 · p. 7 · TY 2025\">\nSeven.\n</passage>", msg);
        Assert.Contains("<passage id=\"P2\" source=\"Pub 970 · Ch. 1 · p. 3 · TY 2025\">\nThree.\n</passage>", msg);
        Assert.EndsWith("<question>Is my scholarship taxable?</question>", msg);
    }

    [Theory]
    [InlineData("Chapter 12. Other Education Benefits", "Ch. 12")]
    [InlineData("Introduction", "Introduction")]
    [InlineData(null, null)]
    public void ShortChapter(string? chapter, string? expected) => Assert.Equal(expected, AnswerPrompt.ShortChapter(chapter));

    [Fact]
    public void Schema_LimitsPassageIdsToP1ThroughP4()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(AnswerPrompt.Schema);

        Assert.Contains("\"enum\":[\"P1\",\"P2\",\"P3\",\"P4\"]", json);
        Assert.Contains("\"additionalProperties\":false", json);
    }
}
