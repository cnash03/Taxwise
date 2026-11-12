using TaxWise.Core.Guardrails;

namespace TaxWise.Core.Tests.Guardrails;

public sealed class QuizItemValidatorTests
{
    private const string Passage =
        "A scholarship or fellowship grant is tax free only if you are a candidate for a degree at an eligible educational institution. " +
        "Amounts used for room and board aren’t qualified education expenses.";

    private static DraftQuizItem Item(string[]? options = null, int correctIndex = 1,
        string quote = "Amounts used for room and board aren't qualified education expenses.",
        string question = "Is the part of a scholarship used for room and board tax free?",
        string explanation = "Room and board isn't a qualified education expense.") =>
        new(question, options ?? ["Yes, always", "No, it's taxable", "Only for graduate students", "Only if paid by check"],
            correctIndex, explanation, quote);

    [Fact]
    public void ValidItem_IsAccepted_AndQuoteIsTheOriginalPassageText()
    {
        var check = QuizItemValidator.Validate(Item(), Passage);

        Assert.True(check.IsValid);
        Assert.Equal(QuizCheck.Ok, check.Outcome);
        // Straight apostrophe in the quote, curly one in the passage: the highlight is the passage's own text.
        Assert.Equal("Amounts used for room and board aren’t qualified education expenses.", check.Quote);
    }

    [Theory]
    [InlineData("A|B|C")]
    [InlineData("A|B|C|D|E")]
    [InlineData("A|B| |D")]
    public void WrongNumberOfOptions_IsRejected(string options) =>
        Assert.Equal(QuizCheck.WrongOptionCount, QuizItemValidator.Validate(Item(options.Split('|')), Passage).Outcome);

    [Fact]
    public void MissingOptions_AreRejected() =>
        Assert.Equal(QuizCheck.WrongOptionCount, QuizItemValidator.Validate(Item() with { Options = null }, Passage).Outcome);

    [Fact]
    public void DuplicateOptions_IgnoringCaseAndSpaces_AreRejected() =>
        Assert.Equal(QuizCheck.DuplicateOptions,
            QuizItemValidator.Validate(Item(["Yes", "No", " yes ", "Maybe"]), Passage).Outcome);

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void CorrectIndexOutOfRange_IsRejected(int index) =>
        Assert.Equal(QuizCheck.IndexOutOfRange, QuizItemValidator.Validate(Item(correctIndex: index), Passage).Outcome);

    [Fact]
    public void QuoteNotInPassage_IsRejected()
    {
        var check = QuizItemValidator.Validate(Item(quote: "Room and board is always tax free."), Passage);

        Assert.False(check.IsValid);
        Assert.Equal(QuizCheck.QuoteNotFound, check.Outcome);
        Assert.Null(check.Quote);
    }

    [Theory]
    [InlineData("", "Because.")]
    [InlineData("Why?", " ")]
    public void EmptyQuestionOrExplanation_IsRejected(string question, string explanation) =>
        Assert.Equal(QuizCheck.MissingText,
            QuizItemValidator.Validate(Item(question: question, explanation: explanation), Passage).Outcome);
}
