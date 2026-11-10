using TaxWise.Shared;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/health", () => new HealthResponse("ok", typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0"));

app.Run();

// Exposed so TaxWise.Api.Tests can use WebApplicationFactory<Program>.
public partial class Program;
