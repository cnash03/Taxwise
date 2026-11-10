using TaxWise.Core;
using TaxWise.Rag.VectorStore;

namespace TaxWise.Rag.Tests.VectorStore;

public sealed class ChunkPayloadTests
{
    [Fact]
    public void EveryChunkField_RoundTripsThroughThePayload()
    {
        var chunk = new Chunk("p970-ty2025-p5-010", "p970", "Pub 970, Tax Benefits for Education", 2025,
            "Chapter 1. Scholarships", "Tax-Free Scholarships", 5, 6, "A scholarship is tax free only if…");

        Assert.Equal(chunk, ChunkPayload.FromPayload(ChunkPayload.ToPayload(chunk)));
    }

    [Fact]
    public void NullChapter_RoundTrips()
    {
        var chunk = new Chunk("i1040-ty2025-p14-026", "i1040", "Form 1040 Instructions", 2025, null, "Line 1a", 14, 14, "text");

        var payload = ChunkPayload.ToPayload(chunk);

        Assert.False(payload.ContainsKey(PayloadKeys.Chapter));
        Assert.Equal(chunk, ChunkPayload.FromPayload(payload));
    }

    [Fact]
    public void MergedSections_RoundTrip()
    {
        var chunk = new Chunk("p970-ty2025-p5-010", "p970", "Pub 970, Tax Benefits for Education", 2025,
            "Chapter 1. Scholarships", "Tax-Free Scholarships", 5, 5, "text", ["Scholarships", "Tax-Free Scholarships"]);

        var roundTripped = ChunkPayload.FromPayload(ChunkPayload.ToPayload(chunk));

        Assert.Equal(chunk.Sections, roundTripped.Sections);
        Assert.Equal(chunk with { Sections = null }, roundTripped with { Sections = null });
    }
}
