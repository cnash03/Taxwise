using Google.Protobuf.Collections;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using TaxWise.Core;
using static Qdrant.Client.Grpc.Conditions;

namespace TaxWise.Rag.VectorStore;

/// <summary>
/// Qdrant-backed chunk store: one collection ("taxwise") for all tax years, cosine distance,
/// payload indexes on docId and taxYear (BUILD_PLAN §1a, §4.2 step 7).
/// </summary>
public sealed class QdrantChunkStore(QdrantClient client, IOptions<QdrantOptions> options) : IChunkStore
{
    private readonly QdrantOptions _options = options.Value;
    private string Collection => _options.Collection;

    public async Task EnsureCollectionAsync(bool recreate, CancellationToken ct = default)
    {
        var exists = await client.CollectionExistsAsync(Collection, ct);
        if (exists && recreate)
        {
            await client.DeleteCollectionAsync(Collection, cancellationToken: ct);
            exists = false;
        }
        if (exists) return;

        await client.CreateCollectionAsync(Collection,
            new VectorParams { Size = _options.VectorSize, Distance = Distance.Cosine }, cancellationToken: ct);
        await client.CreatePayloadIndexAsync(Collection, PayloadKeys.DocId, PayloadSchemaType.Keyword, cancellationToken: ct);
        await client.CreatePayloadIndexAsync(Collection, PayloadKeys.TaxYear, PayloadSchemaType.Integer, cancellationToken: ct);
    }

    public async Task UpsertAsync(IReadOnlyList<Chunk> chunks, IReadOnlyList<float[]> vectors, CancellationToken ct = default)
    {
        if (chunks.Count != vectors.Count)
            throw new ArgumentException($"{chunks.Count} chunks but {vectors.Count} vectors.");

        var points = chunks.Select((chunk, i) =>
        {
            var point = new PointStruct { Id = ChunkIds.ToPointId(chunk.Id), Vectors = vectors[i] };
            foreach (var (key, value) in ChunkPayload.ToPayload(chunk))
                point.Payload[key] = value;
            return point;
        }).ToList();

        foreach (var batch in points.Chunk(256))
            await client.UpsertAsync(Collection, batch, wait: true, cancellationToken: ct);
    }

    public async Task<IReadOnlyList<ScoredChunk>> SearchAsync(float[] vector, int taxYear, int limit,
        IReadOnlyCollection<string>? docIds = null, CancellationToken ct = default)
    {
        var filter = new Filter { Must = { Match(PayloadKeys.TaxYear, taxYear) } };
        if (docIds is { Count: > 0 })
            filter.Must.Add(Match(PayloadKeys.DocId, docIds.ToList()));

        var hits = await client.QueryAsync(Collection, query: vector, filter: filter, limit: (ulong)limit,
            payloadSelector: true, cancellationToken: ct);
        return hits.Select(h => new ScoredChunk(ChunkPayload.FromPayload(h.Payload), h.Score)).ToList();
    }

    public Task DeleteAsync(string docId, int taxYear, CancellationToken ct = default) =>
        client.DeleteAsync(Collection, new Filter { Must = { MatchKeyword(PayloadKeys.DocId, docId), Match(PayloadKeys.TaxYear, taxYear) } },
            wait: true, cancellationToken: ct);

    public Task<ulong> CountAsync(CancellationToken ct = default) =>
        client.CountAsync(Collection, exact: true, cancellationToken: ct);
}

public static class PayloadKeys
{
    public const string ChunkId = "chunkId";
    public const string DocId = "docId";
    public const string DocTitle = "docTitle";
    public const string TaxYear = "taxYear";
    public const string Chapter = "chapter";
    public const string Section = "section";
    public const string PageStart = "pageStart";
    public const string PageEnd = "pageEnd";
    public const string Text = "text";
}

/// <summary>Maps every <see cref="Chunk"/> field to and from a Qdrant payload.</summary>
public static class ChunkPayload
{
    public static Dictionary<string, Value> ToPayload(Chunk c)
    {
        var payload = new Dictionary<string, Value>
        {
            [PayloadKeys.ChunkId] = c.Id,
            [PayloadKeys.DocId] = c.DocId,
            [PayloadKeys.DocTitle] = c.DocTitle,
            [PayloadKeys.TaxYear] = c.TaxYear,
            [PayloadKeys.Section] = c.Section,
            [PayloadKeys.PageStart] = c.PageStart,
            [PayloadKeys.PageEnd] = c.PageEnd,
            [PayloadKeys.Text] = c.Text,
        };
        if (c.Chapter is not null) payload[PayloadKeys.Chapter] = c.Chapter;
        return payload;
    }

    public static Chunk FromPayload(MapField<string, Value> p) => FromPayload((IDictionary<string, Value>)p);

    public static Chunk FromPayload(IDictionary<string, Value> p) => new(
        p[PayloadKeys.ChunkId].StringValue,
        p[PayloadKeys.DocId].StringValue,
        p[PayloadKeys.DocTitle].StringValue,
        (int)p[PayloadKeys.TaxYear].IntegerValue,
        p.TryGetValue(PayloadKeys.Chapter, out var chapter) && chapter.HasStringValue ? chapter.StringValue : null,
        p[PayloadKeys.Section].StringValue,
        (int)p[PayloadKeys.PageStart].IntegerValue,
        (int)p[PayloadKeys.PageEnd].IntegerValue,
        p[PayloadKeys.Text].StringValue);
}
