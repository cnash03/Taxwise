using System.Text.Json.Serialization;

namespace TaxWise.Shared;

[JsonConverter(typeof(JsonStringEnumConverter<FilingStatus>))]
public enum FilingStatus
{
    Single,
    MarriedFilingJointly,
    MarriedFilingSeparately,
    HeadOfHousehold,
}

/// <summary>Body of <c>POST /api/calculator</c> and <c>POST /api/calculator/compare</c> (BUILD_PLAN §7.1).</summary>
/// <param name="Wages">Form W-2 box 1. Also used as earned income for the dependent standard deduction.</param>
/// <param name="FederalWithholding">Form W-2 box 2.</param>
public sealed record CalcRequest(
    int TaxYear,
    decimal Wages,
    decimal FederalWithholding,
    FilingStatus FilingStatus,
    bool CanBeClaimedAsDependent,
    bool PlainLanguage = false);

/// <summary>One row of the step table. <see cref="SourceRef"/> names the IRS text the amount comes from.</summary>
public sealed record CalcStep(string Label, decimal Amount, string SourceRef);

/// <summary>"10% × $11,925 = $1,192.50": the part of <see cref="TaxBreakdown.TaxedAmount"/> taxed at one rate.</summary>
public sealed record BracketPortion(decimal Rate, decimal From, decimal To, decimal Amount, decimal Tax);

/// <summary>
/// How the tax was figured. With the Tax Table method the brackets are applied to the midpoint of the $50 (or $25)
/// row that holds the taxable income, and the result is rounded to the dollar, the way the IRS builds the table.
/// </summary>
/// <param name="Method"><see cref="TaxTableMethod"/> or <see cref="WorksheetMethod"/>.</param>
/// <param name="TaxedAmount">The row midpoint (Tax Table) or the taxable income itself (worksheet).</param>
public sealed record TaxBreakdown(
    string Method,
    decimal? RowAtLeast,
    decimal? RowLessThan,
    decimal TaxedAmount,
    IReadOnlyList<BracketPortion> Portions)
{
    public const string TaxTableMethod = "taxTable";
    public const string WorksheetMethod = "taxComputationWorksheet";
}

/// <summary>
/// The calculator result plus a cited explanation. Every number comes from code; the explanation only describes them.
/// <see cref="RefundOrOwed"/> is never negative: <see cref="IsRefund"/> says which one it is.
/// </summary>
public sealed record CalcResponse(
    int TaxYear,
    FilingStatus FilingStatus,
    bool CanBeClaimedAsDependent,
    IReadOnlyList<CalcStep> Steps,
    TaxBreakdown Breakdown,
    decimal Tax,
    decimal RefundOrOwed,
    bool IsRefund,
    bool IsEstimate,
    string? EstimateNote,
    string ExplanationKind,                 // "answer" (cited, validated) | "fallback" (canned text from the numbers)
    string Explanation,
    IReadOnlyList<CitationRef> Citations,
    IReadOnlyList<SourcePassage> Sources,
    string NotIncluded,
    string Disclaimer)
{
    public const string AnswerKind = "answer";
    public const string FallbackKind = "fallback";
}

/// <summary>Body of <c>POST /api/calculator/compare</c>: the same inputs for every supported year. Numbers only.</summary>
public sealed record CompareYearsResponse(IReadOnlyList<YearResult> Years, string NotIncluded, string Disclaimer);

public sealed record YearResult(
    int TaxYear,
    decimal StandardDeduction,
    decimal TaxableIncome,
    decimal Tax,
    decimal RefundOrOwed,
    bool IsRefund,
    bool IsEstimate);
