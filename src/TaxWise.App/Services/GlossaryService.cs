using System.Reflection;
using System.Text.Json;
using TaxWise.Shared.Display;

namespace TaxWise.App.Services;

/// <summary>Loads the human-reviewed glossary embedded from <c>data/glossary.json</c> (BUILD_PLAN §5.2).</summary>
public sealed class GlossaryService
{
	public GlossaryService()
	{
		using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("TaxWise.App.glossary.json");
		var file = stream is null ? null : JsonSerializer.Deserialize<GlossaryFile>(stream, JsonSerializerOptions.Web);
		Terms = file?.Terms ?? [];
		Marker = new GlossaryMarker(Terms);
	}

	public IReadOnlyList<GlossaryTerm> Terms { get; }

	public GlossaryMarker Marker { get; }
}
