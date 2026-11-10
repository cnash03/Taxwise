using TaxWise.Rag.VectorStore;
using TaxWise.Rag.Voyage;

namespace TaxWise.Ingestion;

/// <summary>The Phase 1 acceptance searches (BUILD_PLAN §4.2), runnable in one command.</summary>
public sealed class SpotCheck(IVoyageClient voyage, IChunkStore store)
{
    /// <param name="Informational">Printed for the human but not pass/fail.</param>
    private sealed record Case(string Query, int TaxYear, string Expect, Func<TaxWise.Core.Chunk, bool> IsHit, bool Informational = false);

    private static bool IsPub970Ch1(TaxWise.Core.Chunk c) =>
        c.DocId == "p970" && (c.Chapter?.StartsWith("Chapter 1.", StringComparison.Ordinal) ?? false);

    private static readonly Case[] Cases =
    [
        // rp-26-35 puts the 2027 amounts in §3, so the plan's "§4.15" is §3.15 in the real document.
        new("standard deduction", 2027, "Rev. Proc. 2026-35 §3.15 Standard Deduction",
            c => c.DocId == "rp-26-35" && c.Section.StartsWith(".15", StringComparison.Ordinal)),
        new("standard deduction", 2026, "Rev. Proc. 2025-32 §4.14 Standard Deduction",
            c => c.DocId == "rp-25-32" && c.Section.StartsWith(".14", StringComparison.Ordinal)),
        // The bare phrase is ambiguous: 529 plans (Pub 970 ch. 7) and dependency support tests are also
        // valid matches, so it's informational. The scholarship questions below are the real check.
        new("room and board", 2025, "Pub 970 Chapter 1 (informational; the phrase alone is ambiguous)", IsPub970Ch1, Informational: true),
        new("Is my scholarship taxable if part of it pays for room and board?", 2025,
            "Pub 970 Chapter 1, p. 5 (check the page by hand)", IsPub970Ch1),
        new("My scholarship paid my tuition and also gave me $1,200 for housing. Do I have to pay tax on it?", 2025,
            "Pub 970 Chapter 1 (the demo question)", IsPub970Ch1),
    ];

    public async Task<int> RunAsync(CancellationToken ct)
    {
        var failures = 0;
        foreach (var test in Cases)
        {
            var vector = (await voyage.EmbedAsync([test.Query], VoyageInputType.Query, ct))[0];
            var hits = await store.SearchAsync(vector, test.TaxYear, 8, ct: ct);
            var reranked = await voyage.RerankAsync(test.Query, hits.Select(h => h.Chunk.EmbeddingText).ToList(), 4, ct);
            var top = reranked.Select(r => (hits[r.Index].Chunk, r.RelevanceScore)).ToList();

            var pass = top.Any(t => test.IsHit(t.Chunk));
            if (!pass && !test.Informational) failures++;
            var verdict = test.Informational ? "INFO" : pass ? "PASS" : "FAIL";
            Console.WriteLine($"{verdict}  \"{test.Query}\" TY{test.TaxYear}: expect {test.Expect}");
            foreach (var (chunk, score) in top)
                Console.WriteLine($"   {(test.IsHit(chunk) ? "*" : " ")} {score:F3}  {chunk.Id}  p.{chunk.PageStart}  {chunk.Breadcrumb}");
        }

        var count = await store.CountAsync(ct);
        Console.WriteLine($"Collection holds {count} points (compare with totalChunks in ingest-report.json; equal = no duplicates).");
        return failures == 0 ? 0 : 3;
    }
}
