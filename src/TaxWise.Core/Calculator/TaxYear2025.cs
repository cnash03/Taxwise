using TaxWise.Shared;

namespace TaxWise.Core.Calculator;

/// <summary>
/// TY2025: returns filed in spring 2026. Standard deductions as raised by the OBBBA (Rev. Proc. 2025-32 §3.01, which
/// replaces Rev. Proc. 2024-40 §2.15(1)). The official Tax Table is published, and TaxTable2025Tests checks every row.
/// </summary>
public sealed class TaxYear2025 : ITaxYearRules
{
    public int TaxYear => 2025;
    public bool HasOfficialTaxTable => true;

    // Rev. Proc. 2025-32 §3.01 (p. 9); same amounts in the 2025 Form 1040 instructions, Standard Deduction chart.
    public decimal StandardDeduction(FilingStatus status) => status switch
    {
        FilingStatus.Single => 15_750m,
        FilingStatus.MarriedFilingJointly => 31_500m,
        FilingStatus.MarriedFilingSeparately => 15_750m,
        FilingStatus.HeadOfHousehold => 23_625m,
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    // 2025 Form 1040 instructions, Standard Deduction Worksheet for Dependents—Line 12e, line 2 (i1040gi--2025.pdf p. 35).
    public decimal DependentMinimum => 1_350m;
    public decimal DependentEarnedIncomeAddOn => 450m;

    // 10% and 12% tops: Rev. Proc. 2024-40 §2.01 Tables 1–4 (not in the knowledge base). 22% and up: 2025 Form 1040
    // instructions, Tax Computation Worksheet—Line 16 (i1040gi--2025.pdf p. 80). The whole schedule below $100,000 is
    // checked against every row of the 2025 Tax Table (TaxTable2025Tests).
    public IReadOnlyList<TaxBracket> Brackets(FilingStatus status) => status switch
    {
        FilingStatus.Single => Single,
        FilingStatus.MarriedFilingJointly => Joint,
        FilingStatus.MarriedFilingSeparately => Separate,
        FilingStatus.HeadOfHousehold => HeadOfHousehold,
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    private static readonly TaxBracket[] Single =
        [new(0.10m, 11_925m), new(0.12m, 48_475m), new(0.22m, 103_350m), new(0.24m, 197_300m), new(0.32m, 250_525m), new(0.35m, 626_350m), new(0.37m, null)];

    private static readonly TaxBracket[] Joint =
        [new(0.10m, 23_850m), new(0.12m, 96_950m), new(0.22m, 206_700m), new(0.24m, 394_600m), new(0.32m, 501_050m), new(0.35m, 751_600m), new(0.37m, null)];

    private static readonly TaxBracket[] Separate =
        [new(0.10m, 11_925m), new(0.12m, 48_475m), new(0.22m, 103_350m), new(0.24m, 197_300m), new(0.32m, 250_525m), new(0.35m, 375_800m), new(0.37m, null)];

    private static readonly TaxBracket[] HeadOfHousehold =
        [new(0.10m, 17_000m), new(0.12m, 64_850m), new(0.22m, 103_350m), new(0.24m, 197_300m), new(0.32m, 250_500m), new(0.35m, 626_350m), new(0.37m, null)];

    public string StandardDeductionSource => "Rev. Proc. 2025-32 §3.01; 2025 Form 1040 instructions, line 12e";
    public string DependentDeductionSource => "2025 Form 1040 instructions, Standard Deduction Worksheet for Dependents";
    public string RateTableSource => "2025 Form 1040 instructions, Tax Computation Worksheet";
    public string TaxTableSource => "2025 Form 1040 instructions, 2025 Tax Table";
}
