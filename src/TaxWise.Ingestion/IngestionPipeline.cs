using TaxWise.Rag.Sources;
using Microsoft.Extensions.Logging;
using TaxWise.Core;
using TaxWise.Core.Chunking;
using TaxWise.Rag.VectorStore;
using TaxWise.Rag.Voyage;

namespace TaxWise.Ingestion;

/// <summary>download → parse → chunk → embed → upsert (BUILD_PLAN §4.2).</summary>
public sealed class IngestionPipeline(
    SourceDownloader downloader, IVoyageClient voyage, IChunkStore store, ILogger<IngestionPipeline> log)
{
    /// <returns>Process exit code: 0 ok, 2 when a document needs a human (wrong edition, missing file).</returns>
    public async Task<int> RunAsync(IngestionArgs args, CancellationToken ct)
    {
        var manifest = Manifest.Load(RepoPaths.Manifest);
        var entries = manifest.Documents
            .Where(d => args.Only is null || d.DocId == args.Only)
            .Where(d => args.Year is null || d.TaxYears.Contains(args.Year.Value))
            .ToList();
        if (entries.Count == 0)
        {
            log.LogError("No manifest entries match --only {Only} / --year {Year}.", args.Only, args.Year);
            return 1;
        }

        var covers = new List<CoverCheck>();
        var chunks = new List<Chunk>();
        var manifestChanged = false;

        foreach (var entry in entries)
        {
            var (outcome, sha) = await downloader.EnsureAsync(entry, args.Download, ct);
            if (outcome == SourceDownloader.Outcome.Missing)
            {
                covers.Add(new(entry.DocId, entry.TaxYear, "-", "MISSING: run with --download"));
                continue;
            }
            if (outcome == SourceDownloader.Outcome.HashMismatch)
            {
                covers.Add(new(entry.DocId, entry.TaxYear, "-", $"CHANGED: sha256 {sha![..12]}… ≠ manifest; the IRS may have posted a new edition"));
                continue;
            }
            if (entry.Sha256.Length == 0)
            {
                entry.Sha256 = sha!;
                manifestChanged = true;
            }

            var pdf = PdfLayoutReader.Read(Path.Combine(RepoPaths.PdfDir, entry.FileName));
            var docChunks = Chunk(entry, pdf);
            var check = CheckEdition(entry, pdf, docChunks);
            covers.Add(check);
            if (!check.Status.StartsWith("ok", StringComparison.Ordinal)) continue;

            docChunks = docChunks.Where(c => args.Year is null || c.TaxYear == args.Year).ToList();
            Validate(entry, docChunks);
            log.LogInformation("{DocId}: {Pages} pages → {Chunks} chunks", entry.DocId, pdf.PageCount, docChunks.Count);
            chunks.AddRange(docChunks);
        }

        if (manifestChanged) manifest.Save(RepoPaths.Manifest);
        PrintCoverTable(covers);
        IngestReport.Write(RepoPaths.IngestReport, chunks, covers);
        log.LogInformation("Wrote {Path} ({Count} chunks).", Path.GetRelativePath(RepoPaths.Root, RepoPaths.IngestReport), chunks.Count);

        if (covers.Any(c => !c.Status.StartsWith("ok", StringComparison.Ordinal)))
        {
            log.LogError("Stopping before embedding: fix the flagged documents above (BUILD_PLAN §4.1), then re-run.");
            return 2;
        }
        if (args.DryRun) return 0;

        await store.EnsureCollectionAsync(args.Recreate, ct);
        foreach (var group in chunks.GroupBy(c => (c.DocId, c.TaxYear)))
        {
            var docChunks = group.ToList();
            var vectors = await voyage.EmbedAsync(docChunks.Select(c => c.EmbeddingText).ToList(), VoyageInputType.Document, ct);
            await store.DeleteAsync(group.Key.DocId, group.Key.TaxYear, ct);
            await store.UpsertAsync(docChunks, vectors, ct);
            log.LogInformation("Upserted {DocId} TY{Year}: {Count} chunks", group.Key.DocId, group.Key.TaxYear, docChunks.Count);
        }
        log.LogInformation("Collection now holds {Count} points.", await store.CountAsync(ct));
        return 0;
    }

    private static IReadOnlyList<Chunk> Chunk(ManifestEntry entry, PdfLayoutReader.Result pdf) =>
        entry.IsRevProc
            ? RevProcChunker.Chunk(entry.ToSourceDocument(), pdf.Blocks,
                entry.RevProcSections!.Select(s => new RevProcSection(s.Number, s.TaxYear)).ToList())
            : SectionChunker.Chunk(entry.ToSourceDocument(), pdf.Blocks);

    /// <summary>
    /// Publications: the year printed on the cover must equal the manifest year. Rev. Procs: each
    /// ingested section's heading ("SECTION 4. 2026 ADJUSTED ITEMS") must name the year it is tagged with.
    /// </summary>
    private static CoverCheck CheckEdition(ManifestEntry entry, PdfLayoutReader.Result pdf, IReadOnlyList<Chunk> chunks)
    {
        if (entry.IsRevProc)
        {
            var headings = chunks.GroupBy(c => (c.TaxYear, c.Chapter)).Select(g => g.Key).ToList();
            var bad = headings.Where(h => h.Chapter is null || !h.Chapter.Contains(h.TaxYear.ToString())).ToList();
            var detected = string.Join("; ", headings.Select(h => $"TY{h.TaxYear} ← {h.Chapter}"));
            return new(entry.DocId, entry.TaxYear, detected,
                bad.Count == 0 ? "ok" : "MISMATCH: section heading doesn't name its tax year");
        }

        var year = CoverYearDetector.Detect(pdf.FirstPage);
        var status = year == entry.TaxYear ? "ok"
            : year is null ? "UNKNOWN: couldn't read the year on the cover; check by hand"
            : $"MISMATCH: cover says {year}; point url at the irs-prior copy";
        return new(entry.DocId, entry.TaxYear, year?.ToString() ?? "?", status);
    }

    private static void Validate(ManifestEntry entry, IReadOnlyList<Chunk> chunks)
    {
        var problems = chunks
            .Where(c => string.IsNullOrWhiteSpace(c.Section) || c.PageStart < 1 || c.PageEnd < c.PageStart
                        || !entry.TaxYears.Contains(c.TaxYear) || string.IsNullOrWhiteSpace(c.Text))
            .Select(c => c.Id)
            .ToList();
        if (problems.Count > 0)
            throw new InvalidOperationException($"{entry.DocId}: invalid chunks: {string.Join(", ", problems.Take(5))}");
        var duplicates = chunks.GroupBy(c => c.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
            throw new InvalidOperationException($"{entry.DocId}: duplicate chunk ids: {string.Join(", ", duplicates.Take(5))}");
    }

    private static void PrintCoverTable(IReadOnlyList<CoverCheck> covers)
    {
        Console.WriteLine();
        Console.WriteLine("Edition check (BUILD_PLAN §12 item 2: a human confirms this table)");
        Console.WriteLine($"{"docId",-10} {"manifest",-9} {"detected",-10} status");
        foreach (var c in covers)
        {
            var detected = c.Detected.Length > 10 ? "(sections)" : c.Detected;
            Console.WriteLine($"{c.DocId,-10} {c.ManifestYear,-9} {detected,-10} {c.Status}");
            if (c.Detected.Length > 10) Console.WriteLine($"{"",-21} {c.Detected}");
        }
        Console.WriteLine();
    }
}
