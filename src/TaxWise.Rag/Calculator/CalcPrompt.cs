using System.Globalization;
using System.Text;
using System.Text.Json;
using TaxWise.Core;
using TaxWise.Core.Calculator;
using TaxWise.Rag.Answering;
using TaxWise.Shared;
using TaxWise.Shared.Display;

namespace TaxWise.Rag.Calculator;

/// <summary>Builds the calculator explanation prompt from the embedded templates in Prompts/ (BUILD_PLAN §7.2).</summary>
public static class CalcPrompt
{
    private static readonly string SystemTemplate = AnswerPrompt.Resource("calc.system.md");

    /// <summary>The explanation JSON schema <c>{ status, explanation, citations[] }</c>, as sent in <c>output_config.format</c>.</summary>
    public static IReadOnlyDictionary<string, JsonElement> Schema { get; } =
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(AnswerPrompt.Resource("calc.schema.json"))!;

    /// <summary>Same text for every calculation with the same year and mode, so the prefix stays cacheable.</summary>
    public static string SystemPrompt(int taxYear, bool plainLanguage) =>
        SystemTemplate
            .Replace("{TaxYear}", taxYear.ToString(CultureInfo.InvariantCulture))
            .Replace("{WritingStyle}", AnswerPrompt.WritingStyle(plainLanguage))
            .TrimEnd();

    /// <summary>
    /// The passages as P1..Pn, then the finished step table and bracket breakdown as fixed data. On the retry,
    /// <paramref name="rejectedAmounts"/> names the amounts the first explanation made up.
    /// </summary>
    public static string UserMessage(CalcResult r, IReadOnlyList<Chunk> passages, IReadOnlyList<string>? rejectedAmounts = null)
    {
        var m = CalcLabels.Money;
        var sb = new StringBuilder();
        AnswerPrompt.AppendPassages(sb, passages);

        var b = r.Breakdown;
        var method = b.Method == TaxBreakdown.TaxTableMethod
            ? (r.IsEstimate ? "Tax Table method (estimate: the official table isn't published yet)" : "IRS Tax Table")
            : "Tax Computation Worksheet (bracket formula)";
        sb.Append($"<calculation tax_year=\"{r.TaxYear}\" filing_status=\"{CalcLabels.FilingStatus(r.FilingStatus)}\" ")
          .Append($"dependent=\"{(r.CanBeClaimedAsDependent ? "yes" : "no")}\" tax_method=\"{AnswerPrompt.Attr(method)}\">\n");
        foreach (var s in r.Steps)
            sb.Append($"{s.Label}: {m(s.Amount)} (source: {s.SourceRef})\n");

        sb.Append(b.Method == TaxBreakdown.TaxTableMethod
            ? $"How the tax was found: the taxable income is in the {m(b.RowAtLeast!.Value)} to {m(b.RowLessThan!.Value)} row of the tax table. " +
              $"The table's tax for that row is the tax on the row's midpoint, {m(b.TaxedAmount)}, rounded to the nearest dollar:\n"
            : $"How the tax was found: the rates applied to the taxable income of {m(b.TaxedAmount)}:\n");
        foreach (var p in b.Portions)
            sb.Append($"  {CalcLabels.Rate(p.Rate)} of {m(p.Amount)} (from {m(p.From)} to {m(p.To)}) = {m(p.Tax)}\n");
        sb.Append("</calculation>");

        if (rejectedAmounts is { Count: > 0 })
        {
            sb.Append("\n\n<retry>Your previous explanation used amounts that are not in <calculation>: ")
              .Append(string.Join(", ", rejectedAmounts))
              .Append(". Write it again using only dollar amounts copied exactly from <calculation>, or no dollar amounts at all.</retry>");
        }
        return sb.ToString();
    }
}
