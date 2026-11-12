using TaxWise.Core;

namespace TaxWise.Rag.VectorStore;

public interface IChunkStore
{
    /// <summary>Creates the collection and payload indexes if missing; drops it first when <paramref name="recreate"/>.</summary>
    Task EnsureCollectionAsync(bool recreate, CancellationToken ct = default);

    /// <summary>Idempotent upsert: point ids are derived from <see cref="Chunk.Id"/>.</summary>
    Task UpsertAsync(IReadOnlyList<Chunk> chunks, IReadOnlyList<float[]> vectors, CancellationToken ct = default);

    /// <summary>Nearest chunks for <paramref name="vector"/> in one tax year, optionally limited to some documents.</summary>
    Task<IReadOnlyList<ScoredChunk>> SearchAsync(float[] vector, int taxYear, int limit,
        IReadOnlyCollection<string>? docIds = null, CancellationToken ct = default);

    /// <summary>The stored chunks with these IDs, in the order asked; IDs that aren't stored are skipped.</summary>
    Task<IReadOnlyList<Chunk>> GetAsync(IReadOnlyList<string> chunkIds, CancellationToken ct = default);

    /// <summary>Removes one document's points for one tax year, so a re-run can't leave stale chunks behind.</summary>
    Task DeleteAsync(string docId, int taxYear, CancellationToken ct = default);

    Task<ulong> CountAsync(CancellationToken ct = default);
}
