using TaxWise.Core.Calculator;
using TaxWise.Shared;

namespace TaxWise.Core.Tests.Calculator;

/// <summary>
/// Each Rev. Proc. rate table states the tax at every breakpoint ("$5,987.00 plus 22% of the excess over $52,025").
/// The bracket formula must reproduce every one of those base amounts, copied from the IRS text.
/// TY2026: Rev. Proc. 2025-32 §4.01 Tables 1–4 (pp. 10–12). TY2027: Rev. Proc. 2026-35 §3.01 Tables 1–4 (pp. 4–6).
/// </summary>
public sealed class RevProcBreakpointTests
{
    public static TheoryData<int, FilingStatus, decimal, decimal> Breakpoints() => new()
    {
        { 2026, FilingStatus.MarriedFilingJointly, 24_800m, 2_480m },
        { 2026, FilingStatus.MarriedFilingJointly, 100_800m, 11_600m },
        { 2026, FilingStatus.MarriedFilingJointly, 211_400m, 35_932m },
        { 2026, FilingStatus.MarriedFilingJointly, 403_550m, 82_048m },
        { 2026, FilingStatus.MarriedFilingJointly, 512_450m, 116_896m },
        { 2026, FilingStatus.MarriedFilingJointly, 768_700m, 206_583.50m },
        { 2026, FilingStatus.HeadOfHousehold, 17_700m, 1_770m },
        { 2026, FilingStatus.HeadOfHousehold, 67_450m, 7_740m },
        { 2026, FilingStatus.HeadOfHousehold, 105_700m, 16_155m },
        { 2026, FilingStatus.HeadOfHousehold, 201_750m, 39_207m },
        { 2026, FilingStatus.HeadOfHousehold, 256_200m, 56_631m },
        { 2026, FilingStatus.HeadOfHousehold, 640_600m, 191_171m },
        { 2026, FilingStatus.Single, 12_400m, 1_240m },
        { 2026, FilingStatus.Single, 50_400m, 5_800m },
        { 2026, FilingStatus.Single, 105_700m, 17_966m },
        { 2026, FilingStatus.Single, 201_775m, 41_024m },
        { 2026, FilingStatus.Single, 256_225m, 58_448m },
        { 2026, FilingStatus.Single, 640_600m, 192_979.25m },
        { 2026, FilingStatus.MarriedFilingSeparately, 12_400m, 1_240m },
        { 2026, FilingStatus.MarriedFilingSeparately, 50_400m, 5_800m },
        { 2026, FilingStatus.MarriedFilingSeparately, 105_700m, 17_966m },
        { 2026, FilingStatus.MarriedFilingSeparately, 201_775m, 41_024m },
        { 2026, FilingStatus.MarriedFilingSeparately, 256_225m, 58_448m },
        { 2026, FilingStatus.MarriedFilingSeparately, 384_350m, 103_291.75m },

        { 2027, FilingStatus.MarriedFilingJointly, 25_600m, 2_560m },
        { 2027, FilingStatus.MarriedFilingJointly, 104_050m, 11_974m },
        { 2027, FilingStatus.MarriedFilingJointly, 218_250m, 37_098m },
        { 2027, FilingStatus.MarriedFilingJointly, 416_650m, 84_714m },
        { 2027, FilingStatus.MarriedFilingJointly, 529_100m, 120_698m },
        { 2027, FilingStatus.MarriedFilingJointly, 793_650m, 213_290.50m },
        { 2027, FilingStatus.HeadOfHousehold, 18_250m, 1_825m },
        { 2027, FilingStatus.HeadOfHousehold, 69_650m, 7_993m },
        { 2027, FilingStatus.HeadOfHousehold, 109_100m, 16_672m },
        { 2027, FilingStatus.HeadOfHousehold, 208_300m, 40_480m },
        { 2027, FilingStatus.HeadOfHousehold, 264_550m, 58_480m },
        { 2027, FilingStatus.HeadOfHousehold, 661_350m, 197_360m },
        { 2027, FilingStatus.Single, 12_800m, 1_280m },
        { 2027, FilingStatus.Single, 52_025m, 5_987m },
        { 2027, FilingStatus.Single, 109_125m, 18_549m },
        { 2027, FilingStatus.Single, 208_325m, 42_357m },
        { 2027, FilingStatus.Single, 264_550m, 60_349m },
        { 2027, FilingStatus.Single, 661_375m, 199_237.75m },
        { 2027, FilingStatus.MarriedFilingSeparately, 12_800m, 1_280m },
        { 2027, FilingStatus.MarriedFilingSeparately, 52_025m, 5_987m },
        { 2027, FilingStatus.MarriedFilingSeparately, 109_125m, 18_549m },
        { 2027, FilingStatus.MarriedFilingSeparately, 208_325m, 42_357m },
        { 2027, FilingStatus.MarriedFilingSeparately, 264_550m, 60_349m },
        { 2027, FilingStatus.MarriedFilingSeparately, 396_825m, 106_645.25m },
    };

    [Theory]
    [MemberData(nameof(Breakpoints))]
    public void BracketFormula_ReproducesRevProcBaseTax(int year, FilingStatus status, decimal breakpoint, decimal baseTax)
    {
        var rules = TaxYearRegistry.Get(year)!;
        Assert.Equal(baseTax, TaxCalculator.BracketTax(rules.Brackets(status), breakpoint).Tax);
    }

    // §4.14(1) / §3.15(1) standard deductions and the §4.14(2) / §3.15(2) dependent amounts.
    [Theory]
    [InlineData(2026, 16_100, 32_200, 16_100, 24_150, 1_350, 450)]
    [InlineData(2027, 16_600, 33_200, 16_600, 24_925, 1_400, 500)]
    [InlineData(2025, 15_750, 31_500, 15_750, 23_625, 1_350, 450)] // Rev. Proc. 2025-32 §3.01; i1040 Standard Deduction Worksheet for Dependents
    public void StandardDeductions(int year, int single, int mfj, int mfs, int hoh, int dependentMinimum, int dependentAddOn)
    {
        var rules = TaxYearRegistry.Get(year)!;
        Assert.Equal(single, rules.StandardDeduction(FilingStatus.Single));
        Assert.Equal(mfj, rules.StandardDeduction(FilingStatus.MarriedFilingJointly));
        Assert.Equal(mfs, rules.StandardDeduction(FilingStatus.MarriedFilingSeparately));
        Assert.Equal(hoh, rules.StandardDeduction(FilingStatus.HeadOfHousehold));
        Assert.Equal(dependentMinimum, rules.DependentMinimum);
        Assert.Equal(dependentAddOn, rules.DependentEarnedIncomeAddOn);
    }
}
