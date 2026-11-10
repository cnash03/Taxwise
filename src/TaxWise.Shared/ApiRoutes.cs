namespace TaxWise.Shared;

/// <summary>API paths shared by the Api (routing, links it hands out) and the App (calls).</summary>
public static class ApiRoutes
{
    public const string Ask = "api/ask";
    public const string KnowledgeBase = "api/knowledge-base";

    /// <summary>Relative URL of a stored source PDF. The UI appends <c>#page=N</c>.</summary>
    public static string SourcePdf(string docId) => $"api/sources/{Uri.EscapeDataString(docId)}/pdf";
}
