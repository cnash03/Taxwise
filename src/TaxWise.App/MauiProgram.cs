using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TaxWise.App.Services;

namespace TaxWise.App;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		using (var settings = Assembly.GetExecutingAssembly().GetManifestResourceStream("TaxWise.App.appsettings.json"))
		{
			if (settings is not null)
				builder.Configuration.AddJsonStream(settings);
		}

		var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5080";
		builder.Services.AddHttpClient<ApiHealthClient>(client =>
		{
			client.BaseAddress = new Uri(apiBaseUrl);
			client.Timeout = TimeSpan.FromSeconds(5);
		});

		builder.Services.AddMauiBlazorWebView();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
