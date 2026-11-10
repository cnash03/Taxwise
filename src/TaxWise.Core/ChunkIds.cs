using System.Security.Cryptography;
using System.Text;

namespace TaxWise.Core;

public static class ChunkIds
{
    /// <summary>
    /// Deterministic GUID for a chunk id, used as the Qdrant point id so re-running ingestion
    /// overwrites points instead of duplicating them (BUILD_PLAN §4.2 step 7).
    /// </summary>
    public static Guid ToPointId(string chunkId)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(chunkId), hash);
        var bytes = hash[..16].ToArray();
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50); // RFC 4122 version 5-style
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC 4122 variant
        return new Guid(bytes, bigEndian: true);
    }
}
