namespace TaxWise.Rag.Voyage;

public enum VoyageInputType { Document, Query }

/// <summary>A reranked document: its index in the input list and its relevance score.</summary>
public sealed record RerankResult(int Index, double RelevanceScore);

public interface IVoyageClient
{
    /// <summary>Embeds <paramref name="texts"/> in batches; the result is in input order.</summary>
    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, VoyageInputType inputType, CancellationToken ct = default);

    /// <summary>Reranks <paramref name="documents"/> against <paramref name="query"/>, best first.</summary>
    Task<IReadOnlyList<RerankResult>> RerankAsync(string query, IReadOnlyList<string> documents, int? topK = null, CancellationToken ct = default);
}

public sealed class VoyageApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
