using System.Text.Json;
using System.Text.Json.Serialization;
using TaxWise.Core.Chunking;

namespace TaxWise.Rag.Sources;

/// <summary>data/sources/manifest.json (BUILD_PLAN §4.1).</summary>
public sealed class Manifest
{
    public List<ManifestEntry> Documents { get; set; } = [];

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static Manifest Load(string path) =>
        JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path), JsonOptions)
        ?? throw new InvalidOperationException($"Could not read {path}.");

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions) + "\n");
}

public sealed class ManifestEntry
{
    public required string DocId { get; set; }
    public required string Title { get; set; }
    public required string Url { get; set; }
    public string? PriorYearUrl { get; set; }
    public int TaxYear { get; set; }
    /// <summary>publication | instructions | rev-proc</summary>
    public required string Kind { get; set; }
    public string Sha256 { get; set; } = "";
    public List<RevProcSectionEntry>? RevProcSections { get; set; }
    public string? Note { get; set; }

    [JsonIgnore] public bool IsRevProc => Kind == "rev-proc";
    [JsonIgnore] public string FileName => $"{DocId}.pdf";

    /// <summary>Every tax year this document produces chunks for.</summary>
    [JsonIgnore]
    public IReadOnlyList<int> TaxYears =>
        RevProcSections is { Count: > 0 } s ? s.Select(x => x.TaxYear).Distinct().ToList() : [TaxYear];

    public SourceDocument ToSourceDocument() => new(DocId, Title, TaxYear);
}

public sealed class RevProcSectionEntry
{
    public int Number { get; set; }
    public int TaxYear { get; set; }
}
