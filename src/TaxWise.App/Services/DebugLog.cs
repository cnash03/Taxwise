namespace TaxWise.App.Services;

/// <summary>
/// Debug builds only: appends app-side failures to <c>last-error.txt</c> in the app's cache folder, because a Mac
/// Catalyst app launched with <c>dotnet build -t:Run</c> has no visible console. Release builds write nothing.
/// </summary>
public static class DebugLog
{
	public static string FilePath => Path.Combine(FileSystem.CacheDirectory, "last-error.txt");

	public static void Write(string message, Exception? exception = null)
	{
#if DEBUG
		try
		{
			File.AppendAllText(FilePath, $"{DateTimeOffset.Now:O} {message}\n{exception}\n\n");
		}
		catch (IOException)
		{
		}
#endif
	}
}
