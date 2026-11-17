using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TaxWise.Rag.Answering;
using TaxWise.Rag.Forms;
using TaxWise.Shared;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace TaxWise.Api.Tests;

/// <summary>/api/forms/upload with a stub extractor in place of Claude, against the committed sample forms.</summary>
public sealed class FormUploadEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly string[] W2Text = ["Form W-2 Wage and Tax Statement 2025", "1 Wages, tips, other compensation 31250.40"];

    private readonly WebApplicationFactory<Program> _factory;
    private readonly StubExtractor _extractor = new();

    public FormUploadEndpointTests(WebApplicationFactory<Program> factory) =>
        _factory = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<IFormExtractor>(_extractor)));

    [Fact]
    public async Task Upload_ReturnsTheSampleShapeWithTheUploadedValues()
    {
        _extractor.Extraction = new FormExtraction("w2", 2025, "Acme Coffee LLC", [new("1", "31250.40"), new("2", "2100")]);
        using var client = _factory.CreateClient();

        var response = await client.PostAsync("/" + ApiRoutes.UploadForm, PdfContent(Pdf(W2Text)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var form = (await response.Content.ReadFromJsonAsync<FormSample>())!;
        Assert.Equal(("w2", "Acme Coffee LLC"), (form.FormId, form.Issuer));
        Assert.Equal(["$31,250.40", "$2,100.00"], form.Boxes.Select(b => b.Value));
        Assert.Equal("Form 1040, line 1a", form.Boxes[0].GoesTo);
    }

    [Fact]
    public async Task Upload_ScannedPdf_Is422WithAFriendlyMessage_AndNoClaudeCall()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync("/" + ApiRoutes.UploadForm, PdfContent(Pdf()));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("scan", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, _extractor.Calls);
    }

    [Fact]
    public async Task Upload_WrongContentTypeOrTooLarge_IsRejectedBeforeParsing()
    {
        using var client = _factory.CreateClient();

        var text = await client.PostAsync("/" + ApiRoutes.UploadForm, new StringContent("hello"));
        var large = await client.PostAsync("/" + ApiRoutes.UploadForm, PdfContent(new byte[FormUploadLimits.MaxBytes + 1]));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, text.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, large.StatusCode);
        Assert.Equal(0, _extractor.Calls);
    }

    [Fact]
    public async Task Upload_ExtractorFailure_MapsToStatus()
    {
        _extractor.Failure = AnswererFailure.RateLimited;
        using var client = _factory.CreateClient();

        var response = await client.PostAsync("/" + ApiRoutes.UploadForm, PdfContent(Pdf(W2Text)));

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    private static ByteArrayContent PdfContent(byte[] pdf)
    {
        var content = new ByteArrayContent(pdf);
        content.Headers.ContentType = new MediaTypeHeaderValue(FormUploadLimits.ContentType);
        return content;
    }

    private static byte[] Pdf(params string[] lines)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(PageSize.Letter);
        for (var i = 0; i < lines.Length; i++)
            page.AddText(lines[i], 10, new PdfPoint(40, 740 - i * 16), font);
        return builder.Build();
    }

    private sealed class StubExtractor : IFormExtractor
    {
        public FormExtraction? Extraction { get; set; }
        public AnswererFailure? Failure { get; set; }
        public int Calls { get; private set; }

        public Task<FormExtractorResult> ExtractAsync(string documentText, CancellationToken ct = default)
        {
            Calls++;
            if (Failure is { } f) throw new AnswererException(f, "safe message");
            return Task.FromResult(new FormExtractorResult(Extraction, false, new ModelCallInfo("stub", 1, 1, 1, 0, "end_turn")));
        }
    }
}
