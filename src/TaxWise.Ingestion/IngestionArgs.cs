namespace TaxWise.Ingestion;

/// <summary>
/// <c>dotnet run --project src/TaxWise.Ingestion -- [--download] [--recreate] [--only p970] [--year 2026]
/// [--dry-run] [--spot-check] [--qdrant-host h] [--qdrant-api-key k]</c>
/// </summary>
public sealed record IngestionArgs
{
    public bool Download { get; init; }
    public bool Recreate { get; init; }
    public bool DryRun { get; init; }
    public bool SpotCheck { get; init; }
    public bool SpotCheckOnly { get; init; }
    public string? Only { get; init; }
    public int? Year { get; init; }
    public string? QdrantHost { get; init; }
    public string? QdrantApiKey { get; init; }

    public const string Usage = """
        Usage: dotnet run --project src/TaxWise.Ingestion -- [options]
          --download          download missing PDFs (and fill in manifest sha256)
          --dry-run           parse + chunk + write ingest-report.json; no Voyage/Qdrant calls
          --recreate          drop and recreate the Qdrant collection first
          --only <docId>      only this manifest entry (e.g. p970)
          --year <year>       only chunks for this tax year (e.g. 2026)
          --spot-check        after ingesting, run the Phase 1 acceptance searches
          --spot-check-only   run the acceptance searches without ingesting
          --qdrant-host <h>   override Qdrant:Host (e.g. a Qdrant Cloud cluster)
          --qdrant-api-key <k> override Qdrant:ApiKey (also turns on HTTPS)
        """;

    public static IngestionArgs Parse(string[] args)
    {
        var result = new IngestionArgs();
        for (var i = 0; i < args.Length; i++)
        {
            result = args[i] switch
            {
                "--download" => result with { Download = true },
                "--recreate" => result with { Recreate = true },
                "--dry-run" => result with { DryRun = true },
                "--spot-check" => result with { SpotCheck = true },
                "--spot-check-only" => result with { SpotCheck = true, SpotCheckOnly = true },
                "--only" => result with { Only = Value(args, ref i) },
                "--year" => result with { Year = int.Parse(Value(args, ref i)) },
                "--qdrant-host" => result with { QdrantHost = Value(args, ref i) },
                "--qdrant-api-key" => result with { QdrantApiKey = Value(args, ref i) },
                _ => throw new ArgumentException($"Unknown option '{args[i]}'.\n{Usage}"),
            };
        }
        return result;
    }

    private static string Value(string[] args, ref int i) =>
        ++i < args.Length ? args[i] : throw new ArgumentException($"{args[i - 1]} needs a value.\n{Usage}");
}
