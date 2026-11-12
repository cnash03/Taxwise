using TaxWise.Shared;

namespace TaxWise.Core.Calculator;

/// <summary>One rate bracket: income above the previous bracket's <see cref="UpTo"/> and up to this one is taxed at <see cref="Rate"/>.</summary>
/// <param name="UpTo">Null for the top bracket.</param>
public sealed record TaxBracket(decimal Rate, decimal? UpTo);

/// <summary>
/// The constants for one tax year (BUILD_PLAN §7.1). Each year is one file, <c>TaxYear{yyyy}.cs</c>, found by
/// <see cref="TaxYearRegistry"/>: adding a year means adding a file. Every constant cites its IRS source in a comment,
/// and a person checks each one against the PDF.
/// </summary>
public interface ITaxYearRules
{
    int TaxYear { get; }

    /// <summary>False until the IRS publishes that year's Tax Table; results from the Tax Table method are then estimates.</summary>
    bool HasOfficialTaxTable { get; }

    decimal StandardDeduction(FilingStatus status);

    /// <summary>A dependent's standard deduction is the greater of this or earned income + <see cref="DependentEarnedIncomeAddOn"/>, capped at the regular amount.</summary>
    decimal DependentMinimum { get; }
    decimal DependentEarnedIncomeAddOn { get; }

    /// <summary>Lowest bracket first; the last one has no <see cref="TaxBracket.UpTo"/>.</summary>
    IReadOnlyList<TaxBracket> Brackets(FilingStatus status);

    /// <summary>Short citations shown in the step table, e.g. "Rev. Proc. 2025-32 §4.14(1)".</summary>
    string StandardDeductionSource { get; }
    string DependentDeductionSource { get; }
    string RateTableSource { get; }
    string TaxTableSource { get; }
}
