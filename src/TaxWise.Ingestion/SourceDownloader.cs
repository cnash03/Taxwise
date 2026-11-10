using System.Security.Cryptography;

namespace TaxWise.Ingestion;

/// <summary>Downloads manifest PDFs to data/sources/pdf/ and tracks their sha256 (BUILD_PLAN §4.2 step 1).</summary>
public sealed class SourceDownloader(HttpClient http)
{
    public enum Outcome { Present, Downloaded, Missing, HashMismatch }

    /// <summary>
    /// Ensures the PDF is on disk. A file whose hash matches the manifest is reused. An empty manifest
    /// hash is filled in. A different hash means the IRS posted a new edition: the caller must stop.
    /// </summary>
    public async Task<(Outcome Outcome, string? ActualSha)> EnsureAsync(ManifestEntry entry, bool download, CancellationToken ct)
    {
        Directory.CreateDirectory(RepoPaths.PdfDir);
        var path = Path.Combine(RepoPaths.PdfDir, entry.FileName);

        if (File.Exists(path))
        {
            var sha = await HashAsync(path, ct);
            if (entry.Sha256.Length == 0 || entry.Sha256 == sha) return (Outcome.Present, sha);
            if (!download) return (Outcome.HashMismatch, sha);
        }
        else if (!download) return (Outcome.Missing, null);

        var tmp = path + ".part";
        using (var response = await http.GetAsync(entry.Url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            await using var file = File.Create(tmp);
            await response.Content.CopyToAsync(file, ct);
        }
        var downloadedSha = await HashAsync(tmp, ct);
        File.Move(tmp, path, overwrite: true);
        return entry.Sha256.Length == 0 || entry.Sha256 == downloadedSha
            ? (Outcome.Downloaded, downloadedSha)
            : (Outcome.HashMismatch, downloadedSha);
    }

    private static async Task<string> HashAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct));
    }
}
