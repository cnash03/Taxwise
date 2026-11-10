using System.Net.Http.Json;
using TaxWise.Shared;

namespace TaxWise.App.Services;

/// <summary>Calls the API's <c>GET /health</c>. Any failure (API down, timeout, bad body) counts as unhealthy.</summary>
public sealed class ApiHealthClient(HttpClient http)
{
	public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
	{
		try
		{
			var body = await http.GetFromJsonAsync<HealthResponse>("health", cancellationToken);
			return body?.Status == "ok";
		}
		catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
		{
			return false;
		}
	}
}
