using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaxWise.Eval;

/// <summary>
/// One line of <c>eval/gold.jsonl</c> (BUILD_PLAN §8). <see cref="ExpectedPages"/> are PDF page numbers, the same
/// numbering as chunk pages and the <c>#page=N</c> links. <see cref="GoldAnswer"/> is written by a human; until then it
/// is empty and the question isn't judged.
/// </summary>
public sealed record GoldItem(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("persona")] string Persona,
    [property: JsonPropertyName("inScope")] bool InScope,
    [property: JsonPropertyName("question")] string Question,
    [property: JsonPropertyName("expectedDocs")] IReadOnlyList<string> ExpectedDocs,
    [property: JsonPropertyName("expectedPages")] IReadOnlyList<int> ExpectedPages,
    [property: JsonPropertyName("goldAnswer")] string GoldAnswer = "",
    [property: JsonPropertyName("goldStatus")] string GoldStatus = GoldItem.Pending,
    [property: JsonPropertyName("taxYear")] int TaxYear = GoldItem.DefaultTaxYear,
    [property: JsonPropertyName("sourceHint")] string? SourceHint = null)
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const int DefaultTaxYear = 2025;

    public bool HasGoldAnswer => !string.IsNullOrWhiteSpace(GoldAnswer);
}

public static class GoldSet
{
    public static string DefaultPath => Path.Combine(Rag.Sources.RepoPaths.Root, "eval", "gold.jsonl");

    public static IReadOnlyList<GoldItem> Load(string path) => Parse(File.ReadLines(path));

    /// <summary>Parses JSON lines (blank lines skipped) and checks ids are unique and in-scope items name a doc.</summary>
    public static IReadOnlyList<GoldItem> Parse(IEnumerable<string> lines)
    {
        var items = new List<GoldItem>();
        var lineNo = 0;
        foreach (var line in lines)
        {
            lineNo++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            GoldItem item;
            try
            {
                item = JsonSerializer.Deserialize<GoldItem>(line) ?? throw new JsonException("null");
            }
            catch (JsonException e)
            {
                throw new FormatException($"gold.jsonl line {lineNo}: {e.Message}", e);
            }
            if (string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Question))
                throw new FormatException($"gold.jsonl line {lineNo}: id and question are required.");
            if (item.InScope && item.ExpectedDocs.Count == 0)
                throw new FormatException($"gold.jsonl line {lineNo} ({item.Id}): an in-scope question needs expectedDocs.");
            items.Add(item with { ExpectedDocs = item.ExpectedDocs ?? [], ExpectedPages = item.ExpectedPages ?? [] });
        }

        var duplicate = items.GroupBy(i => i.Id).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null) throw new FormatException($"gold.jsonl: duplicate id {duplicate.Key}.");
        return items;
    }
}
