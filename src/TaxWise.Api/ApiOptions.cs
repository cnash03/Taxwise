namespace TaxWise.Api;

/// <summary>"TaxYears" config section (BUILD_PLAN §1a, §3).</summary>
public sealed class TaxYearOptions
{
    public const string SectionName = "TaxYears";

    public int[] Supported { get; set; } = [];
    /// <summary>Years whose full IRS publications are ingested; other years get the "limited sources" banner.</summary>
    public int[] FullKnowledgeBase { get; set; } = [];
    public int DefaultChat { get; set; }
    public int DefaultCalculator { get; set; }
}

public static class AskLimits
{
    public const int MaxQuestionLength = 1000;
}

public static class CalcLimits
{
    /// <summary>Upper bound for wages and withholding; well above any student scenario.</summary>
    public const decimal MaxAmount = 10_000_000m;
}
