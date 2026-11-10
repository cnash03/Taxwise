namespace TaxWise.Shared;

/// <summary>Body of <c>GET /health</c>. The App shows a green dot when <see cref="Status"/> is "ok".</summary>
public sealed record HealthResponse(string Status, string Version);
