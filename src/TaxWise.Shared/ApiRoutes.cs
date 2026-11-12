namespace TaxWise.Shared;

/// <summary>API paths shared by the Api (routing, links it hands out) and the App (calls).</summary>
public static class ApiRoutes
{
    public const string Ask = "api/ask";
    public const string KnowledgeBase = "api/knowledge-base";
    public const string ExplainForm = "api/forms/explain";
    public const string Calculator = "api/calculator";
    public const string CompareYears = "api/calculator/compare";

    /// <summary>A sample form and its boxes, e.g. <c>api/forms/w2</c>.</summary>
    public static string Form(string formId) => $"api/forms/{Uri.EscapeDataString(formId)}";

    /// <summary>Relative URL of a stored source PDF. The UI appends <c>#page=N</c>.</summary>
    public static string SourcePdf(string docId) => $"api/sources/{Uri.EscapeDataString(docId)}/pdf";
}
