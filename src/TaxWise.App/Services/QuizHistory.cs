using System.Text.Json;
using TaxWise.Shared;

namespace TaxWise.App.Services;

/// <summary>The IRS chunks cited in chat under one topic, most recently cited first.</summary>
public sealed record SavedQuizTopic(string Topic, int TaxYear, IReadOnlyList<string> ChunkIds, DateTimeOffset UpdatedAt);

/// <summary>
/// The chunk IDs cited in chat answers, grouped by topic and tax year, so quizzes can be built from them later
/// (BUILD_PLAN §7, Phase 6). Saved as JSON in <c>FileSystem.AppDataDirectory</c>, so topics survive restarts.
/// A topic keeps its <see cref="QuizLimits.MaxChunkIds"/> most recently cited chunks.
/// </summary>
public sealed class QuizHistory(IFileSystem fileSystem)
{
	private List<SavedQuizTopic>? _topics;

	private string FilePath => Path.Combine(fileSystem.AppDataDirectory, "quiz-history.json");

	/// <summary>Most recently updated first.</summary>
	public IReadOnlyList<SavedQuizTopic> Topics => Load().OrderByDescending(t => t.UpdatedAt).ToList();

	public SavedQuizTopic? Find(string topic, int taxYear) =>
		Load().FirstOrDefault(t => t.TaxYear == taxYear && string.Equals(t.Topic, topic, StringComparison.OrdinalIgnoreCase));

	/// <summary>Adds one answer's cited chunks to its topic. Nothing is saved when it cited nothing.</summary>
	public void Record(string topic, int taxYear, IReadOnlyList<string> chunkIds)
	{
		if (chunkIds.Count == 0) return;
		var topics = Load();
		var existing = Find(topic, taxYear);
		var ids = chunkIds.Concat(existing?.ChunkIds ?? []).Distinct().Take(QuizLimits.MaxChunkIds).ToList();
		if (existing is not null) topics.Remove(existing);
		topics.Add(new SavedQuizTopic(existing?.Topic ?? topic, taxYear, ids, DateTimeOffset.Now));
		Save(topics);
	}

	public void Remove(SavedQuizTopic topic)
	{
		if (Load().Remove(topic)) Save(_topics!);
	}

	private List<SavedQuizTopic> Load()
	{
		if (_topics is not null) return _topics;
		try
		{
			_topics = File.Exists(FilePath)
				? JsonSerializer.Deserialize<List<SavedQuizTopic>>(File.ReadAllText(FilePath)) ?? []
				: [];
		}
		catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
		{
			// A broken history file only costs the saved topics; chat keeps working.
			DebugLog.Write("couldn't read quiz history", e);
			_topics = [];
		}
		return _topics;
	}

	private void Save(List<SavedQuizTopic> topics)
	{
		try
		{
			File.WriteAllText(FilePath, JsonSerializer.Serialize(topics));
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
			DebugLog.Write("couldn't save quiz history", e);
		}
	}
}
