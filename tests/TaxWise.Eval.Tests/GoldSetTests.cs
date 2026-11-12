namespace TaxWise.Eval.Tests;

public class GoldSetTests
{
    [Fact]
    public void Parses_lines_with_defaults()
    {
        var items = GoldSet.Parse(
        [
            """{"id":"q01","persona":"scholarship","inScope":true,"question":"Q?","expectedDocs":["p970"],"expectedPages":[6,7]}""",
            "",
            """{"id":"y01","persona":"year-specific","inScope":true,"taxYear":2027,"question":"Q?","expectedDocs":["rp-26-35"],"expectedPages":[13],"goldAnswer":"$16,600","goldStatus":"approved"}""",
        ]);

        Assert.Equal(2, items.Count);
        Assert.Equal(2025, items[0].TaxYear);
        Assert.False(items[0].HasGoldAnswer);
        Assert.Equal(GoldItem.Pending, items[0].GoldStatus);
        Assert.Equal(2027, items[1].TaxYear);
        Assert.True(items[1].HasGoldAnswer);
    }

    [Fact]
    public void A_drafted_answer_is_not_judgeable_until_approved()
    {
        var item = new GoldItem("q01", "p", true, "Q?", ["p970"], [6], "Drafted.", GoldItem.Draft);
        Assert.False(item.HasGoldAnswer);
        Assert.True((item with { GoldStatus = GoldItem.Approved }).HasGoldAnswer);
    }

    [Fact]
    public void Rejects_duplicates_and_in_scope_items_without_docs()
    {
        const string line = """{"id":"q01","persona":"p","inScope":true,"question":"Q?","expectedDocs":["p970"],"expectedPages":[]}""";
        Assert.Throws<FormatException>(() => GoldSet.Parse([line, line]));
        Assert.Throws<FormatException>(() => GoldSet.Parse(
            ["""{"id":"q02","persona":"p","inScope":true,"question":"Q?","expectedDocs":[],"expectedPages":[]}"""]));
        Assert.Throws<FormatException>(() => GoldSet.Parse(["{not json"]));
    }

    [Fact]
    public void The_committed_gold_set_parses_and_has_the_planned_mix()
    {
        var gold = GoldSet.Load(GoldSet.DefaultPath);

        Assert.InRange(gold.Count, 45, 60);
        Assert.InRange(gold.Count(g => g.InScope), 35, 45);
        Assert.InRange(gold.Count(g => g.Persona == "out-of-scope"), 8, 12);
        Assert.Equal(2, gold.Count(g => g.Persona == "year-trap"));
        Assert.InRange(gold.Count(g => g.Persona == "year-specific"), 5, 8);
        foreach (var persona in new[] { "w2-first-timer", "scholarship", "dependent", "gig", "international" })
            Assert.True(gold.Count(g => g.Persona == persona) >= 5, persona);
    }
}
