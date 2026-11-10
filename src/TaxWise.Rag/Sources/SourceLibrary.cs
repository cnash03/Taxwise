using Microsoft.Extensions.Options;

namespace TaxWise.Rag.Sources;

/// <summary>
/// "Sources" config section. <see cref="Directory"/> holds manifest.json and pdf/; it defaults to the repo's
/// data/sources, and must be set when the API runs outside the repo (Azure).
/// </summary>
public sealed class SourceOptions
{
    public const string SectionName = "Sources";

    public string? Directory { get; set; }
}

/// <summary>The manifest's documents and their downloaded PDFs, for the sources and knowledge-base endpoints.</summary>
public sealed class SourceLibrary
{
    private readonly string _pdfDir;
    private readonly Dictionary<string, ManifestEntry> _byId;

    public SourceLibrary(IOptions<SourceOptions> options)
    {
        var dir = string.IsNullOrWhiteSpace(options.Value.Directory) ? RepoPaths.Sources : options.Value.Directory;
        var manifest = Manifest.Load(Path.Combine(dir, "manifest.json"));
        Documents = manifest.Documents;
        _byId = manifest.Documents.ToDictionary(d => d.DocId, StringComparer.OrdinalIgnoreCase);
        _pdfDir = Path.Combine(dir, "pdf");
    }

    public IReadOnlyList<ManifestEntry> Documents { get; }

    /// <summary>Full path of the stored PDF for a manifest doc ID, or null when the ID is unknown or not downloaded.</summary>
    public string? FindPdf(string docId)
    {
        if (!_byId.TryGetValue(docId, out var entry)) return null;
        var path = Path.Combine(_pdfDir, entry.FileName);
        return File.Exists(path) ? path : null;
    }
}
