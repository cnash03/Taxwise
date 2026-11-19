using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxWise.Core;
using TaxWise.Core.Guardrails;
using TaxWise.Rag.Answering;
using TaxWise.Shared;

namespace TaxWise.Rag.Tests.Answering;

/// <summary>Colorado chat (DECISIONS #23): the jurisdiction reaches retrieval and the prompt, and refusals name the DOR.</summary>
public sealed class ColoradoAskTests
{
    private static Chunk CoChunk(int i, string text = "") =>
        new($"co-dr0104-ty2025-p{i}-{i:D3}", "co-dr0104", "DR 0104 Book, 2025 Colorado Individual Income Tax Filing Guide", 2025,
            null, "Who Must File This Tax Return", i, i, text.Length > 0 ? text : $"Colorado passage {i}. It has two sentences.",
            Jurisdiction: Jurisdictions.Colorado);

    private static AskService MakeService(IRetriever retriever, IAnswerer answerer) =>
        new(retriever, answerer, NullLogger<AskService>.Instance);

    [Fact]
    public void AskRequest_DefaultsToFederal()
    {
        Assert.Equal(Jurisdictions.Federal, new AskRequest("q", false, 2025).Jurisdiction);
    }

    [Fact]
    public async Task Retriever_PassesTheJurisdictionToTheStore_FederalByDefault()
    {
        var store = new FakeStore([CoChunk(4)]);
        var retriever = new Retriever(new FakeVoyage(0.9), store,
            Options.Create(new RetrievalOptions { VectorTopK = 8, RerankTopK = 4, MinRerankScore = 0.35 }));

        await retriever.RetrieveAsync("q", 2025);
        await retriever.RetrieveAsync("q", 2025, jurisdiction: Jurisdictions.Colorado);

        Assert.Equal([Jurisdictions.Federal, Jurisdictions.Colorado], store.SearchedJurisdictions);
    }

    [Fact]
    public async Task Ask_Colorado_RetrievesAndAnswersAsColorado_AndSourcesCarryTheJurisdiction()
    {
        var retriever = new FakeRetriever(new RetrievalResult(8, [new RetrievedPassage(CoChunk(4, "You must file a Colorado return. More."), 0.8)], 0.8, true));
        var answerer = new FakeAnswerer(new DraftAnswer(DraftAnswer.Answered, [new("Yes.", [new("P1", "You must file a Colorado return.")])]));

        var response = await MakeService(retriever, answerer)
            .AskAsync(new AskRequest("Do I file in Colorado?", false, 2025, "co"));

        Assert.Equal(AskResponse.AnswerKind, response.Kind);
        Assert.Equal(Jurisdictions.Colorado, retriever.LastJurisdiction);
        Assert.Equal(Jurisdictions.Colorado, answerer.LastJurisdiction);
        Assert.Equal(Jurisdictions.Colorado, Assert.Single(response.Sources).Jurisdiction);
    }

    [Fact]
    public async Task Ask_NullJurisdiction_IsFederal()
    {
        var retriever = new FakeRetriever(new RetrievalResult(0, [], 0, false));

        var response = await MakeService(retriever, new FakeAnswerer(null)).AskAsync(new AskRequest("q", false, 2025, null));

        Assert.Equal(Jurisdictions.Federal, retriever.LastJurisdiction);
        Assert.Equal(AskResponses.NotFoundText(2025), Assert.Single(response.Paragraphs).Text);
        Assert.Equal(AskResponses.HelpLinks, response.Links);
    }

    [Fact]
    public async Task Ask_ColoradoBelowThreshold_RefusesWithDorTextAndLinks_WithoutCallingClaude()
    {
        var answerer = new FakeAnswerer(null);
        var retriever = new FakeRetriever(new RetrievalResult(8, [new RetrievedPassage(CoChunk(4), 0.1)], 0.1, false));

        var response = await MakeService(retriever, answerer).AskAsync(new AskRequest("How do I stake crypto?", false, 2025, Jurisdictions.Colorado));

        Assert.Equal(AskResponse.RefusalKind, response.Kind);
        Assert.Equal(0, answerer.Calls);
        Assert.Contains("Colorado Department of Revenue documents I have for tax year 2025", Assert.Single(response.Paragraphs).Text);
        Assert.Equal(AskResponses.ColoradoHelpLinks, response.Links);
        Assert.Equal(AskResponses.Disclaimer, response.Disclaimer);
    }

    [Fact]
    public async Task Ask_ColoradoModelRefusal_UsesColoradoText()
    {
        var retriever = new FakeRetriever(new RetrievalResult(8, [new RetrievedPassage(CoChunk(4), 0.9)], 0.9, true));

        var response = await MakeService(retriever, new FakeAnswerer(null, refuse: true))
            .AskAsync(new AskRequest("q", false, 2025, Jurisdictions.Colorado));

        Assert.Equal(AskResponses.ColoradoCantHelpText, Assert.Single(response.Paragraphs).Text);
    }

    [Fact]
    public async Task Ask_UnknownJurisdiction_Throws()
    {
        var service = MakeService(new FakeRetriever(new RetrievalResult(0, [], 0, false)), new FakeAnswerer(null));

        await Assert.ThrowsAsync<ArgumentException>(() => service.AskAsync(new AskRequest("q", false, 2025, "TX")));
    }

    [Fact]
    public void FederalSystemPrompt_IsUnchangedFromBeforeColorado()
    {
        // Byte-for-byte the pre-Colorado template, so the federal prompt cache and eval results still apply.
        const string expected = """
            You are TaxWise, an educational assistant that helps U.S. college students understand
            their federal taxes for tax year 2025.

            You will receive up to four passages from official IRS documents, labeled P1-P4.
            Answer the student's question using ONLY those passages. Do not use outside knowledge,
            even if you are confident. If the passages do not contain enough information to answer,
            return status "insufficient_sources" with an empty paragraphs array.

            Each paragraph must be supported by at least one citation. For each citation, copy one
            sentence verbatim from the passage into "quote" so the app can highlight it.

            Do not calculate tax amounts. If the student asks "how much will I owe", explain the
            relevant rule and suggest the Scenario calculator.

            Write clearly and concisely for a college student. Use the passage's terminology.
            """;

        Assert.Equal(expected, AnswerPrompt.SystemPrompt(2025, plainLanguage: false));
        Assert.Equal(expected, AnswerPrompt.SystemPrompt(2025, plainLanguage: false, Jurisdictions.Federal));
    }

    [Fact]
    public void ColoradoSystemPrompt_NamesTheDor_AndDoesNotSuggestTheFederalCalculator()
    {
        var prompt = AnswerPrompt.SystemPrompt(2025, plainLanguage: false, Jurisdictions.Colorado);

        Assert.Contains("understand\ntheir Colorado state income taxes for tax year 2025.", prompt);
        Assert.Contains("official Colorado Department of Revenue documents", prompt);
        Assert.DoesNotContain("IRS", prompt);
        Assert.DoesNotContain("suggest the Scenario calculator", prompt);
        Assert.DoesNotContain("{", prompt);
        Assert.Equal(prompt, AnswerPrompt.SystemPrompt(2025, plainLanguage: false, Jurisdictions.Colorado));
    }

    [Fact]
    public void SourceLabel_PrefixesColoradoPassagesWithTheDor()
    {
        Assert.Equal("Colorado DOR · DR 0104 Book · p. 4 · TY 2025", AnswerPrompt.SourceLabel(CoChunk(4)));
        Assert.Equal("Pub 970 · Ch. 1 · p. 7 · TY 2025", AnswerPrompt.SourceLabel(TestChunks.Make(7)));
    }
}
