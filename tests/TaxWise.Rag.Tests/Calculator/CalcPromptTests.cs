using TaxWise.Core.Calculator;
using TaxWise.Rag.Calculator;
using TaxWise.Rag.Tests.Answering;
using TaxWise.Shared;

namespace TaxWise.Rag.Tests.Calculator;

public sealed class CalcPromptTests
{
    private static readonly CalcResult Slide11 =
        TaxCalculator.Calculate(TaxYearRegistry.Get(2025)!, 28_000m, 1_500m, FilingStatus.Single, false);

    [Fact]
    public void SystemPrompt_IsByteStablePerYearAndMode_AndForbidsComputing()
    {
        Assert.Equal(CalcPrompt.SystemPrompt(2026, false), CalcPrompt.SystemPrompt(2026, false));
        Assert.NotEqual(CalcPrompt.SystemPrompt(2026, false), CalcPrompt.SystemPrompt(2026, true));
        Assert.Contains("tax year 2026", CalcPrompt.SystemPrompt(2026, false));
        Assert.Contains("Do not compute", CalcPrompt.SystemPrompt(2025, false));
        Assert.DoesNotContain("{", CalcPrompt.SystemPrompt(2025, true));
    }

    [Fact]
    public void UserMessage_HasPassagesThenTheFixedNumbers()
    {
        var message = CalcPrompt.UserMessage(Slide11, [TestChunks.Make(1), TestChunks.Make(2)]);

        Assert.True(message.IndexOf("<passage id=\"P2\"", StringComparison.Ordinal) < message.IndexOf("<calculation", StringComparison.Ordinal));
        Assert.Contains("tax_year=\"2025\" filing_status=\"Single\" dependent=\"no\" tax_method=\"IRS Tax Table\"", message);
        Assert.Contains("Taxable income: $12,250 (source:", message);
        Assert.Contains("Tax (from IRS Tax Table): $1,235", message);
        Assert.Contains("Estimated refund: $265", message);
        Assert.Contains("$12,250 to $12,300 row", message);
        Assert.Contains("10% of $11,925 (from $0 to $11,925) = $1,192.50", message);
        Assert.Contains("12% of $350 (from $11,925 to $12,275) = $42", message);
        Assert.DoesNotContain("<retry>", message);
    }

    [Fact]
    public void RetryMessage_NamesTheRejectedAmounts()
    {
        var message = CalcPrompt.UserMessage(Slide11, [TestChunks.Make(1)], ["$1,234.00"]);
        Assert.EndsWith("</retry>", message);
        Assert.Contains("$1,234.00", message);
    }

    [Fact]
    public void Schema_IsTheExplanationShape()
    {
        Assert.Equal(["status", "explanation", "citations"],
            CalcPrompt.Schema["required"].EnumerateArray().Select(e => e.GetString()));
    }
}
