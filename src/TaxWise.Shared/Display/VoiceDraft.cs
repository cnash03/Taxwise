namespace TaxWise.Shared.Display;

public enum VoiceStatus
{
    /// <summary>Speech recognition doesn't exist on this platform; the mic button is hidden.</summary>
    Unavailable,
    Idle,
    /// <summary>Asking for permission and starting the recognizer.</summary>
    Starting,
    Listening,
    Error,
}

public enum VoiceError
{
    PermissionDenied,
    /// <summary>The OS has no on-device recognizer for English (e.g. Dictation is off on the Mac).</summary>
    NotAvailable,
    NothingHeard,
    Failed,
}

/// <summary>
/// The mic button on the Ask page (voice input, BUILD_PLAN §11). Plain state so it can be tested without MAUI:
/// the page feeds it recognizer events and writes <see cref="Draft"/> back into the question box.
/// What was typed before the mic was pressed is kept, and the transcript is appended after it.
/// </summary>
public sealed class VoiceDraft(bool supported, int maxLength)
{
    private string _typed = "";

    public VoiceStatus Status { get; private set; } = supported ? VoiceStatus.Idle : VoiceStatus.Unavailable;
    public VoiceError? Error { get; private set; }

    /// <summary>The question box text: what was typed plus what has been heard so far, cut to <c>maxLength</c>.</summary>
    public string Draft { get; private set; } = "";

    public bool IsActive => Status is VoiceStatus.Starting or VoiceStatus.Listening;

    public string ButtonLabel => IsActive ? "Stop listening" : "Speak your question";

    public string? StatusText => Status switch
    {
        VoiceStatus.Starting => "Starting the microphone…",
        VoiceStatus.Listening => "Listening… pause or press the mic to finish.",
        VoiceStatus.Error => ErrorMessage(Error!.Value),
        _ => null,
    };

    /// <summary>Starts a session from the current question box text. False if one is running or speech is unavailable.</summary>
    public bool TryStart(string typed)
    {
        if (Status is VoiceStatus.Unavailable || IsActive) return false;
        _typed = typed.TrimEnd();
        Draft = typed;
        Error = null;
        Status = VoiceStatus.Starting;
        return true;
    }

    /// <summary>Text heard so far. Returns the new draft.</summary>
    public string Heard(string transcript)
    {
        if (!IsActive) return Draft;
        Status = VoiceStatus.Listening;
        Draft = Combine(transcript);
        return Draft;
    }

    /// <summary>The recognizer finished. An empty transcript is an error so the student knows nothing was heard.</summary>
    public string Completed(string transcript)
    {
        if (!IsActive) return Draft;
        if (string.IsNullOrWhiteSpace(transcript))
        {
            Draft = Combine("");
            Fail(VoiceError.NothingHeard);
            return Draft;
        }
        Draft = Combine(transcript);
        Status = VoiceStatus.Idle;
        return Draft;
    }

    public void Fail(VoiceError error)
    {
        if (Status is VoiceStatus.Unavailable) return;
        Error = error;
        Status = VoiceStatus.Error;
    }

    /// <summary>Abandons the session (question submitted, page left). The draft keeps what was heard.</summary>
    public void Cancel()
    {
        if (IsActive) Status = VoiceStatus.Idle;
    }

    /// <summary>Clears an error once the student types or presses the mic again.</summary>
    public void Dismiss()
    {
        if (Status is not VoiceStatus.Error) return;
        Error = null;
        Status = VoiceStatus.Idle;
    }

    public static string ErrorMessage(VoiceError error) => error switch
    {
        VoiceError.PermissionDenied =>
            "TaxWise can't use the microphone. Allow Microphone and Speech Recognition for TaxWise in your computer's privacy settings, then try again.",
        VoiceError.NotAvailable =>
            "Speech recognition isn't available on this computer. On a Mac, turn on Dictation in System Settings → Keyboard so the on-device English model downloads.",
        VoiceError.NothingHeard => "Didn't catch that. Try again, or type your question.",
        _ => "Voice input stopped unexpectedly. Try again, or type your question.",
    };

    private string Combine(string transcript)
    {
        transcript = transcript.Trim();
        var text = _typed.Length == 0 || transcript.Length == 0 ? _typed + transcript : $"{_typed} {transcript}";
        return text.Length <= maxLength ? text : text[..maxLength];
    }
}
