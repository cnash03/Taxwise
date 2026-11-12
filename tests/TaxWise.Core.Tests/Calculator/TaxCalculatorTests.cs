using TaxWise.Core.Calculator;
using TaxWise.Shared;

namespace TaxWise.Core.Tests.Calculator;

public sealed class TaxCalculatorTests
{
    private static readonly FilingStatus[] Statuses = Enum.GetValues<FilingStatus>();

    private static CalcResult Calc(int year, decimal wages, FilingStatus status = FilingStatus.Single, bool dependent = false,
        decimal withholding = 0) =>
        TaxCalculator.Calculate(TaxYearRegistry.Get(year)!, wages, withholding, status, dependent);

    [Fact]
    public void Registry_HasExactlyTheThreeSupportedYears()
    {
        Assert.Equal([2025, 2026, 2027], TaxYearRegistry.Supported);
        Assert.Null(TaxYearRegistry.Get(2024));
        Assert.Null(TaxYearRegistry.Get(2028));
    }

    [Fact]
    public void Slide11Scenario_UsesTheTaxTable_NotTheExactFormula()
    {
        // The mockup showed $1,231.50 (exact formula on $12,250). A 1040 filer uses the $12,250–$12,300 row: $1,235.
        var r = Calc(2025, 28_000m, withholding: 1_500m);

        Assert.Equal(12_250m, r.TaxableIncome);
        Assert.Equal(1_235m, r.Tax);
        Assert.Equal(265m, r.RefundOrOwed);
        Assert.Equal(TaxBreakdown.TaxTableMethod, r.Breakdown.Method);
        Assert.Equal(12_275m, r.Breakdown.TaxedAmount);
        Assert.Equal([1_192.50m, 42.00m], r.Breakdown.Portions.Select(p => p.Tax));
        Assert.Equal([11_925m, 350m], r.Breakdown.Portions.Select(p => p.Amount));
    }

    [Fact]
    public void Steps_ListTheInputsAndEachComputedAmount_WithSources()
    {
        var r = Calc(2026, 28_000m, withholding: 3_000m);

        Assert.Equal([28_000m, 16_100m, 11_900m, r.Tax, 3_000m, 3_000m - r.Tax], r.Steps.Select(s => s.Amount));
        Assert.All(r.Steps, s => Assert.False(string.IsNullOrWhiteSpace(s.SourceRef)));
        Assert.Contains("Rev. Proc. 2025-32", r.Steps[1].SourceRef);
        Assert.Equal("Estimated refund", r.Steps[^1].Label);
    }

    [Fact]
    public void AmountOwed_IsNegative_AndLabelledOwed()
    {
        var r = Calc(2025, 28_000m, withholding: 200m);
        Assert.Equal(200m - 1_235m, r.RefundOrOwed);
        Assert.Equal("Estimated amount owed", r.Steps[^1].Label);
        Assert.Equal(1_035m, r.Steps[^1].Amount);
    }

    [Theory]
    [InlineData(2025, false)]
    [InlineData(2026, true)]
    [InlineData(2027, true)]
    public void YearsWithoutAnOfficialTaxTable_AreEstimates(int year, bool estimate)
    {
        Assert.Equal(estimate, Calc(year, 40_000m).IsEstimate);
        // The bracket formula (taxable income ≥ $100,000) is exact Rev. Proc. text, so it isn't an estimate.
        Assert.False(Calc(year, 200_000m).IsEstimate);
    }

    [Theory]
    [InlineData(2025, 0, 1_350)]
    [InlineData(2026, 0, 1_350)]
    [InlineData(2027, 0, 1_400)]
    [InlineData(2025, 500, 1_350)]   // 500 + 450 < 1,350
    [InlineData(2027, 1_000, 1_500)] // 1,000 + 500
    [InlineData(2026, 5_000, 5_450)]
    [InlineData(2026, 40_000, 16_100)] // capped at the regular amount
    public void DependentStandardDeduction_UsesThatYearsFloorAndAddOn(int year, int wages, int expected)
    {
        Assert.Equal(expected, Calc(year, wages, dependent: true).StandardDeduction);
    }

    [Fact]
    public void DependentCap_IsTheRegularAmountForTheFilingStatus()
    {
        Assert.Equal(31_500m, Calc(2025, 90_000m, FilingStatus.MarriedFilingJointly, dependent: true).StandardDeduction);
        Assert.Equal(23_625m, Calc(2025, 90_000m, FilingStatus.HeadOfHousehold, dependent: true).StandardDeduction);
    }

    [Fact]
    public void SameWages_LaterYear_NeverMoreTax()
    {
        foreach (var status in Statuses)
        foreach (var dependent in new[] { false, true })
        for (var wages = 0m; wages <= 400_000m; wages += 1_237.13m)
        {
            var t25 = Calc(2025, wages, status, dependent).Tax;
            var t26 = Calc(2026, wages, status, dependent).Tax;
            var t27 = Calc(2027, wages, status, dependent).Tax;
            Assert.True(t26 <= t25, $"{status} dep={dependent} {wages}: 2026 {t26} > 2025 {t25}");
            Assert.True(t27 <= t26, $"{status} dep={dependent} {wages}: 2027 {t27} > 2026 {t26}");
        }
    }

    [Fact]
    public void Tax_IsMonotonicInWages_AndTaxableIncomeNeverNegative()
    {
        foreach (var year in TaxYearRegistry.Supported)
        foreach (var status in Statuses)
        foreach (var dependent in new[] { false, true })
        {
            var previous = -1m;
            for (var wages = 0m; wages <= 300_000m; wages += 97.31m)
            {
                var r = Calc(year, wages, status, dependent);
                Assert.True(r.TaxableIncome >= 0);
                Assert.True(r.Tax >= previous, $"{year} {status} dep={dependent}: tax fell at wages {wages}");
                previous = r.Tax;
            }
        }
    }

    [Theory]
    [InlineData(0, 0, 5)]
    [InlineData(4.99, 0, 5)]
    [InlineData(5, 5, 15)]
    [InlineData(24.99, 15, 25)]
    [InlineData(25, 25, 50)]
    [InlineData(2_999.99, 2_975, 3_000)]
    [InlineData(3_000, 3_000, 3_050)]
    [InlineData(12_250, 12_250, 12_300)]
    [InlineData(99_999.99, 99_950, 100_000)]
    public void TaxTableRow_Boundaries(double income, int atLeast, int lessThan)
    {
        Assert.Equal(((decimal)atLeast, (decimal)lessThan), TaxCalculator.TaxTableRow((decimal)income));
    }

    [Fact]
    public void KnownAmounts_IncludeStepsBreakdownAndThresholds()
    {
        var r = Calc(2025, 28_000m, withholding: 1_500m);
        foreach (var amount in new[] { 28_000m, 15_750m, 12_250m, 1_235m, 1_500m, 265m, 12_275m, 12_300m, 11_925m, 1_192.50m, 350m, 42m, 48_475m, 100_000m })
            Assert.Contains(amount, r.KnownAmounts);
        Assert.DoesNotContain(1_231.50m, r.KnownAmounts);
    }
}
