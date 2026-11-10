using System.Text.Json;
using TaxWise.Core;
using TaxWise.Core.Chunking;

namespace TaxWise.Ingestion;

/// <summary>data/sources/ingest-report.json: what the human reads to sanity-check the chunker (BUILD_PLAN §4.2 step 8).</summary>
public static class IngestReport
{
    public static void Write(string path, IReadOnlyList<Chunk> chunks, IReadOnlyList<CoverCheck> covers)
    {
        var withTokens = chunks.Select(c => (Chunk: c, Tokens: TextUtil.EstimateTokens(c.Text))).ToList();
        var report = new
        {
            generatedOn = DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd"),
            totalChunks = chunks.Count,
            averageTokens = withTokens.Count == 0 ? 0 : Math.Round(withTokens.Average(x => x.Tokens), 1),
            coverChecks = covers,
            documents = withTokens
                .GroupBy(x => (x.Chunk.DocId, x.Chunk.TaxYear))
                .Select(g => new
                {
                    docId = g.Key.DocId,
                    taxYear = g.Key.TaxYear,
                    chunks = g.Count(),
                    averageTokens = Math.Round(g.Average(x => x.Tokens), 1),
                    pages = $"{g.Min(x => x.Chunk.PageStart)}–{g.Max(x => x.Chunk.PageEnd)}",
                }),
            shortest = withTokens.OrderBy(x => x.Tokens).Take(10).Select(Describe),
            longest = withTokens.OrderByDescending(x => x.Tokens).Take(10).Select(Describe),
        };
        File.WriteAllText(path, JsonSerializer.Serialize(report, Manifest.JsonOptions) + "\n");
    }

    private static object Describe((Chunk Chunk, int Tokens) x) => new
    {
        id = x.Chunk.Id,
        tokens = x.Tokens,
        chapter = x.Chunk.Chapter,
        section = x.Chunk.Section,
        pages = x.Chunk.PageStart == x.Chunk.PageEnd ? $"{x.Chunk.PageStart}" : $"{x.Chunk.PageStart}–{x.Chunk.PageEnd}",
        preview = x.Chunk.Text.Length <= 160 ? x.Chunk.Text : x.Chunk.Text[..160] + "…",
    };
}

/// <summary>One row of the edition check the human confirms (BUILD_PLAN §12 item 2).</summary>
public sealed record CoverCheck(string DocId, int ManifestYear, string Detected, string Status);
