using TaxWise.Shared.Display;

namespace TaxWise.Core.Tests.Display;

public sealed class VoiceDraftTests
{
    private static VoiceDraft Started(string typed = "", int maxLength = 1000)
    {
        var voice = new VoiceDraft(supported: true, maxLength);
        Assert.True(voice.TryStart(typed));
        return voice;
    }

    [Fact]
    public void Unsupported_IsHiddenAndNeverStarts()
    {
        var voice = new VoiceDraft(supported: false, 1000);

        Assert.Equal(VoiceStatus.Unavailable, voice.Status);
        Assert.False(voice.TryStart(""));
        voice.Fail(VoiceError.Failed);
        Assert.Equal(VoiceStatus.Unavailable, voice.Status);
    }

    [Fact]
    public void Start_GoesToStartingAndFlipsTheButton()
    {
        var voice = new VoiceDraft(supported: true, 1000);
        Assert.Equal("Speak your question", voice.ButtonLabel);
        Assert.Null(voice.StatusText);

        Assert.True(voice.TryStart(""));

        Assert.Equal(VoiceStatus.Starting, voice.Status);
        Assert.True(voice.IsActive);
        Assert.Equal("Stop listening", voice.ButtonLabel);
        Assert.False(voice.TryStart(""));
    }

    [Fact]
    public void PartialText_FillsTheDraftWhileListening()
    {
        var voice = Started();

        Assert.Equal("is my", voice.Heard("is my"));
        Assert.Equal(VoiceStatus.Listening, voice.Status);
        Assert.Equal("is my scholarship taxable", voice.Heard("is my scholarship taxable"));
        Assert.StartsWith("Listening", voice.StatusText);
    }

    [Fact]
    public void Completed_KeepsTheTranscriptForTheStudentToSend()
    {
        var voice = Started();
        voice.Heard("is my scholarship");

        Assert.Equal("Is my scholarship taxable?", voice.Completed(" Is my scholarship taxable? "));
        Assert.Equal(VoiceStatus.Idle, voice.Status);
        Assert.False(voice.IsActive);
    }

    [Fact]
    public void TypedText_IsKeptAndTheTranscriptAppended()
    {
        var voice = Started("For tax year 2025, ");

        Assert.Equal("For tax year 2025, is my stipend taxable", voice.Heard("is my stipend taxable"));
        Assert.Equal("For tax year 2025, is my stipend taxable?", voice.Completed("is my stipend taxable?"));
    }

    [Fact]
    public void Draft_IsCutToTheQuestionLimit()
    {
        var voice = Started("abc", maxLength: 10);

        Assert.Equal("abc defghi", voice.Completed("defghijklmnop"));
    }

    [Fact]
    public void NothingHeard_IsAnErrorAndKeepsWhatWasTyped()
    {
        var voice = Started("my question");

        Assert.Equal("my question", voice.Completed("  "));
        Assert.Equal(VoiceStatus.Error, voice.Status);
        Assert.Equal(VoiceError.NothingHeard, voice.Error);
        Assert.Equal(VoiceDraft.ErrorMessage(VoiceError.NothingHeard), voice.StatusText);
    }

    [Fact]
    public void PermissionDenied_ShowsHowToFixIt()
    {
        var voice = Started();

        voice.Fail(VoiceError.PermissionDenied);

        Assert.Equal(VoiceStatus.Error, voice.Status);
        Assert.Contains("privacy settings", voice.StatusText);
        Assert.Equal("Speak your question", voice.ButtonLabel);
    }

    [Fact]
    public void Error_ClearsOnDismissAndAllowsAnotherTry()
    {
        var voice = Started();
        voice.Fail(VoiceError.NotAvailable);

        voice.Dismiss();

        Assert.Equal(VoiceStatus.Idle, voice.Status);
        Assert.Null(voice.Error);
        Assert.True(voice.TryStart(""));
    }

    [Fact]
    public void Cancel_StopsListeningAndIgnoresLateResults()
    {
        var voice = Started();
        voice.Heard("do I need to file");

        voice.Cancel();

        Assert.Equal(VoiceStatus.Idle, voice.Status);
        Assert.Equal("do I need to file", voice.Draft);
        // A result that arrives after the question was sent must not refill the box.
        Assert.Equal("do I need to file", voice.Heard("do I need to file a return"));
        Assert.Equal("do I need to file", voice.Completed("do I need to file a return"));
        Assert.Equal(VoiceStatus.Idle, voice.Status);
    }

    [Fact]
    public void Dismiss_DoesNothingWhileListening()
    {
        var voice = Started();
        voice.Heard("hello");

        voice.Dismiss();

        Assert.Equal(VoiceStatus.Listening, voice.Status);
    }
}
