using TaxWise.Core.Guardrails;

namespace TaxWise.Core.Tests.Guardrails;

public sealed class DollarAmountValidatorTests
{
    // Slide 11 scenario: wages, standard deduction, taxable income, tax, withholding, refund and the bracket breakdown.
    private static readonly decimal[] Known = [28_000m, 15_750m, 12_250m, 1_235m, 1_500m, 265m, 11_925m, 1_192.50m, 350m, 42m];

    [Fact]
    public void CorrectExplanation_IsAccepted()
    {
        const string text = "Your $28,000 in wages minus the $15,750 standard deduction leaves $12,250 of taxable income. " +
                            "The IRS Tax Table puts the tax for that row at $1,235. You had $1,500.00 withheld, so you get $265 back. " +
                            "The first 11,925 is taxed at 10% for 2025.";

        var result = DollarAmountValidator.Validate(text, Known);

        Assert.True(result.IsValid);
        Assert.Empty(result.Unexpected);
    }

    [Fact]
    public void MadeUpAmount_IsRejected()
    {
        var result = DollarAmountValidator.Validate("Your tax is $1,234.00, so you get a refund.", Known);

        Assert.False(result.IsValid);
        Assert.Equal(["$1,234.00"], result.Unexpected);
    }

    [Fact]
    public void ExactFormulaAmountFromTheMockup_IsRejected()
    {
        Assert.False(DollarAmountValidator.Validate("The exact tax would be $1,231.50.", Known).IsValid);
    }

    [Theory]
    [InlineData("You owe 1,234 dollars.", "1,234")]          // bare number with comma thousands
    [InlineData("That is $12k of income.", "$12")]           // shorthand reads as $12, which isn't known
    [InlineData("A refund of $265.01.", "$265.01")]          // must match to the cent
    [InlineData("About $ 900 in tax.", "$ 900")]
    public void Variants_AreCaught(string text, string unexpected)
    {
        var result = DollarAmountValidator.Validate(text, Known);
        Assert.False(result.IsValid);
        Assert.Equal([unexpected], result.Unexpected);
    }

    [Theory]
    [InlineData("Tax year 2025 uses rates of 10% and 12%.")] // years and percentages aren't dollar amounts
    [InlineData("See Form 1040, line 16.")]
    [InlineData("The tax is $1,235.")]                       // sentence-ending period isn't a decimal
    public void NonAmounts_AreIgnored(string text)
    {
        Assert.True(DollarAmountValidator.Validate(text, Known).IsValid);
    }

    [Fact]
    public void Extract_ParsesValues()
    {
        Assert.Equal([1_192.50m, 28_000m, 7m],
            DollarAmountValidator.Extract("$1,192.50 of 28,000 and $7").Select(a => a.Value));
    }
}
