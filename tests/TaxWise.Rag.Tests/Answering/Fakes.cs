using TaxWise.Core;
using TaxWise.Core.Guardrails;
using TaxWise.Rag.Answering;
using TaxWise.Rag.VectorStore;
using TaxWise.Rag.Voyage;

namespace TaxWise.Rag.Tests.Answering;

internal static class TestChunks
{
    public static Chunk Make(int i, string text = "", int taxYear = 2025) =>
        new($"p970-ty{taxYear}-p{i}-{i:D3}", "p970", "Pub 970, Tax Benefits for Education", taxYear,
            "Chapter 1. Scholarships, Fellowship Grants, Grants, and Tuition Reductions", $"Section {i}",
            i, i, text.Length > 0 ? text : $"Passage {i} text. It has two sentences.");
}

/// <summary>Voyage fake: a fixed query vector, and rerank scores taken from a script (by document index).</summary>
internal sealed class FakeVoyage(params double[] rerankScores) : IVoyageClient
{
    public List<VoyageInputType> EmbedCalls { get; } = [];

    public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, VoyageInputType inputType, CancellationToken ct = default)
    {
        EmbedCalls.Add(inputType);
        return Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => new[] { 1f, 0f }).ToList());
    }

    public Task<IReadOnlyList<RerankResult>> RerankAsync(string query, IReadOnlyList<string> documents, int? topK = null, CancellationToken ct = default)
    {
        var results = documents.Select((_, i) => new RerankResult(i, i < rerankScores.Length ? rerankScores[i] : 0))
            .OrderByDescending(r => r.RelevanceScore)
            .Take(topK ?? documents.Count)
            .ToList();
        return Task.FromResult<IReadOnlyList<RerankResult>>(results);
    }
}

internal sealed class FakeStore(IReadOnlyList<Chunk> hits) : IChunkStore
{
    public List<(int TaxYear, int Limit, IReadOnlyCollection<string>? DocIds)> Searches { get; } = [];

    public Task<IReadOnlyList<ScoredChunk>> SearchAsync(float[] vector, int taxYear, int limit,
        IReadOnlyCollection<string>? docIds = null, CancellationToken ct = default)
    {
        Searches.Add((taxYear, limit, docIds));
        return Task.FromResult<IReadOnlyList<ScoredChunk>>(hits.Take(limit).Select(c => new ScoredChunk(c, 0.5)).ToList());
    }

    public Task<IReadOnlyList<Chunk>> GetAsync(IReadOnlyList<string> chunkIds, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Chunk>>(chunkIds.Select(id => hits.FirstOrDefault(c => c.Id == id)).OfType<Chunk>().ToList());

    public Task EnsureCollectionAsync(bool recreate, CancellationToken ct = default) => throw new NotSupportedException();
    public Task UpsertAsync(IReadOnlyList<Chunk> chunks, IReadOnlyList<float[]> vectors, CancellationToken ct = default) => throw new NotSupportedException();
    public Task DeleteAsync(string docId, int taxYear, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<ulong> CountAsync(CancellationToken ct = default) => throw new NotSupportedException();
}

/// <summary>Answerer fake that records calls; returns <see cref="Draft"/> or a model refusal.</summary>
internal sealed class FakeAnswerer(DraftAnswer? draft, bool refuse = false) : IAnswerer
{
    public int Calls { get; private set; }
    public IReadOnlyList<Chunk>? LastPassages { get; private set; }

    public Task<AnswererResult> AnswerAsync(string question, IReadOnlyList<Chunk> passages, int taxYear, bool plainLanguage, CancellationToken ct = default)
    {
        Calls++;
        LastPassages = passages;
        var call = new ModelCallInfo("fake", 1, 10, 10, 0, refuse ? "refusal" : "end_turn");
        return Task.FromResult(new AnswererResult(refuse ? null : draft, refuse, call));
    }
}

internal sealed class FakeRetriever(RetrievalResult result) : IRetriever
{
    public Task<RetrievalResult> RetrieveAsync(string question, int taxYear, IReadOnlyCollection<string>? docIds = null,
        CancellationToken ct = default) => Task.FromResult(result);
}
