using TaxWise.Shared;

namespace TaxWise.Core.Calculator;

/// <summary>
/// A finished calculation. <see cref="RefundOrOwed"/> is withholding − tax: positive is a refund, negative is owed.
/// <see cref="KnownAmounts"/> is every dollar figure the explanation may mention (<c>DollarAmountValidator</c>).
/// </summary>
public sealed record CalcResult(
    int TaxYear,
    FilingStatus FilingStatus,
    bool CanBeClaimedAsDependent,
    decimal Wages,
    decimal FederalWithholding,
    decimal StandardDeduction,
    decimal TaxableIncome,
    decimal Tax,
    TaxBreakdown Breakdown,
    decimal RefundOrOwed,
    bool IsEstimate,
    IReadOnlyList<CalcStep> Steps,
    IReadOnlyList<decimal> KnownAmounts)
{
    public bool IsRefund => RefundOrOwed >= 0;
}

/// <summary>
/// The scenario calculator (BUILD_PLAN §7.1): standard deduction → taxable income → tax → refund or amount owed.
/// Pure and deterministic, <c>decimal</c> only. Credits, other income and itemized deductions are out of scope.
/// </summary>
public static class TaxCalculator
{
    /// <summary>Taxable income below this uses the Tax Table; at or above it, the Tax Computation Worksheet.</summary>
    public const decimal TaxTableLimit = 100_000m;

    public static CalcResult Calculate(ITaxYearRules rules, decimal wages, decimal federalWithholding, FilingStatus status,
        bool canBeClaimedAsDependent)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(wages);
        ArgumentOutOfRangeException.ThrowIfNegative(federalWithholding);

        var regular = rules.StandardDeduction(status);
        // Wages are the earned income here: the calculator has no other income (scholarships, tips) as input.
        var deduction = canBeClaimedAsDependent
            ? Math.Min(regular, Math.Max(rules.DependentMinimum, wages + rules.DependentEarnedIncomeAddOn))
            : regular;
        var taxable = Math.Max(0m, wages - deduction);
        var (tax, breakdown) = TaxOn(rules, status, taxable);
        var refundOrOwed = federalWithholding - tax;
        var isTable = breakdown.Method == TaxBreakdown.TaxTableMethod;
        var isEstimate = isTable && !rules.HasOfficialTaxTable;

        List<CalcStep> steps =
        [
            new("Wages (W-2 box 1)", wages, "Your input"),
            new(canBeClaimedAsDependent ? "Standard deduction (dependent)" : "Standard deduction", deduction,
                canBeClaimedAsDependent ? rules.DependentDeductionSource : rules.StandardDeductionSource),
            new("Taxable income", taxable, "Wages minus the standard deduction (Form 1040, line 15)"),
            new(TaxLabel(isTable, isEstimate), tax, isTable ? rules.TaxTableSource : rules.RateTableSource),
            new("Federal income tax withheld (W-2 box 2)", federalWithholding, "Your input"),
            new(refundOrOwed >= 0 ? "Estimated refund" : "Estimated amount owed", Math.Abs(refundOrOwed),
                "Withholding minus tax"),
        ];

