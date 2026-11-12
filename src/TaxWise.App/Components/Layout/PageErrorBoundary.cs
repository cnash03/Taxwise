using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using TaxWise.App.Services;

namespace TaxWise.App.Components.Layout;

/// <summary>
/// Catches a page's render or event exceptions so the app shows an error card instead of the "Reload" bar. The exception
/// is logged; Debug builds also write it to <c>last-error.txt</c> in the app's cache folder, since a Mac Catalyst app
/// launched with <c>dotnet build -t:Run</c> has no visible console.
/// </summary>
public sealed class PageErrorBoundary : ErrorBoundary
{
    [Inject] private ILogger<PageErrorBoundary> Logger { get; set; } = default!;

    protected override Task OnErrorAsync(Exception exception)
    {
        Logger.LogError(exception, "Unhandled UI exception");
        DebugLog.Write("unhandled UI exception", exception);
        return Task.CompletedTask;
    }
}
