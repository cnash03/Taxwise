using TaxWise.App.Services;
using TaxWise.Shared;

namespace TaxWise.App.Components.Chat;

/// <summary>The citation chip the student clicked; drives the Source passage panel.</summary>
public sealed record CitationSelection(ChatTurn Turn, CitationRef Citation, SourcePassage Source);
