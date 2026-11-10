namespace TaxWise.App.Services;

/// <summary>Choices remembered across launches with MAUI <see cref="IPreferences"/> (BUILD_PLAN §1a, §5 Phase 3).</summary>
public sealed class UserSettings(IPreferences preferences)
{
	/// <summary>Tax years the API supports (appsettings TaxYears:Supported).</summary>
	public static readonly IReadOnlyList<int> SupportedTaxYears = [2025, 2026, 2027];

	/// <summary>Chat and Explain my form default to TY2025, the calculator to TY2026 (BUILD_PLAN §1a).</summary>
	public static int DefaultTaxYear(TaxYearFeature feature) => feature == TaxYearFeature.Calculator ? 2026 : 2025;

	public bool PlainLanguage
	{
		get => preferences.Get("plainLanguage", false);
		set => preferences.Set("plainLanguage", value);
	}

	public int GetTaxYear(TaxYearFeature feature)
	{
		var year = preferences.Get(Key(feature), DefaultTaxYear(feature));
		return SupportedTaxYears.Contains(year) ? year : DefaultTaxYear(feature);
	}

	public void SetTaxYear(TaxYearFeature feature, int year) => preferences.Set(Key(feature), year);

	private static string Key(TaxYearFeature feature) => $"taxYear.{feature}";
}

public enum TaxYearFeature
{
	Chat,
	Forms,
	Calculator,
	Quizzes,
}
