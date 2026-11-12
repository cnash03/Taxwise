using Microsoft.Extensions.Options;
using TaxWise.Core;
using TaxWise.Rag.VectorStore;
using TaxWise.Rag.Voyage;

namespace TaxWise.Rag.Answering;

/// <summary>A passage kept after reranking. Its position in <see cref="RetrievalResult.Passages"/> is its P-number minus one.</summary>
public sealed record RetrievedPassage(Chunk Chunk, double RerankScore);

/// <summary>
/// <see cref="Searched"/> is how many vector hits were reranked; <see cref="Passages"/> are the best ones, best first.
/// <see cref="MeetsThreshold"/> is false when even the best passage scored below <c>Retrieval:MinRerankScore</c>.
/// </summary>
public sealed record RetrievalResult(int Searched, IReadOnlyList<RetrievedPassage> Passages, double TopScore, bool MeetsThreshold);

public interface IRetriever
{
    /// <param name="docIds">When set, only these manifest documents are searched (Explain my form, BUILD_PLAN §6).</param>
    Task<RetrievalResult> RetrieveAsync(string question, int taxYear, IReadOnlyCollection<string>? docIds = null,
        CancellationToken ct = default);
}

/// <summary>Embed the question → Qdrant top K for the tax year → Voyage rerank → keep the top N (BUILD_PLAN §5.1).</summary>
public sealed class Retriever(IVoyageClient voyage, IChunkStore store, IOptions<RetrievalOptions> options) : IRetriever
{
    private readonly RetrievalOptions _options = options.Value;

    public async Task<RetrievalResult> RetrieveAsync(string question, int taxYear, IReadOnlyCollection<string>? docIds = null,
        CancellationToken ct = default)
    {
        var vectors = await voyage.EmbedAsync([question], VoyageInputType.Query, ct);
        var hits = await store.SearchAsync(vectors[0], taxYear, _options.VectorTopK, docIds, ct);
        if (hits.Count == 0) return new RetrievalResult(0, [], 0, false);

        // Rerank on the same breadcrumb + text that was embedded, so section names still count.
        var ranked = await voyage.RerankAsync(question, hits.Select(h => h.Chunk.EmbeddingText).ToList(), _options.RerankTopK, ct);
        var kept = ranked
            .OrderByDescending(r => r.RelevanceScore)
            .Take(_options.RerankTopK)
            .Select(r => new RetrievedPassage(hits[r.Index].Chunk, r.RelevanceScore))
            .ToList();

        var top = kept.Count > 0 ? kept[0].RerankScore : 0;
        return new RetrievalResult(hits.Count, kept, top, kept.Count > 0 && top >= _options.MinRerankScore);
    }
}
