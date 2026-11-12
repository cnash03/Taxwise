using System.Text.Json;
using Microsoft.Extensions.Options;
using TaxWise.Rag.Sources;
using TaxWise.Shared;

namespace TaxWise.Rag.Forms;

/// <summary>
/// "Forms" config section. <see cref="Directory"/> holds the <c>*-sample.json</c> files; it defaults to the repo's
/// data/forms, and must be set when the API runs outside the repo (Azure).
/// </summary>
public sealed class FormOptions
{
    public const string SectionName = "Forms";

    public string? Directory { get; set; }
}

/// <summary>The human-reviewed sample forms (BUILD_PLAN §6), loaded once at startup.</summary>
public sealed class FormLibrary
{
    private readonly Dictionary<string, FormSample> _byId;

    public FormLibrary(IOptions<FormOptions> options)
        : this(string.IsNullOrWhiteSpace(options.Value.Directory) ? RepoPaths.Forms : options.Value.Directory)
    {
    }

    public FormLibrary(string directory)
    {
        Forms = Directory.EnumerateFiles(directory, "*-sample.json")
            .Order(StringComparer.Ordinal)
            .Select(Load)
            .ToList();
        _byId = Forms.ToDictionary(f => f.FormId, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<FormSample> Forms { get; }

    public FormSample? Find(string formId) => _byId.GetValueOrDefault(formId);

    public static FormBox? FindBox(FormSample form, string boxId) =>
        form.Boxes.FirstOrDefault(b => string.Equals(b.Id, boxId, StringComparison.OrdinalIgnoreCase));

    private static FormSample Load(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<FormSample>(stream, JsonSerializerOptions.Web)
               ?? throw new InvalidOperationException($"{path} is empty.");
    }
}
