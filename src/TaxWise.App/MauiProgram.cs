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
		builder.Services.AddHttpClient<TaxWiseApiClient>(client =>
		{
			client.BaseAddress = new Uri(apiBaseUrl);
			// Retrieval + a Claude call with thinking can take a while.
			client.Timeout = TimeSpan.FromSeconds(120);
		});

		builder.Services.AddSingleton(Preferences.Default);
		builder.Services.AddSingleton(Launcher.Default);
		builder.Services.AddSingleton<UserSettings>();
		builder.Services.AddSingleton<GlossaryService>();
		builder.Services.AddSingleton<ChatSession>();
		builder.Services.AddSingleton(FileSystem.Current);
		builder.Services.AddSingleton<QuizHistory>();

		builder.Services.AddMauiBlazorWebView();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
