using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TaxWise.Shared;

namespace TaxWise.App.Services;

/// <summary>A failed API call, with a message that's safe to show the student.</summary>
public sealed class ApiException(string message, bool apiUnreachable) : Exception(message)
{
	/// <summary>True when the API couldn't be reached at all (down, wrong URL, timeout).</summary>
	public bool ApiUnreachable { get; } = apiUnreachable;
}

/// <summary>Calls the TaxWise API (ask, knowledge base, forms, calculator). API keys live only in the API (BUILD_PLAN §1).</summary>
public sealed class TaxWiseApiClient(HttpClient http)
{
	public async Task<AskResponse> AskAsync(AskRequest request, CancellationToken cancellationToken)
	{
		using var response = await SendAsync(() => http.PostAsJsonAsync(ApiRoutes.Ask, request, cancellationToken), cancellationToken);
		return await ReadAsync<AskResponse>(response, cancellationToken);
	}

	public async Task<KnowledgeBaseResponse> GetKnowledgeBaseAsync(CancellationToken cancellationToken)
	{
		using var response = await SendAsync(() => http.GetAsync(ApiRoutes.KnowledgeBase, cancellationToken), cancellationToken);
		return await ReadAsync<KnowledgeBaseResponse>(response, cancellationToken);
	}

	public async Task<FormSample> GetFormAsync(string formId, CancellationToken cancellationToken)
	{
		using var response = await SendAsync(() => http.GetAsync(ApiRoutes.Form(formId), cancellationToken), cancellationToken);
		return await ReadAsync<FormSample>(response, cancellationToken);
	}

	public async Task<ExplainFormResponse> ExplainFormAsync(ExplainFormRequest request, CancellationToken cancellationToken)
	{
		using var response = await SendAsync(() => http.PostAsJsonAsync(ApiRoutes.ExplainForm, request, cancellationToken), cancellationToken);
		return await ReadAsync<ExplainFormResponse>(response, cancellationToken);
	}

	public async Task<CalcResponse> CalculateAsync(CalcRequest request, CancellationToken cancellationToken)
	{
		using var response = await SendAsync(() => http.PostAsJsonAsync(ApiRoutes.Calculator, request, cancellationToken), cancellationToken);
		return await ReadAsync<CalcResponse>(response, cancellationToken);
	}

	public async Task<CompareYearsResponse> CompareYearsAsync(CalcRequest request, CancellationToken cancellationToken)
	{
		using var response = await SendAsync(() => http.PostAsJsonAsync(ApiRoutes.CompareYears, request, cancellationToken), cancellationToken);
		return await ReadAsync<CompareYearsResponse>(response, cancellationToken);
	}

	private static async Task<HttpResponseMessage> SendAsync(Func<Task<HttpResponseMessage>> send, CancellationToken cancellationToken)
	{
		try
		{
			return await send();
		}
		catch (HttpRequestException e)
		{
			DebugLog.Write("send failed", e);
			throw new ApiException("Can't reach the TaxWise API. Make sure it's running, then try again.", apiUnreachable: true);
		}
		catch (TaskCanceledException e) when (!cancellationToken.IsCancellationRequested)
		{
			DebugLog.Write("send timed out", e);
			throw new ApiException("The TaxWise API took too long to answer. Please try again.", apiUnreachable: true);
		}
	}

	private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
	{
		if (!response.IsSuccessStatusCode)
		{
			var message = await ProblemDetailAsync(response, cancellationToken);
			DebugLog.Write($"{(int)response.StatusCode} from {response.RequestMessage?.RequestUri}: {message}");
			throw new ApiException(message, apiUnreachable: false);
		}
		try
		{
			return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
				?? throw new ApiException("The TaxWise API sent an empty response.", apiUnreachable: false);
		}
		catch (JsonException e)
		{
			DebugLog.Write($"unreadable {typeof(T).Name} from {response.RequestMessage?.RequestUri}", e);
			throw new ApiException("The TaxWise API sent a response the app couldn't read.", apiUnreachable: false);
		}
	}

	/// <summary>The API returns ProblemDetails with a student-safe <c>detail</c>; fall back to a generic message.</summary>
	private static async Task<string> ProblemDetailAsync(HttpResponseMessage response, CancellationToken cancellationToken)
	{
		try
		{
			using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
			if (doc.RootElement.ValueKind == JsonValueKind.Object
			    && doc.RootElement.TryGetProperty("detail", out var detail)
			    && detail.GetString() is { Length: > 0 } text)
				return text;
		}
		catch (JsonException)
		{
		}
		return response.StatusCode == HttpStatusCode.TooManyRequests
			? "TaxWise is busy right now. Please wait a moment and try again."
			: $"Something went wrong on the TaxWise API ({(int)response.StatusCode}). Please try again.";
	}
}
