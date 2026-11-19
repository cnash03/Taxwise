using Qdrant.Client.Grpc;
using TaxWise.Core;
using TaxWise.Rag.VectorStore;
using TaxWise.Shared;

namespace TaxWise.Rag.Tests.VectorStore;

/// <summary>The Qdrant search filter and payload mapping for jurisdictions (DECISIONS #23).</summary>
public sealed class JurisdictionFilterTests
{
    private static Condition Find(Filter filter, Func<Condition, bool> match)
    {
        var found = filter.Must.Where(match).ToList();
        Assert.Single(found);
        return found[0];
    }

    [Fact]
    public void Federal_MatchesUsOrAMissingField_SoTheOldCollectionStillWorks()
    {
        var filter = QdrantChunkStore.SearchFilter(2025, Jurisdictions.Federal);

        Assert.Equal(2, filter.Must.Count);
        Assert.Equal(2025, Find(filter, c => c.Field?.Key == PayloadKeys.TaxYear).Field.Match.Integer);
        var either = Find(filter, c => c.ConditionOneOfCase == Condition.ConditionOneOfOneofCase.Filter).Filter;
        Assert.Equal(2, either.Should.Count);
        Assert.Contains(either.Should, c => c.Field?.Key == PayloadKeys.Jurisdiction && c.Field.Match.Keyword == "US");
        Assert.Contains(either.Should, c => c.IsEmpty?.Key == PayloadKeys.Jurisdiction);
        Assert.Empty(filter.Should);
    }

    [Fact]
    public void Colorado_MatchesCoOnly()
    {
        var filter = QdrantChunkStore.SearchFilter(2025, "co");

        var jurisdiction = Find(filter, c => c.Field?.Key == PayloadKeys.Jurisdiction);
        Assert.Equal("CO", jurisdiction.Field.Match.Keyword);
        Assert.DoesNotContain(filter.Must, c => c.ConditionOneOfCase == Condition.ConditionOneOfOneofCase.Filter);
    }

    [Fact]
    public void DocIds_AreStillAdded()
    {
        var filter = QdrantChunkStore.SearchFilter(2025, Jurisdictions.Federal, ["iw2", "i1040"]);

        var docs = Find(filter, c => c.Field?.Key == PayloadKeys.DocId);
        Assert.Equal(["iw2", "i1040"], docs.Field.Match.Keywords.Strings);
    }

    [Fact]
    public void UnknownJurisdiction_Throws() =>
        Assert.Throws<ArgumentException>(() => QdrantChunkStore.SearchFilter(2025, "TX"));

    [Fact]
    public void ColoradoChunk_RoundTripsThroughThePayload()
    {
        var chunk = new Chunk("co-guide-ty2025-p3-001", "co-guide", "Income Tax Guide, Colorado Individual Income Tax Guide", 2025,
            null, "Domicile", 3, 3, "text", Jurisdiction: Jurisdictions.Colorado);

        var payload = ChunkPayload.ToPayload(chunk);

        Assert.Equal("CO", payload[PayloadKeys.Jurisdiction].StringValue);
        Assert.Equal(chunk, ChunkPayload.FromPayload(payload));
    }

    [Fact]
    public void FederalChunk_WritesUs_AndAPayloadWithoutTheField_ReadsAsFederal()
    {
        var chunk = new Chunk("p970-ty2025-p5-010", "p970", "Pub 970, Tax Benefits for Education", 2025, null, "Scholarships", 5, 5, "text");

        var payload = ChunkPayload.ToPayload(chunk);
        Assert.Equal("US", payload[PayloadKeys.Jurisdiction].StringValue);

        payload.Remove(PayloadKeys.Jurisdiction); // a point ingested before Colorado was added
        Assert.Equal(Jurisdictions.Federal, ChunkPayload.FromPayload(payload).Jurisdiction);
    }
}
