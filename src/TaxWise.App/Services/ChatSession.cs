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

	/// <summary>The section of the first cited passage, e.g. "Scholarships and Fellowship Grants"; null for refusals.</summary>
	public string? QuizTopic
	{
		get
		{
			if (Status != TurnStatus.Done || IsRefusal || Response is not { } r) return null;
			var first = r.Paragraphs.SelectMany(p => p.Citations).FirstOrDefault();
			return first is null ? null : r.Sources.FirstOrDefault(s => s.Number == first.Number)?.Section;
		}
	}

	/// <summary>Every chunk this answer cited, in citation order; saved under <see cref="QuizTopic"/> for quizzes.</summary>
	public IReadOnlyList<string> CitedChunkIds => IsRefusal || Response is null
		? []
		: Response.Paragraphs.SelectMany(p => p.Citations).Select(c => c.ChunkId).Distinct().ToList();
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

	public void Clear() => Turns.Clear();
}
