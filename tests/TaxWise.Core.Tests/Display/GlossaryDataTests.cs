using System.Text.Json;
using System.Text.RegularExpressions;
using TaxWise.Shared.Display;

namespace TaxWise.Core.Tests.Display;

/// <summary>Sanity checks on <c>data/glossary.json</c>, which the App embeds (BUILD_PLAN §5.2).</summary>
public sealed partial class GlossaryDataTests
{
    private static readonly GlossaryFile Glossary = Load();

    private static GlossaryFile Load()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaxWise.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var json = File.ReadAllText(Path.Combine(dir.FullName, "data", "glossary.json"));
        return JsonSerializer.Deserialize<GlossaryFile>(json, JsonSerializerOptions.Web)!;
    }

    [Fact]
    public void HasAboutFortyTerms() => Assert.InRange(Glossary.Terms.Count, 35, 60);

    [Fact]
    public void EveryTermHasADefinitionAndSourceChunkFromItsTaxYear() =>
        Assert.All(Glossary.Terms, t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Definition), t.Term);
            Assert.Matches(ChunkIdPattern(), t.ChunkId);
            Assert.Contains($"-ty{Glossary.TaxYear}-", t.ChunkId);
        });

    [Fact]
    public void SpellingsAreUniqueAcrossTerms()
    {
        var duplicates = Glossary.Terms.SelectMany(t => t.Spellings)
            .GroupBy(s => s, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key);
        Assert.Empty(duplicates);
    }

    [GeneratedRegex(@"^[a-z0-9-]+-ty\d{4}-p\d+-\d{3}$")]
    private static partial Regex ChunkIdPattern();
}
