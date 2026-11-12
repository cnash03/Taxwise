using TaxWise.Shared.Display;

namespace TaxWise.Core.Tests.Display;

public sealed class GlossaryMarkerTests
{
    private static readonly GlossaryTerm Deduction = Term("deduction", "deductions");
    private static readonly GlossaryTerm StandardDeduction = Term("standard deduction");
    private static readonly GlossaryTerm Agi = Term("adjusted gross income", "AGI");
    private static readonly GlossaryTerm EarnedIncome = Term("earned income");
    private static readonly GlossaryTerm Eic = Term("earned income credit", "EIC");
    private static readonly GlossaryTerm Refundable = Term("refundable credit", "refundable");

    private static GlossaryTerm Term(string term, params string[] aliases) =>
        new(term, aliases, $"Definition of {term}.", "p17-ty2025-p1-001", Reviewed: false);

    private static string Render(IEnumerable<TextSegment> segments) =>
        string.Concat(segments.Select(s => s.Term is null ? s.Text : $"[{s.Text}]"));

    [Fact]
    public void MarksOnlyTheFirstMatch_CaseInsensitive()
    {
        var marker = new GlossaryMarker([Agi]);
        var segments = marker.Mark("Your agi is your AGI.", new HashSet<GlossaryTerm>());

        Assert.Equal("Your [agi] is your AGI.", Render(segments));
        Assert.Same(Agi, segments.Single(s => s.Term is not null).Term);
    }

    [Fact]
    public void MatchesWholeWordsOnly()
    {
        var marker = new GlossaryMarker([Refundable, EarnedIncome]);

        Assert.Equal("A nonrefundable credit. Unearned income.",
            Render(marker.Mark("A nonrefundable credit. Unearned income.", new HashSet<GlossaryTerm>())));
    }

    [Fact]
    public void LongerTermWinsOverlap_AndShorterTermCanStillMatchLater()
    {
        var marker = new GlossaryMarker([Deduction, StandardDeduction]);

        Assert.Equal("The [standard deduction] is one [deduction].",
            Render(marker.Mark("The standard deduction is one deduction.", new HashSet<GlossaryTerm>())));
    }

    [Fact]
    public void LongerAliasBeatsShorterTermAtSamePosition()
    {
        var marker = new GlossaryMarker([EarnedIncome, Eic]);

        Assert.Equal("The [earned income credit] needs [earned income].",
            Render(marker.Mark("The earned income credit needs earned income.", new HashSet<GlossaryTerm>())));
    }

    [Fact]
    public void TermsMarkedInEarlierParagraphAreSkipped()
    {
        var marker = new GlossaryMarker([Agi, Deduction]);
        var used = new HashSet<GlossaryTerm>();

        Assert.Equal("[AGI] first.", Render(marker.Mark("AGI first.", used)));
        Assert.Equal("AGI again, then [deductions].", Render(marker.Mark("AGI again, then deductions.", used)));
        Assert.Equal(2, used.Count);
    }

    [Fact]
    public void SegmentsRebuildTheOriginalText()
    {
        const string text = "Scholarship money for tuition is tax free; AGI and standard deduction matter.";
        var marker = new GlossaryMarker([Agi, StandardDeduction, Deduction]);

        Assert.Equal(text, string.Concat(marker.Mark(text, new HashSet<GlossaryTerm>()).Select(s => s.Text)));
    }
}
