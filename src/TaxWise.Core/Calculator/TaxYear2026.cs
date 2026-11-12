using TaxWise.Shared;

namespace TaxWise.Core.Calculator;

/// <summary>
/// TY2026: returns students file in spring 2027. From Rev. Proc. 2025-32 §4 (2026 adjusted items). The 2026 Tax Table
/// isn't published yet, so results below $100,000 are estimates. When the 2026 Form 1040 instructions come out, add
/// golden cases from the official table and set <see cref="HasOfficialTaxTable"/> to true.
/// </summary>
public sealed class TaxYear2026 : ITaxYearRules
{
    public int TaxYear => 2026;
    public bool HasOfficialTaxTable => false;

    // Rev. Proc. 2025-32 §4.14(1) (p. 18).
    public decimal StandardDeduction(FilingStatus status) => status switch
    {
        FilingStatus.Single => 16_100m,
        FilingStatus.MarriedFilingJointly => 32_200m,
        FilingStatus.MarriedFilingSeparately => 16_100m,
        FilingStatus.HeadOfHousehold => 24_150m,
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    // Rev. Proc. 2025-32 §4.14(2) (p. 18).
    public decimal DependentMinimum => 1_350m;
    public decimal DependentEarnedIncomeAddOn => 450m;

    public IReadOnlyList<TaxBracket> Brackets(FilingStatus status) => status switch
    {
        FilingStatus.Single => Single,
        FilingStatus.MarriedFilingJointly => Joint,
        FilingStatus.MarriedFilingSeparately => Separate,
        FilingStatus.HeadOfHousehold => HeadOfHousehold,
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    // Rev. Proc. 2025-32 §4.01 Table 3, unmarried individuals (p. 11).
    private static readonly TaxBracket[] Single =
        [new(0.10m, 12_400m), new(0.12m, 50_400m), new(0.22m, 105_700m), new(0.24m, 201_775m), new(0.32m, 256_225m), new(0.35m, 640_600m), new(0.37m, null)];

    // Rev. Proc. 2025-32 §4.01 Table 1, married filing jointly and surviving spouses (p. 10).
    private static readonly TaxBracket[] Joint =
        [new(0.10m, 24_800m), new(0.12m, 100_800m), new(0.22m, 211_400m), new(0.24m, 403_550m), new(0.32m, 512_450m), new(0.35m, 768_700m), new(0.37m, null)];

    // Rev. Proc. 2025-32 §4.01 Table 4, married filing separately (pp. 11–12).
    private static readonly TaxBracket[] Separate =
        [new(0.10m, 12_400m), new(0.12m, 50_400m), new(0.22m, 105_700m), new(0.24m, 201_775m), new(0.32m, 256_225m), new(0.35m, 384_350m), new(0.37m, null)];

    // Rev. Proc. 2025-32 §4.01 Table 2, heads of households (pp. 10–11).
    private static readonly TaxBracket[] HeadOfHousehold =
        [new(0.10m, 17_700m), new(0.12m, 67_450m), new(0.22m, 105_700m), new(0.24m, 201_750m), new(0.32m, 256_200m), new(0.35m, 640_600m), new(0.37m, null)];

    public string StandardDeductionSource => "Rev. Proc. 2025-32 §4.14(1)";
    public string DependentDeductionSource => "Rev. Proc. 2025-32 §4.14(2)";
    public string RateTableSource => "Rev. Proc. 2025-32 §4.01";
    public string TaxTableSource => "Tax Table method applied to Rev. Proc. 2025-32 §4.01 (official 2026 Tax Table not yet published)";
}
