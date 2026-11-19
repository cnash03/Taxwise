namespace TaxWise.Shared;

/// <summary>
/// Whose tax rules a chat question is about. Stored as the <c>jurisdiction</c> payload on every chunk and sent in
/// <see cref="AskRequest.Jurisdiction"/>. Chunks ingested before Colorado was added have no field and count as federal
/// (DECISIONS #23).
/// </summary>
public static class Jurisdictions
{
    /// <summary>U.S. federal: the IRS documents.</summary>
    public const string Federal = "US";

    /// <summary>Colorado individual income tax: Colorado Department of Revenue documents.</summary>
    public const string Colorado = "CO";

    public static IReadOnlyList<string> All { get; } = [Federal, Colorado];

    /// <summary>Null or blank means federal; codes are matched without regard to case. Unknown codes return null.</summary>
    public static string? Normalize(string? jurisdiction)
    {
        if (string.IsNullOrWhiteSpace(jurisdiction)) return Federal;
        var code = jurisdiction.Trim().ToUpperInvariant();
        return All.Contains(code) ? code : null;
    }

    public static bool IsColorado(string? jurisdiction) => Normalize(jurisdiction) == Colorado;

    /// <summary>"IRS" or "Colorado DOR": who publishes the sources, for labels and chips.</summary>
    public static string Publisher(string? jurisdiction) => IsColorado(jurisdiction) ? "Colorado DOR" : "IRS";

    /// <summary>"federal" or "Colorado", as in "your federal taxes".</summary>
    public static string TaxName(string? jurisdiction) => IsColorado(jurisdiction) ? "Colorado" : "federal";
}
