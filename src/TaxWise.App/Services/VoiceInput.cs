using System.Globalization;
using CommunityToolkit.Maui.Media;
using TaxWise.Shared.Display;

namespace TaxWise.App.Services;

/// <summary>Why listening couldn't start or stopped early. The page shows <see cref="VoiceDraft.ErrorMessage"/>.</summary>
public sealed class VoiceInputException(VoiceError error, Exception? inner = null)
	: Exception(VoiceDraft.ErrorMessage(error), inner)
{
	public VoiceError Error { get; } = error;
}

/// <summary>Speech to text for the Ask page's mic button (BUILD_PLAN §11).</summary>
public interface IVoiceInput
{
	/// <summary>False where the OS has no speech recognizer we support; the mic button is hidden.</summary>
	bool IsSupported { get; }

	/// <summary>
	/// Asks for microphone and speech permission, then listens until the student pauses or <see cref="StopAsync"/> is
	/// called. <paramref name="onHeard"/> gets the whole transcript so far each time it grows, on a background thread.
	/// Returns the final transcript (empty if nothing was heard). Cancelling the token stops listening and throws.
	/// </summary>
	/// <exception cref="VoiceInputException">Permission denied, no recognizer, or recognition failed.</exception>
	Task<string> ListenAsync(Action<string> onHeard, CancellationToken cancellationToken);

	/// <summary>Ends listening early; the pending <see cref="ListenAsync"/> returns what was heard.</summary>
	Task StopAsync();
}

/// <summary>
/// On-device recognition through CommunityToolkit.Maui's <see cref="OfflineSpeechToText"/> (DECISIONS #21): Apple's
/// Speech framework with <c>RequiresOnDeviceRecognition</c> on the Mac, System.Speech dictation on Windows. No audio
/// or text leaves the computer, and neither is ever logged.
/// </summary>
/// <remarks>
/// A singleton, and the event handlers are instance methods: the toolkit holds its subscribers through weak
/// references, so handlers on a short-lived closure could be collected mid-sentence.
/// </remarks>
public sealed class VoiceInput(ISpeechToText speech) : IVoiceInput
{
	/// <summary>The IRS sources and prompts are English, so the recognizer is too.</summary>
	private static readonly CultureInfo Culture = new("en-US");

	/// <summary>How long a pause ends listening.</summary>
	private static readonly TimeSpan SilenceTimeout = TimeSpan.FromSeconds(4);

	/// <summary>Listening never runs longer than this, even if the recognizer keeps hearing noise.</summary>
	private static readonly TimeSpan MaxListen = TimeSpan.FromSeconds(60);

	/// <summary>How long a stop waits for the recognizer's final result before using what was already heard.</summary>
	private static readonly TimeSpan StopGrace = TimeSpan.FromSeconds(2);

	private TaskCompletionSource<string>? _done;
	private Action<string>? _onHeard;
	private string _heard = "";

	public bool IsSupported => OperatingSystem.IsMacCatalyst() || OperatingSystem.IsWindows();

	public async Task<string> ListenAsync(Action<string> onHeard, CancellationToken cancellationToken)
	{
		if (!IsSupported) throw new VoiceInputException(VoiceError.NotAvailable);
		if (_done is not null) throw new InvalidOperationException("Already listening.");

		var done = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
		_done = done;
		_onHeard = onHeard;
		_heard = "";
		try
		{
			await RequestPermissionsAsync(cancellationToken);

			speech.RecognitionResultUpdated += OnUpdated;
			speech.RecognitionResultCompleted += OnCompleted;
			speech.StateChanged += OnStateChanged;
			try
			{
				await speech.StartListenAsync(new SpeechToTextOptions
				{
					Culture = Culture,
					ShouldReportPartialResults = true,
					AutoStopSilenceTimeout = SilenceTimeout,
				}, cancellationToken);
			}
			catch (Exception e) when (e is not OperationCanceledException)
			{
				// Mac: no on-device English model (Dictation off). Windows: no desktop speech recognizer installed.
				throw new VoiceInputException(VoiceError.NotAvailable, e);
			}

			await using var stop = cancellationToken.Register(() => _ = speech.StopListenAsync(CancellationToken.None));
			using var cap = new CancellationTokenSource(MaxListen);
			await using var capStop = cap.Token.Register(() => _ = StopAsync());
			return await done.Task.WaitAsync(cancellationToken);
		}
		finally
		{
			speech.RecognitionResultUpdated -= OnUpdated;
			speech.RecognitionResultCompleted -= OnCompleted;
			speech.StateChanged -= OnStateChanged;
			_done = null;
			_onHeard = null;
		}
	}

	public async Task StopAsync()
	{
		if (_done is not { } done) return;
		await speech.StopListenAsync(CancellationToken.None);
		// Windows doesn't report a result after a stop, and Apple's final result can take a moment.
		await FinishSoonAsync(done);
	}

	private void OnUpdated(object? sender, SpeechToTextRecognitionResultUpdatedEventArgs e)
	{
#if WINDOWS
		// System.Speech reports each recognized phrase on its own.
		_heard = _heard.Length == 0 ? e.RecognitionResult : $"{_heard} {e.RecognitionResult}";
#else
		// Apple's recognizer reports the whole transcript so far.
		_heard = e.RecognitionResult;
#endif
		_onHeard?.Invoke(_heard);
	}

	private void OnCompleted(object? sender, SpeechToTextRecognitionResultCompletedEventArgs e)
	{
		if (_done is not { } done) return;
		var result = e.RecognitionResult;
		if (result.IsSuccessful)
		{
#if WINDOWS
			// The toolkit joins Windows phrases without spaces; ours has them.
			done.TrySetResult(_heard);
#else
			done.TrySetResult(string.IsNullOrWhiteSpace(result.Text) ? _heard : result.Text);
#endif
		}
		else if (_heard.Length > 0 || result.Exception is TaskCanceledException)
		{
			// Stopped by hand, or Apple reports "no speech" after a stop: keep what was heard.
			done.TrySetResult(_heard);
		}
		else
		{
			done.TrySetException(new VoiceInputException(VoiceError.NothingHeard, result.Exception));
		}
	}

	private void OnStateChanged(object? sender, SpeechToTextStateChangedEventArgs e)
	{
		// The recognizer can stop (silence timeout) without reporting a result; don't wait forever for one.
		if (e.State == SpeechToTextState.Stopped && _done is { } done) _ = FinishSoonAsync(done);
	}

	private async Task FinishSoonAsync(TaskCompletionSource<string> done)
	{
		await Task.WhenAny(done.Task, Task.Delay(StopGrace));
		done.TrySetResult(_heard);
	}

	private async Task RequestPermissionsAsync(CancellationToken cancellationToken)
	{
		var microphone = await MainThread.InvokeOnMainThreadAsync(Permissions.RequestAsync<Permissions.Microphone>)
			.WaitAsync(cancellationToken);
		if (microphone != PermissionStatus.Granted)
			throw new VoiceInputException(VoiceError.PermissionDenied);

		// On the Mac this is the Speech Recognition prompt; on Windows it asks for the microphone again (a no-op).
		if (!await speech.RequestPermissions(cancellationToken))
			throw new VoiceInputException(VoiceError.PermissionDenied);
	}
}
