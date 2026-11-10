namespace TaxWise.Core.Chunking;

/// <summary>Chunk sizing (BUILD_PLAN §4.2 step 4).</summary>
public sealed record ChunkingOptions
{
    public int MinTokens { get; init; } = 350;
    public int MaxTokens { get; init; } = 700;
    public int OverlapTokens { get; init; } = 60;

    public static ChunkingOptions Default { get; } = new();
}
