using Microsoft.Extensions.Options;
using TaxWise.Api;
using TaxWise.Core.Calculator;
using TaxWise.Rag;
using TaxWise.Rag.Answering;
using TaxWise.Rag.Calculator;
using TaxWise.Rag.Forms;
using TaxWise.Rag.Sources;
using TaxWise.Rag.Voyage;
using TaxWise.Shared;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<TaxYearOptions>(builder.Configuration.GetSection(TaxYearOptions.SectionName));
builder.Services.Configure<SourceOptions>(builder.Configuration.GetSection(SourceOptions.SectionName));
builder.Services.AddTaxWiseRag(builder.Configuration);
builder.Services.AddTaxWiseAnswering(builder.Configuration);
builder.Services.AddSingleton<SourceLibrary>();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.MapGet("/health", () => new HealthResponse("ok", typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0"));

app.MapPost("/" + ApiRoutes.Ask, async (AskRequest? request, AskService ask, IOptions<TaxYearOptions> years,
    HttpContext http, ILogger<Program> logger, CancellationToken ct) =>
{
    if (request is null || string.IsNullOrWhiteSpace(request.Question))
        return Results.Problem("Ask a question.", statusCode: StatusCodes.Status400BadRequest);
    if (request.Question.Length > AskLimits.MaxQuestionLength)
        return Results.Problem($"Questions can be at most {AskLimits.MaxQuestionLength} characters.", statusCode: StatusCodes.Status400BadRequest);
    if (!years.Value.Supported.Contains(request.TaxYear))
        return Results.Problem($"Tax year {request.TaxYear} isn't supported. Choose {string.Join(", ", years.Value.Supported)}.",
            statusCode: StatusCodes.Status400BadRequest);

    return await Guarded(logger, async () =>
    {
        var response = await ask.AskAsync(request, ct);
        return Results.Ok(response with { Sources = AbsoluteSources(http, response.Sources) });
    });
});

app.MapGet("/api/forms/{formId}", (string formId, FormLibrary forms) =>
    forms.Find(formId) is { } form ? Results.Ok(form) : Results.NotFound());

app.MapPost("/" + ApiRoutes.ExplainForm, async (ExplainFormRequest? request, FormLibrary forms, FormExplainService explain,
    HttpContext http, ILogger<Program> logger, CancellationToken ct) =>
{
    if (request is null || string.IsNullOrWhiteSpace(request.Form) || string.IsNullOrWhiteSpace(request.BoxId))
        return Results.Problem("Choose a form and a box.", statusCode: StatusCodes.Status400BadRequest);
    if (forms.Find(request.Form) is not { } form)
        return Results.Problem($"There's no sample form called '{request.Form}'.", statusCode: StatusCodes.Status404NotFound);
    if (FormLibrary.FindBox(form, request.BoxId) is not { } box)
        return Results.Problem($"Form {form.Form} has no box '{request.BoxId}'.", statusCode: StatusCodes.Status404NotFound);

    return await Guarded(logger, async () =>
    {
        var response = await explain.ExplainAsync(form, box, request.PlainLanguage, ct);
        return Results.Ok(response with { Sources = AbsoluteSources(http, response.Sources) });
    });
});

app.MapPost("/" + ApiRoutes.Calculator, async (CalcRequest? request, CalculatorService calculator,
    HttpContext http, ILogger<Program> logger, CancellationToken ct) =>
{
    if (CalcProblem(request) is { } problem) return problem;
    if (TaxYearRegistry.Get(request!.TaxYear) is not { } rules)
        return Results.Problem($"Tax year {request.TaxYear} isn't supported. Choose {string.Join(", ", TaxYearRegistry.Supported)}.",
            statusCode: StatusCodes.Status400BadRequest);

    return await Guarded(logger, async () =>
    {
        var response = await calculator.CalculateAsync(rules, request, ct);
        return Results.Ok(response with { Sources = AbsoluteSources(http, response.Sources) });
    });
});

// Numbers only, for every supported year; TaxYear in the body is ignored.
app.MapPost("/" + ApiRoutes.CompareYears, (CalcRequest? request) =>
    CalcProblem(request) ?? Results.Ok(CalculatorService.Compare(request!)));

app.MapGet("/api/sources/{docId}/pdf", (string docId, SourceLibrary library) =>
    // Only manifest doc IDs resolve to a file, so the route can't be used to read anything else.
    library.FindPdf(docId) is { } path
        ? Results.File(path, "application/pdf", enableRangeProcessing: true)
        : Results.NotFound());

app.MapGet("/" + ApiRoutes.KnowledgeBase, (SourceLibrary library, IOptions<TaxYearOptions> years, HttpContext http) =>
    new KnowledgeBaseResponse(
        library.Documents
            .Select(d => new KnowledgeBaseDocument(d.DocId, d.Title, d.Kind, d.TaxYears, Absolute(http, ApiRoutes.SourcePdf(d.DocId))))
            .ToList(),
        years.Value.Supported,
        years.Value.FullKnowledgeBase));

app.Run();

// Maps answerer and retrieval failures to safe ProblemDetails (429 / 502), shared by /api/ask, /api/forms/explain and /api/calculator.
static async Task<IResult> Guarded(ILogger logger, Func<Task<IResult>> run)
{
    try
    {
        return await run();
    }
    catch (AnswererException e) when (e.Failure == AnswererFailure.RateLimited)
    {
        return Results.Problem(e.Message, statusCode: StatusCodes.Status429TooManyRequests);
    }
    catch (AnswererException e)
    {
        return Results.Problem(e.Message, statusCode: StatusCodes.Status502BadGateway);
    }
    catch (Exception e) when (e is VoyageApiException or Grpc.Core.RpcException)
    {
        logger.LogError("Retrieval failed: {Type}: {Message}", e.GetType().Name, e.Message);
        return Results.Problem("The search service is unavailable. Please try again.", statusCode: StatusCodes.Status502BadGateway);
    }
}

// Shared input checks for the calculator endpoints; null when the request is fine.
static IResult? CalcProblem(CalcRequest? request)
{
    if (request is null)
        return Results.Problem("Enter your wages and withholding.", statusCode: StatusCodes.Status400BadRequest);
    if (!Enum.IsDefined(request.FilingStatus))
        return Results.Problem("Choose a filing status.", statusCode: StatusCodes.Status400BadRequest);
    if (request.Wages is < 0 or > CalcLimits.MaxAmount || request.FederalWithholding is < 0 or > CalcLimits.MaxAmount)
        return Results.Problem($"Wages and withholding must be between $0 and ${CalcLimits.MaxAmount:N0}.",
            statusCode: StatusCodes.Status400BadRequest);
    return null;
}

static List<SourcePassage> AbsoluteSources(HttpContext http, IEnumerable<SourcePassage> sources) =>
    sources.Select(s => s with { PdfUrl = Absolute(http, s.PdfUrl) }).ToList();

static string Absolute(HttpContext http, string relative) =>
    $"{http.Request.Scheme}://{http.Request.Host}{http.Request.PathBase}/{relative}";

// Exposed so TaxWise.Api.Tests can use WebApplicationFactory<Program>.
public partial class Program;
