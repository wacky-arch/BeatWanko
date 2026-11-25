using System.Collections.Generic;

namespace BeatWanko;

internal sealed class SessionSummary
{
	public ChallengeMode Mode;

	public int Cups;

	public long TotalScore;

	public float TotalPlaySeconds;

	public int TotalNotes;

	public float AverageAccuracyPercent;

	public HistoryEntry BestEntry;

	public List<HistoryEntry> Entries = new List<HistoryEntry>();

	public string Notice;
}
