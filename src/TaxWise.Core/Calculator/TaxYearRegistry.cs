namespace TaxWise.Core.Calculator;

/// <summary>
/// Every <see cref="ITaxYearRules"/> in this assembly, found by reflection, so adding a year means adding one
/// <c>TaxYear{yyyy}.cs</c> file and nothing else (BUILD_PLAN §7.1).
/// </summary>
public static class TaxYearRegistry
{
    private static readonly IReadOnlyDictionary<int, ITaxYearRules> Rules = typeof(ITaxYearRules).Assembly.GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ITaxYearRules).IsAssignableFrom(t))
        .Select(t => (ITaxYearRules)Activator.CreateInstance(t)!)
        .ToDictionary(r => r.TaxYear);

    /// <summary>Ascending.</summary>
    public static IReadOnlyList<int> Supported { get; } = Rules.Keys.Order().ToList();

    /// <summary>Null when the year isn't supported (the API answers 400).</summary>
    public static ITaxYearRules? Get(int taxYear) => Rules.GetValueOrDefault(taxYear);
}
