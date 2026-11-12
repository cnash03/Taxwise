using TaxWise.Shared;

namespace TaxWise.Core.Calculator;

/// <summary>
/// TY2027: next year's planning. From Rev. Proc. 2026-35 §3 (2027 adjusted items; unlike Rev. Proc. 2025-32 the year's
/// items are in §3, see DECISIONS.md #3). No Tax Table exists yet, so results below $100,000 are estimates.
/// </summary>
public sealed class TaxYear2027 : ITaxYearRules
{
    public int TaxYear => 2027;
    public bool HasOfficialTaxTable => false;

    // Rev. Proc. 2026-35 §3.15(1) (p. 13).
    public decimal StandardDeduction(FilingStatus status) => status switch
    {
        FilingStatus.Single => 16_600m,
        FilingStatus.MarriedFilingJointly => 33_200m,
        FilingStatus.MarriedFilingSeparately => 16_600m,
        FilingStatus.HeadOfHousehold => 24_925m,
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    // Rev. Proc. 2026-35 §3.15(2) (p. 13).
    public decimal DependentMinimum => 1_400m;
    public decimal DependentEarnedIncomeAddOn => 500m;

    public IReadOnlyList<TaxBracket> Brackets(FilingStatus status) => status switch
    {
        FilingStatus.Single => Single,
        FilingStatus.MarriedFilingJointly => Joint,
        FilingStatus.MarriedFilingSeparately => Separate,
        FilingStatus.HeadOfHousehold => HeadOfHousehold,
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    // Rev. Proc. 2026-35 §3.01 Table 3, unmarried individuals (p. 5).
    private static readonly TaxBracket[] Single =
        [new(0.10m, 12_800m), new(0.12m, 52_025m), new(0.22m, 109_125m), new(0.24m, 208_325m), new(0.32m, 264_550m), new(0.35m, 661_375m), new(0.37m, null)];

    // Rev. Proc. 2026-35 §3.01 Table 1, married filing jointly and surviving spouses (p. 4).
    private static readonly TaxBracket[] Joint =
        [new(0.10m, 25_600m), new(0.12m, 104_050m), new(0.22m, 218_250m), new(0.24m, 416_650m), new(0.32m, 529_100m), new(0.35m, 793_650m), new(0.37m, null)];

    // Rev. Proc. 2026-35 §3.01 Table 4, married filing separately (p. 6).
    private static readonly TaxBracket[] Separate =
        [new(0.10m, 12_800m), new(0.12m, 52_025m), new(0.22m, 109_125m), new(0.24m, 208_325m), new(0.32m, 264_550m), new(0.35m, 396_825m), new(0.37m, null)];

    // Rev. Proc. 2026-35 §3.01 Table 2, heads of households (p. 5).
    private static readonly TaxBracket[] HeadOfHousehold =
        [new(0.10m, 18_250m), new(0.12m, 69_650m), new(0.22m, 109_100m), new(0.24m, 208_300m), new(0.32m, 264_550m), new(0.35m, 661_350m), new(0.37m, null)];

    public string StandardDeductionSource => "Rev. Proc. 2026-35 §3.15(1)";
    public string DependentDeductionSource => "Rev. Proc. 2026-35 §3.15(2)";
    public string RateTableSource => "Rev. Proc. 2026-35 §3.01";
    public string TaxTableSource => "Tax Table method applied to Rev. Proc. 2026-35 §3.01 (no 2027 Tax Table yet)";
}
