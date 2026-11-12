using System.Globalization;
using TaxWise.Core.Calculator;
using TaxWise.Shared;

namespace TaxWise.Core.Tests.Calculator;

/// <summary>
/// TY2025 against the official IRS numbers. Data/tax-table-2025.csv is every row of the 2025 Tax Table (2025 Form 1040
/// instructions, i1040gi--2025.pdf, PDF pages 68–79), extracted with PdfPig. Its last column is the PDF page so a
/// person can check any row against the PDF.
/// </summary>
public sealed class TaxTable2025Tests
{
    private static readonly ITaxYearRules Rules = TaxYearRegistry.Get(2025)!;

    private static readonly FilingStatus[] CsvColumns =
        [FilingStatus.Single, FilingStatus.MarriedFilingJointly, FilingStatus.MarriedFilingSeparately, FilingStatus.HeadOfHousehold];

    private static IEnumerable<(decimal AtLeast, decimal LessThan, decimal[] Tax, int Page)> Rows()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Calculator", "Data", "tax-table-2025.csv");
        foreach (var line in File.ReadLines(path).Skip(1))
        {
            var f = line.Split(',').Select(s => decimal.Parse(s, CultureInfo.InvariantCulture)).ToArray();
            yield return (f[0], f[1], f[2..6], (int)f[6]);
        }
    }

    [Fact]
    public void TableData_CoversZeroTo100k_WithNoGaps()
    {
        var rows = Rows().ToList();
        Assert.Equal(2062, rows.Count);
        Assert.Equal(0m, rows[0].AtLeast);
        Assert.Equal(100_000m, rows[^1].LessThan);
        for (var i = 1; i < rows.Count; i++)
            Assert.Equal(rows[i - 1].LessThan, rows[i].AtLeast);
    }

    // Every row × every filing status, at the bottom of the row and one cent below the top.
    [Fact]
    public void EveryTaxTableRow_MatchesTheOfficialTable()
    {
        var mismatches = new List<string>();
        foreach (var (atLeast, lessThan, tax, page) in Rows())
        {
            for (var s = 0; s < CsvColumns.Length; s++)
            {
                foreach (var income in new[] { atLeast, lessThan - 0.01m })
                {
                    var (actual, breakdown) = TaxCalculator.TaxOn(Rules, CsvColumns[s], income);
                    if (actual != tax[s] || breakdown.RowAtLeast != atLeast || breakdown.RowLessThan != lessThan)
                        mismatches.Add($"{CsvColumns[s]} {income}: expected {tax[s]} (p. {page}), got {actual}");
                }
            }
        }
        Assert.Empty(mismatches);
    }

    // 2025 Tax Computation Worksheet (i1040gi--2025.pdf PDF page 80): tax = income × rate − subtraction amount.
    [Theory]
    [InlineData(FilingStatus.Single, 100_000, 103_350, 0.22, 5_086.00)]
    [InlineData(FilingStatus.Single, 103_350, 197_300, 0.24, 7_153.00)]
    [InlineData(FilingStatus.Single, 197_300, 250_525, 0.32, 22_937.00)]
    [InlineData(FilingStatus.Single, 250_525, 626_350, 0.35, 30_452.75)]
    [InlineData(FilingStatus.Single, 626_350, 2_000_000, 0.37, 42_979.75)]
    [InlineData(FilingStatus.MarriedFilingJointly, 100_000, 206_700, 0.22, 10_172.00)]
    [InlineData(FilingStatus.MarriedFilingJointly, 206_700, 394_600, 0.24, 14_306.00)]
    [InlineData(FilingStatus.MarriedFilingJointly, 394_600, 501_050, 0.32, 45_874.00)]
    [InlineData(FilingStatus.MarriedFilingJointly, 501_050, 751_600, 0.35, 60_905.50)]
    [InlineData(FilingStatus.MarriedFilingJointly, 751_600, 2_000_000, 0.37, 75_937.50)]
    [InlineData(FilingStatus.MarriedFilingSeparately, 100_000, 103_350, 0.22, 5_086.00)]
    [InlineData(FilingStatus.MarriedFilingSeparately, 103_350, 197_300, 0.24, 7_153.00)]
    [InlineData(FilingStatus.MarriedFilingSeparately, 197_300, 250_525, 0.32, 22_937.00)]
    [InlineData(FilingStatus.MarriedFilingSeparately, 250_525, 375_800, 0.35, 30_452.75)]
    [InlineData(FilingStatus.MarriedFilingSeparately, 375_800, 2_000_000, 0.37, 37_968.75)]
    [InlineData(FilingStatus.HeadOfHousehold, 100_000, 103_350, 0.22, 6_825.00)]
    [InlineData(FilingStatus.HeadOfHousehold, 103_350, 197_300, 0.24, 8_892.00)]
    [InlineData(FilingStatus.HeadOfHousehold, 197_300, 250_500, 0.32, 24_676.00)]
    [InlineData(FilingStatus.HeadOfHousehold, 250_500, 626_350, 0.35, 32_191.00)]
    [InlineData(FilingStatus.HeadOfHousehold, 626_350, 2_000_000, 0.37, 44_718.00)]
    public void TaxComputationWorksheet_Matches(FilingStatus status, int from, int to, double rate, double subtraction)
    {
        foreach (var income in new[] { from, (from + to) / 2m + 0.37m, to })
        {
            var expected = Math.Round(income * (decimal)rate - (decimal)subtraction, 2, MidpointRounding.AwayFromZero);
            var (tax, breakdown) = TaxCalculator.TaxOn(Rules, status, income);
            Assert.Equal(expected, tax);
            Assert.Equal(TaxBreakdown.WorksheetMethod, breakdown.Method);
        }
    }

    /// <summary>
    /// Whole-scenario golden cases: wages → standard deduction → taxable income → tax. Expected tax comes from the 2025
    /// Tax Table row (PDF page in the comment) or, at $100,000 and up, the Tax Computation Worksheet (PDF page 80).
    /// </summary>
    [Theory]
    [InlineData(FilingStatus.Single, false, 28_000, 12_250, 1_235)]                  // p. 70; slide 11 demo scenario
    [InlineData(FilingStatus.MarriedFilingJointly, false, 56_800, 25_300, 2_562)]    // p. 71 (also the IRS "Sample Table" example)
    [InlineData(FilingStatus.HeadOfHousehold, false, 48_925, 25_300, 2_699)]         // p. 71
    [InlineData(FilingStatus.MarriedFilingSeparately, false, 41_050, 25_300, 2_801)] // p. 71
    [InlineData(FilingStatus.Single, false, 60_750, 45_000, 5_165)]                  // p. 73
    [InlineData(FilingStatus.HeadOfHousehold, false, 83_625, 60_000, 6_863)]         // p. 75
    [InlineData(FilingStatus.MarriedFilingJointly, false, 131_450, 99_950, 11_823)]  // p. 79, last Tax Table row
    [InlineData(FilingStatus.Single, true, 20_000, 4_250, 428)]                      // p. 69; dependent, capped at the regular $15,750
    [InlineData(FilingStatus.MarriedFilingSeparately, true, 25_250, 9_500, 953)]     // p. 69; dependent
    [InlineData(FilingStatus.Single, true, 4_000, 0, 0)]                             // dependent: deduction $4,450 > wages
    [InlineData(FilingStatus.Single, false, 0, 0, 0)]                                // zero wages
    [InlineData(FilingStatus.Single, false, 125_750, 110_000, 19_247)]               // p. 80: 110,000 × 24% − 7,153
    [InlineData(FilingStatus.HeadOfHousehold, false, 123_625, 100_000, 15_175)]      // p. 80: 100,000 × 22% − 6,825
    [InlineData(FilingStatus.MarriedFilingJointly, false, 331_500, 300_000, 57_694)] // p. 80: 300,000 × 24% − 14,306
    public void GoldenScenarios(FilingStatus status, bool dependent, int wages, int taxableIncome, int tax)
    {
        var result = TaxCalculator.Calculate(Rules, wages, 1_500m, status, dependent);

        Assert.Equal(taxableIncome, result.TaxableIncome);
        Assert.Equal(tax, result.Tax);
        Assert.Equal(1_500m - tax, result.RefundOrOwed);
        Assert.False(result.IsEstimate);
    }
}