        return new CalcResult(rules.TaxYear, status, canBeClaimedAsDependent, wages, federalWithholding, deduction, taxable,
            tax, breakdown, refundOrOwed, isEstimate, steps, KnownAmounts(rules, status, steps, breakdown, regular, wages));
    }

    /// <summary>
    /// Tax on <paramref name="taxableIncome"/>. Below $100,000 this is the Tax Table method: the brackets applied to the
    /// midpoint of the row, rounded to the nearest dollar, which is how the IRS builds the table. At $100,000 and up it
    /// is the exact bracket formula, to the cent.
    /// </summary>
    public static (decimal Tax, TaxBreakdown Breakdown) TaxOn(ITaxYearRules rules, FilingStatus status, decimal taxableIncome)
    {
        var brackets = rules.Brackets(status);
        if (taxableIncome >= TaxTableLimit)
        {
            var (tax, portions) = BracketTax(brackets, taxableIncome);
            return (tax, new TaxBreakdown(TaxBreakdown.WorksheetMethod, null, null, taxableIncome, portions));
        }

        var (atLeast, lessThan) = TaxTableRow(taxableIncome);
        var midpoint = (atLeast + lessThan) / 2;
        var (exact, rowPortions) = Exact(brackets, midpoint);
        var rounded = Math.Round(exact, 0, MidpointRounding.AwayFromZero);
        return (rounded, new TaxBreakdown(TaxBreakdown.TaxTableMethod, atLeast, lessThan, midpoint, rowPortions));
    }

    /// <summary>The Tax Table row holding <paramref name="taxableIncome"/>: $0–5, $5–15, $15–25, then $25 rows to $3,000 and $50 rows after.</summary>
    public static (decimal AtLeast, decimal LessThan) TaxTableRow(decimal taxableIncome)
    {
        if (taxableIncome < 5) return (0, 5);
        if (taxableIncome < 15) return (5, 15);
        if (taxableIncome < 25) return (15, 25);
        var width = taxableIncome < 3_000 ? 25m : 50m;
        var atLeast = Math.Floor(taxableIncome / width) * width;
        return (atLeast, atLeast + width);
    }

    /// <summary>The bracket formula: each bracket's rate on the part of <paramref name="amount"/> inside it, total rounded to the cent.</summary>
    public static (decimal Tax, IReadOnlyList<BracketPortion> Portions) BracketTax(IReadOnlyList<TaxBracket> brackets, decimal amount)
    {
        var (exact, portions) = Exact(brackets, amount);
        return (Math.Round(exact, 2, MidpointRounding.AwayFromZero), portions);
    }

    // Rounds only the total, so the Tax Table method rounds once (to the dollar), not to the cent first.
    private static (decimal Exact, IReadOnlyList<BracketPortion> Portions) Exact(IReadOnlyList<TaxBracket> brackets, decimal amount)
    {
        var portions = new List<BracketPortion>();
        var exact = 0m;
        var from = 0m;
        foreach (var b in brackets)
        {
            if (amount <= from) break;
            var to = b.UpTo is { } upTo ? Math.Min(amount, upTo) : amount;
            var part = to - from;
            var tax = part * b.Rate;
            exact += tax;
            portions.Add(new BracketPortion(b.Rate, from, to, part, Math.Round(tax, 2, MidpointRounding.AwayFromZero)));
            if (b.UpTo is null || amount <= b.UpTo) break;
            from = b.UpTo.Value;
        }
        return (exact, portions);
    }

    private static string TaxLabel(bool isTable, bool isEstimate) =>
        !isTable ? "Tax (Tax Computation Worksheet)"
        : isEstimate ? "Tax (Tax Table method, estimate)"
        : "Tax (from IRS Tax Table)";

    // The step amounts, the breakdown, the year's thresholds for this status, the deduction constants and the
    // $100,000 Tax Table cutoff (the passages and the worksheet name it).
    private static List<decimal> KnownAmounts(ITaxYearRules rules, FilingStatus status, List<CalcStep> steps,
        TaxBreakdown breakdown, decimal regularDeduction, decimal wages)
    {
        var amounts = new List<decimal>(steps.Select(s => s.Amount))
        {
            breakdown.TaxedAmount,
            BracketTax(rules.Brackets(status), breakdown.TaxedAmount).Tax,
            regularDeduction,
            rules.DependentMinimum,
            rules.DependentEarnedIncomeAddOn,
            wages + rules.DependentEarnedIncomeAddOn,
            TaxTableLimit,
        };
        if (breakdown.RowAtLeast is { } a) amounts.Add(a);
        if (breakdown.RowLessThan is { } b) amounts.Add(b);
        foreach (var p in breakdown.Portions)
            amounts.AddRange([p.From, p.To, p.Amount, p.Tax]);
        amounts.AddRange(rules.Brackets(status).Where(t => t.UpTo is not null).Select(t => t.UpTo!.Value));
        return amounts.Distinct().ToList();
    }
}
