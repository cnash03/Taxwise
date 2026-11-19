using Microsoft.Extensions.Logging;
using TaxWise.Core;
using TaxWise.Core.Guardrails;
using TaxWise.Shared;

namespace TaxWise.Rag.Answering;

/// <summary>
/// The cited Q&amp;A pipeline (BUILD_PLAN §5.1): retrieve → refuse below the rerank threshold <em>without</em> calling
/// Claude → answer → validate citations → map P-numbers to chunks. PdfUrls in the response are relative
/// (<see cref="ApiRoutes.SourcePdf"/>); the API makes them absolute.
/// </summary>
public sealed class AskService(IRetriever retriever, IAnswerer answerer, ILogger<AskService> logger)
{
    public async Task<AskResponse> AskAsync(AskRequest request, CancellationToken ct = default)
    {
        // A missing or null jurisdiction is federal; the API turns unknown codes away before this point.
        var jurisdiction = Jurisdictions.Normalize(request.Jurisdiction)
                           ?? throw new ArgumentException($"Unknown jurisdiction '{request.Jurisdiction}'.", nameof(request));
        request = request with { Jurisdiction = jurisdiction };

        var retrieval = await retriever.RetrieveAsync(request.Question, request.TaxYear, jurisdiction: jurisdiction, ct: ct);
        if (!retrieval.MeetsThreshold)
        {
            logger.LogInformation(
                "endpoint=ask outcome=refusal reason=below_threshold topScore={TopScore:F4} searched={Searched} taxYear={TaxYear} " +
                "jurisdiction={Jurisdiction} claudeCalled=false",
                retrieval.TopScore, retrieval.Searched, request.TaxYear, jurisdiction);
            return AskResponses.NotFound(request.TaxYear, retrieval.Searched, retrieval.Passages.Count, jurisdiction);
        }

        var chunks = retrieval.Passages.Select(p => p.Chunk).ToList();
        var result = await answerer.AnswerAsync(request.Question, chunks, request.TaxYear, request.PlainLanguage, jurisdiction, ct);
        var call = result.Call;

        if (result.ModelRefused || result.Draft is null)
        {
            LogCall(call, "model_refusal", request, retrieval.TopScore);
            return AskResponses.CantHelp(retrieval.Searched, chunks.Count, jurisdiction);
        }

        var validated = CitationValidator.Validate(result.Draft, chunks.Select(c => c.Text).ToList());
        LogCall(call, validated.Stats.Outcome, request, retrieval.TopScore, validated.Stats);
        if (validated.IsRefusal)
            return AskResponses.NotFound(request.TaxYear, retrieval.Searched, chunks.Count, jurisdiction);

        var paragraphs = validated.Paragraphs
            .Select(p => new AnswerParagraph(p.Text,
                p.Citations.Select(c => new CitationRef(c.PassageNumber, chunks[c.PassageNumber - 1].Id, c.Highlight)).ToList()))
            .ToList();
        var sources = chunks.Select((c, i) => ToSource(i + 1, c)).ToList();

        return new AskResponse(AskResponse.AnswerKind, paragraphs, sources, retrieval.Searched, chunks.Count,
            validated.SuggestedFollowUps, AskResponses.Disclaimer, []);
    }

    public static SourcePassage ToSource(int number, Chunk c) =>
        new(number, c.Id, c.DocId, c.DocTitle, c.Chapter, c.Section, c.PageStart, c.TaxYear, c.Text, ApiRoutes.SourcePdf(c.DocId),
            c.Jurisdiction);

    // One line per Claude call: endpoint, latency, tokens, stop reason, validator outcome (BUILD_PLAN §10). No question text.
    private void LogCall(ModelCallInfo call, string outcome, AskRequest request, double topScore, ValidationStats? stats = null) =>
        logger.LogInformation(
            "endpoint=ask model={Model} latencyMs={LatencyMs} inputTokens={InputTokens} outputTokens={OutputTokens} " +
            "cacheReadTokens={CacheReadTokens} stopReason={StopReason} validator={Outcome} citationsRemoved={Removed} " +
            "highlightsMissing={Missing} paragraphsDropped={Dropped} topScore={TopScore:F4} taxYear={TaxYear} " +
            "jurisdiction={Jurisdiction} plain={Plain}",
            call.Model, call.LatencyMs, call.InputTokens, call.OutputTokens, call.CacheReadTokens, call.StopReason, outcome,
            stats?.CitationsRemoved ?? 0, stats?.HighlightsMissing ?? 0, stats?.ParagraphsDropped ?? 0,
            topScore, request.TaxYear, request.Jurisdiction, request.PlainLanguage);
}

/// <summary>Fixed user-facing texts for answers and refusals. Colorado refusals name the DOR and link to Colorado help.</summary>
public static class AskResponses
{
    public const string Disclaimer = "Educational tool, not professional tax advice.";

    public static IReadOnlyList<HelpLink> HelpLinks { get; } =
    [
        new("IRS.gov", "https://www.irs.gov/"),
        new("IRS Free File", "https://www.irs.gov/filing/free-file-do-your-federal-taxes-for-free"),
        new("Find a free VITA clinic", "https://irs.treasury.gov/freetaxprep/"),
    ];

    /// <summary>Revenue Online is the DOR's free filing system (DR 0104 Book p. 4).</summary>
    public static IReadOnlyList<HelpLink> ColoradoHelpLinks { get; } =
    [
        new("Tax.Colorado.gov", "https://tax.colorado.gov/individual-income-tax"),
        new("Colorado Revenue Online", "https://www.colorado.gov/revenueonline/"),
        new("Find a free VITA clinic", "https://irs.treasury.gov/freetaxprep/"),
    ];

    public static IReadOnlyList<HelpLink> LinksFor(string? jurisdiction) =>
        Jurisdictions.IsColorado(jurisdiction) ? ColoradoHelpLinks : HelpLinks;

    public static string NotFoundText(int taxYear, string? jurisdiction = Jurisdictions.Federal) =>
        Jurisdictions.IsColorado(jurisdiction)
            ? $"I don't know. I couldn't find this in the Colorado Department of Revenue documents I have for tax year {taxYear}. " +
              "Try Tax.Colorado.gov, Colorado Revenue Online, or a free VITA clinic."
            : $"I don't know. I couldn't find this in the IRS publications I have for tax year {taxYear}. " +
              "Try IRS.gov, IRS Free File, or a free VITA clinic.";

    public const string CantHelpText =
        "I can't help with that one. Try IRS.gov, IRS Free File, or a free VITA clinic.";

    public const string ColoradoCantHelpText =
        "I can't help with that one. Try Tax.Colorado.gov, Colorado Revenue Online, or a free VITA clinic.";

    /// <summary>The sources didn't cover the question (low rerank score, insufficient_sources, or every citation failed).</summary>
    public static AskResponse NotFound(int taxYear, int searched, int kept, string? jurisdiction = Jurisdictions.Federal) =>
        Refusal(NotFoundText(taxYear, jurisdiction), searched, kept, jurisdiction);

    /// <summary>Claude stopped with stop_reason "refusal".</summary>
    public static AskResponse CantHelp(int searched, int kept, string? jurisdiction = Jurisdictions.Federal) =>
        Refusal(Jurisdictions.IsColorado(jurisdiction) ? ColoradoCantHelpText : CantHelpText, searched, kept, jurisdiction);

    private static AskResponse Refusal(string text, int searched, int kept, string? jurisdiction) =>
        new(AskResponse.RefusalKind, [new AnswerParagraph(text, [])], [], searched, kept, [], Disclaimer, LinksFor(jurisdiction));
}
