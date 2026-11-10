using TaxWise.Core.Guardrails;

namespace TaxWise.Core.Tests.Guardrails;

public sealed class CitationValidatorTests
{
    private static readonly string[] Passages =
    [
        "A scholarship is generally an amount paid for the benefit of a student. Amounts used for room and board are taxable.",
        "You must include in gross income amounts used for incidental expenses.",
        "Qualified education expenses include tuition and fees required for enrollment.",
        "Generally, a fellowship grant is an amount paid for the benefit of an individual to aid in the pursuit of study.",
    ];

    private static DraftAnswer Answer(params DraftParagraph[] paragraphs) => new(DraftAnswer.Answered, paragraphs);
    private static DraftParagraph Para(string text, params DraftCitation[] c) => new(text, c);
    private static DraftCitation Cite(string passage, string quote) => new(passage, quote);

    [Fact]
    public void ValidCitation_KeepsParagraphAndHighlight()
    {
        var result = CitationValidator.Validate(
            Answer(Para("Room and board is taxable.", Cite("P1", "Amounts used for room and board are taxable."))), Passages);

        Assert.False(result.IsRefusal);
        var c = Assert.Single(Assert.Single(result.Paragraphs).Citations);
        Assert.Equal(1, c.PassageNumber);
        Assert.Equal("Amounts used for room and board are taxable.", c.Highlight);
        Assert.Equal(ValidationStats.Ok, result.Stats.Outcome);
    }

    [Theory]
    [InlineData("P5")]  // only 4 passages were sent
    [InlineData("P0")]
    [InlineData("X1")]
    [InlineData("")]
    public void UnknownPassageId_IsRemoved(string badId)
    {
        var result = CitationValidator.Validate(
            Answer(Para("Tuition counts.", Cite(badId, "anything"), Cite("P3", "Qualified education expenses include tuition and fees required for enrollment."))),
            Passages);

        var c = Assert.Single(Assert.Single(result.Paragraphs).Citations);
        Assert.Equal(3, c.PassageNumber);
        Assert.Equal(1, result.Stats.CitationsRemoved);
    }

    [Fact]
    public void PassageIdBeyondWhatWasSent_IsRemoved()
    {
        // Only two passages sent: P3 is in the schema's enum but wasn't actually sent.
        var result = CitationValidator.Validate(Answer(Para("Text.", Cite("P3", "x"))), Passages[..2]);

        Assert.True(result.IsRefusal);
        Assert.Equal(ValidationStats.AllParagraphsDropped, result.Stats.Outcome);
    }

    [Fact]
    public void QuoteNotInPassage_KeepsCitationWithNullHighlight()
    {
        var result = CitationValidator.Validate(
            Answer(Para("Scholarships can be taxable.", Cite("P1", "Scholarships are always taxable."))), Passages);

        var c = Assert.Single(Assert.Single(result.Paragraphs).Citations);
        Assert.Null(c.Highlight);
        Assert.Equal(1, result.Stats.HighlightsMissing);
    }

    [Fact]
    public void ParagraphWithNoValidCitations_IsDropped()
    {
        var result = CitationValidator.Validate(Answer(
            Para("Supported.", Cite("P2", "You must include in gross income amounts used for incidental expenses.")),
            Para("Unsupported claim.", Cite("P9", "x")),
            Para("No citations at all.")), Passages);

        Assert.Equal("Supported.", Assert.Single(result.Paragraphs).Text);
        Assert.Equal(2, result.Stats.ParagraphsDropped);
    }

    [Fact]
    public void AllParagraphsDropped_IsRefusal()
    {
        var result = CitationValidator.Validate(Answer(Para("A.", Cite("P7", "x")), Para("B.")), Passages);

        Assert.True(result.IsRefusal);
        Assert.Empty(result.Paragraphs);
        Assert.Equal(ValidationStats.AllParagraphsDropped, result.Stats.Outcome);
    }

    [Fact]
    public void InsufficientSources_IsRefusal()
    {
        var draft = new DraftAnswer(DraftAnswer.InsufficientSources,
            [Para("Ignored.", Cite("P1", "Amounts used for room and board are taxable."))]);

        var result = CitationValidator.Validate(draft, Passages);

        Assert.True(result.IsRefusal);
        Assert.Equal(ValidationStats.InsufficientSources, result.Stats.Outcome);
    }

    [Fact]
    public void DuplicatePassageCitations_CollapseToOneAndPreferAMatchedQuote()
    {
        var result = CitationValidator.Validate(Answer(Para("Room and board.",
            Cite("P1", "not in the passage"),
            Cite("P1", "Amounts used for room and board are taxable."))), Passages);

        var c = Assert.Single(Assert.Single(result.Paragraphs).Citations);
        Assert.Equal("Amounts used for room and board are taxable.", c.Highlight);
    }

    [Fact]
    public void FollowUps_AreCappedAtTwo()
    {
        var draft = Answer(Para("A.", Cite("P1", "x"))) with { SuggestedFollowUps = ["One?", " ", "Two?", "Three?"] };

        Assert.Equal(["One?", "Two?"], CitationValidator.Validate(draft, Passages).SuggestedFollowUps);
    }

    [Theory]
    // Curly quotes / dashes / extra whitespace / ligatures in either side still match, and the highlight is the original text.
    [InlineData("You can’t deduct it — see  chapter\n1.", "You can't deduct it - see chapter 1.", "You can’t deduct it — see  chapter\n1.")]
    [InlineData("The ﬁrst rule applies.", "The first rule applies.", "The ﬁrst rule applies.")]
    [InlineData("Intro. Tuition is “qualified” here. Outro.", "\"Tuition is \"qualified\" here.\"", "Tuition is “qualified” here.")]
    [InlineData("Intro. Tuition is qualified here. Outro.", "tuition is qualified here", "Tuition is qualified here")]
    public void FindQuote_NormalizesAndReturnsOriginalSpan(string passage, string quote, string expected)
    {
        Assert.Equal(expected, CitationValidator.FindQuote(passage, quote));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void FindQuote_EmptyQuote_IsNull(string? quote)
    {
        Assert.Null(CitationValidator.FindQuote("Some passage.", quote));
    }
}
