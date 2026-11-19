using System.Text.Json;
using TaxWise.Shared;
using TaxWise.Shared.Display;

namespace TaxWise.Core.Tests.Display;

/// <summary>The jurisdiction contract shared by the API and the App (DECISIONS #23).</summary>
public sealed class JurisdictionTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(null, "US")]
    [InlineData("", "US")]
    [InlineData("  ", "US")]
    [InlineData("us", "US")]
    [InlineData("CO", "CO")]
    [InlineData(" co ", "CO")]
    [InlineData("TX", null)]
    public void Normalize(string? input, string? expected) => Assert.Equal(expected, Jurisdictions.Normalize(input));

    [Fact]
    public void AskRequestJson_WithoutJurisdiction_IsFederal()
    {
        // What an App built before Colorado was added sends.
        var request = JsonSerializer.Deserialize<AskRequest>("""{"question":"q","plainLanguage":false,"taxYear":2025}""", Web)!;

        Assert.Equal(Jurisdictions.Federal, request.Jurisdiction);
    }

    [Fact]
    public void AskRequestJson_CarriesColorado()
    {
        var json = JsonSerializer.Serialize(new AskRequest("q", false, 2025, Jurisdictions.Colorado), Web);

        Assert.Contains("\"jurisdiction\":\"CO\"", json);
        Assert.Equal(Jurisdictions.Colorado, JsonSerializer.Deserialize<AskRequest>(json, Web)!.Jurisdiction);
    }

    [Fact]
    public void SourcePassageJson_WithoutJurisdiction_IsFederal()
    {
        var source = JsonSerializer.Deserialize<SourcePassage>(
            """{"number":1,"chunkId":"c","docId":"p970","docTitle":"Pub 970","section":"S","page":5,"taxYear":2025,"text":"t","pdfUrl":"u"}""", Web)!;

        Assert.Equal(Jurisdictions.Federal, source.Jurisdiction);
    }

    private static SourcePassage CoSource() =>
        new(1, "co-dr0104-ty2025-p4-001", "co-dr0104", "DR 0104 Book, 2025 Colorado Individual Income Tax Filing Guide", null,
            "Who Must File This Tax Return", 4, 2025, "text", "http://x/pdf", Jurisdictions.Colorado);

    [Fact]
    public void Chip_NamesTheColoradoDor_AndKeepsTheTaxYear()
    {
        Assert.Equal("1 · Colorado DOR · DR 0104 Book · p. 4 · TY 2025", SourceLabels.Chip(CoSource()));
        Assert.Equal("Citation 1: Colorado DOR, DR 0104 Book, page 4, tax year 2025. Show the passage.",
            SourceLabels.ChipAriaLabel(CoSource()));
        Assert.Equal("Colorado DOR", SourceLabels.Publisher(CoSource()));
    }

    [Fact]
    public void FederalChip_HasNoPublisher() =>
        Assert.Null(SourceLabels.Publisher(CoSource() with { Jurisdiction = Jurisdictions.Federal }));
}
