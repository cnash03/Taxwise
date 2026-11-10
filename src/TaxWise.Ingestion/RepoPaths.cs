namespace TaxWise.Ingestion;

/// <summary>Locates the repo root (the folder holding TaxWise.sln) so paths work from any working directory.</summary>
public static class RepoPaths
{
    public static string Root { get; } = FindRoot();
    public static string Sources => Path.Combine(Root, "data", "sources");
    public static string Manifest => Path.Combine(Sources, "manifest.json");
    public static string PdfDir => Path.Combine(Sources, "pdf");
    public static string IngestReport => Path.Combine(Sources, "ingest-report.json");

    private static string FindRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "TaxWise.sln"))) return dir.FullName;
        }
        throw new InvalidOperationException("Could not find TaxWise.sln above the current directory.");
    }
}
