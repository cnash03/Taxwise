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

/// <summary>
/// "UsageLimit" config section: a per-IP cap on the endpoints that call Claude, so a shared download of the App
/// can't run up the Anthropic bill. Off when PermitLimit is 0 (the default, so local runs and the eval aren't limited).
/// </summary>
public sealed class UsageLimitOptions
{
    public const string SectionName = "UsageLimit";
    public const string PolicyName = "claude";

    public int PermitLimit { get; set; }
    public int WindowMinutes { get; set; } = 60;
}
