namespace TaxWise.Shared;

/// <summary>Body of <c>GET /api/knowledge-base</c>, for the nav footer and the tax-year banner (BUILD_PLAN §1a).</summary>
public sealed record KnowledgeBaseResponse(
    IReadOnlyList<KnowledgeBaseDocument> Documents,
    IReadOnlyList<int> SupportedTaxYears,
    IReadOnlyList<int> FullKnowledgeBaseYears);

/// <summary>One ingested IRS document. <see cref="TaxYears"/> lists every year it has chunks for.</summary>
public sealed record KnowledgeBaseDocument(string DocId, string Title, string Kind, IReadOnlyList<int> TaxYears, string PdfUrl);
