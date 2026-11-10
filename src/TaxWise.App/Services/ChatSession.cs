using TaxWise.Shared;
using TaxWise.Shared.Display;

namespace TaxWise.App.Services;

public enum TurnStatus
{
	Loading,
	Done,
	Failed,
}

/// <summary>One question and what came back. The settings are captured when it was asked.</summary>
public sealed class ChatTurn(string question, bool plainLanguage, int taxYear)
{
	public string Question { get; } = question;
	public bool PlainLanguage { get; } = plainLanguage;
	public int TaxYear { get; } = taxYear;
	public TurnStatus Status { get; set; } = TurnStatus.Loading;
	public AskResponse? Response { get; set; }
	public string? Error { get; set; }
	public bool ApiUnreachable { get; set; }

	public bool IsRefusal => Response?.Kind == AskResponse.RefusalKind;
}

/// <summary>
/// The current conversation. A singleton so it survives switching pages; the in-flight request itself is
/// cancelled when the Ask page goes away (BUILD_PLAN §10).
/// </summary>
public sealed class ChatSession
{
	private const int TitleLength = 60;

	public List<ChatTurn> Turns { get; } = [];

	public string Title => Turns.Count == 0 ? "New conversation" : SourceLabels.Truncate(Turns[0].Question, TitleLength);

	/// <summary>Every chunk cited in this chat, oldest first; "Quiz me" passes these to quizzes.</summary>
	public IReadOnlyList<string> CitedChunkIds => Turns
		.Where(t => t is { Status: TurnStatus.Done, IsRefusal: false })
		.SelectMany(t => t.Response!.Paragraphs.SelectMany(p => p.Citations).Select(c => c.ChunkId))
		.Distinct()
		.ToList();

	public void Clear() => Turns.Clear();
}
