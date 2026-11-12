using System.Text.Json.Serialization;

namespace TaxWise.Core.Guardrails;

/// <summary>Claude's structured quiz question for one passage, as the quiz JSON schema shapes it. Not yet trusted.</summary>
public sealed record DraftQuizItem(
    [property: JsonPropertyName("question")] string? Question,
    [property: JsonPropertyName("options")] IReadOnlyList<string>? Options,
    [property: JsonPropertyName("correctIndex")] int CorrectIndex,
    [property: JsonPropertyName("explanation")] string? Explanation,
    [property: JsonPropertyName("quote")] string? Quote);

/// <summary>
/// <see cref="Outcome"/> is <see cref="Ok"/> or the first check that failed, for the log line and the retry prompt.
/// <see cref="Quote"/> is the matching span of the original passage text when the item is valid.
/// </summary>
public sealed record QuizCheck(string Outcome, string? Quote)
{
    public const string Ok = "ok";
    public const string MissingText = "missing_text";
    public const string WrongOptionCount = "wrong_option_count";
    public const string DuplicateOptions = "duplicate_options";
    public const string IndexOutOfRange = "index_out_of_range";
    public const string QuoteNotFound = "quote_not_in_passage";

    public bool IsValid => Outcome == Ok;
}

/// <summary>
/// Checks a generated quiz question against the passage it was written from (BUILD_PLAN §7, Phase 6): a question and
/// an explanation, exactly 4 distinct options, a correct index in 0–3, and a quote that is in the passage. Quotes are
/// matched the same way citation highlights are (<see cref="CitationValidator.FindQuote"/>).
/// </summary>
public static class QuizItemValidator
{
    public const int OptionCount = 4;

    public static QuizCheck Validate(DraftQuizItem item, string passage)
    {
        if (string.IsNullOrWhiteSpace(item.Question) || string.IsNullOrWhiteSpace(item.Explanation))
            return Fail(QuizCheck.MissingText);
        if (item.Options is not { Count: OptionCount } options || options.Any(string.IsNullOrWhiteSpace))
            return Fail(QuizCheck.WrongOptionCount);
        if (options.Select(o => o.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != OptionCount)
            return Fail(QuizCheck.DuplicateOptions);
        if (item.CorrectIndex is < 0 or >= OptionCount)
            return Fail(QuizCheck.IndexOutOfRange);

        var quote = CitationValidator.FindQuote(passage, item.Quote);
        return quote is null ? Fail(QuizCheck.QuoteNotFound) : new QuizCheck(QuizCheck.Ok, quote);
    }

    private static QuizCheck Fail(string outcome) => new(outcome, null);
}
