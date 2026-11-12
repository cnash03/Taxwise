using TaxWise.Shared.Display;

namespace TaxWise.Core.Calculator;

/// <summary>
/// The canned explanation shown when Claude's explanation can't be used (no sources, a refusal, or the dollar guardrail
/// failed twice). Built only from the calculated numbers, so it always passes <c>DollarAmountValidator</c>.
/// </summary>
public static class CalcNarrative
{
    public static string Canned(CalcResult r)
    {
        var m = CalcLabels.Money;
        var deduction = r.CanBeClaimedAsDependent
            ? $"Because someone can claim you as a dependent, your standard deduction is {m(r.StandardDeduction)}."
            : $"Your standard deduction is {m(r.StandardDeduction)}.";
        var taxable = r.TaxableIncome == 0
            ? $"That covers all of your wages of {m(r.Wages)}, so you have no taxable income and no income tax."
            : $"Subtracting it from your wages of {m(r.Wages)} leaves {m(r.TaxableIncome)} of taxable income, and the tax on that is {m(r.Tax)}.";
        var result = r.RefundOrOwed switch
        {
            > 0 => $"You had {m(r.FederalWithholding)} withheld, more than the tax, so you'd get a refund of {m(r.RefundOrOwed)}.",
            < 0 => $"You had {m(r.FederalWithholding)} withheld, less than the tax, so you'd owe {m(-r.RefundOrOwed)}.",
            _ => "Your withholding exactly covers the tax, so you'd neither get a refund nor owe anything.",
        };
        return $"{deduction} {taxable} {result}";
    }
}
