namespace TaxWise.Rag.Voyage;

/// <summary>"Voyage" config section (BUILD_PLAN §3). <see cref="ApiKey"/> comes from user-secrets / app settings only.</summary>
public sealed class VoyageOptions
{
    public const string SectionName = "Voyage";

    public string? ApiKey { get; set; }
    public string EmbedModel { get; set; } = "voyage-3.5";
    public string RerankModel { get; set; } = "rerank-2.5";
    public string BaseUrl { get; set; } = "https://api.voyageai.com/v1/";

    /// <summary>Texts per /embeddings request (BUILD_PLAN §4.2 step 6).</summary>
    public int EmbedBatchSize { get; set; } = 128;

    /// <summary>First retry delay; later retries back off exponentially with jitter.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(2);
}
