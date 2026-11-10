namespace TaxWise.Rag.VectorStore;

/// <summary>"Qdrant" config section (BUILD_PLAN §3). <see cref="ApiKey"/> is only needed for Qdrant Cloud.</summary>
public sealed class QdrantOptions
{
    public const string SectionName = "Qdrant";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 6334;
    public bool UseHttps { get; set; }
    public string Collection { get; set; } = "taxwise";
    public string? ApiKey { get; set; }

    /// <summary>voyage-3.5 default output dimension.</summary>
    public ulong VectorSize { get; set; } = 1024;
}
