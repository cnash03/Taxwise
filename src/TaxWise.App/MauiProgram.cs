using System.Reflection;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Media;
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
			.UseMauiCommunityToolkitCore()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		// appsettings.Release.json is only embedded in Release builds and overrides ApiBaseUrl with the deployed API.
		// appsettings.local.json (gitignored, any build) overrides both, e.g. to run a Debug build against Azure.
		foreach (var name in new[] { "TaxWise.App.appsettings.json", "TaxWise.App.appsettings.Release.json", "TaxWise.App.appsettings.local.json" })
		{
			using var settings = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
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
		// On-device only (DECISIONS #22): OfflineSpeechToText, not SpeechToText, which may send audio to Apple's servers.
		builder.Services.AddSingleton(OfflineSpeechToText.Default);
		builder.Services.AddSingleton<IVoiceInput, VoiceInput>();

		builder.Services.AddMauiBlazorWebView();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
