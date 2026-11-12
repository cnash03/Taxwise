using System.Globalization;

namespace TaxWise.Shared.Display;

/// <summary>Display text for the calculator, shared by the API (prompt, canned text) and the App.</summary>
public static class CalcLabels
{
    public static string FilingStatus(FilingStatus status) => status switch
    {
        Shared.FilingStatus.Single => "Single",
        Shared.FilingStatus.MarriedFilingJointly => "Married filing jointly",
        Shared.FilingStatus.MarriedFilingSeparately => "Married filing separately",
        Shared.FilingStatus.HeadOfHousehold => "Head of household",
        _ => status.ToString(),
    };

    /// <summary>"$28,000" for whole dollars, "$1,192.50" otherwise.</summary>
    public static string Money(decimal amount) =>
        "$" + amount.ToString(amount == decimal.Truncate(amount) ? "#,##0" : "#,##0.00", CultureInfo.InvariantCulture);

    /// <summary>"10%", "22%".</summary>
    public static string Rate(decimal rate) => (rate * 100).ToString("0.##", CultureInfo.InvariantCulture) + "%";

    public static string EstimateNote(int taxYear) => $"Estimate: official {taxYear} Tax Table not yet published";

    /// <summary>Shown under every result (BUILD_PLAN §7.1 "Not included").</summary>
    public const string NotIncluded =
        "Not included: credits (education credits, EITC, child tax credit), the 2025–2028 deductions for qualified tips " +
        "and overtime, other income, and itemized deductions. If you earned tips, your real tax may be lower.";
}
