using System.Collections.Generic;

namespace BeatWanko;

public class SessionRecord
{
	public string Date;

	public string Mode;

	public string EndReason;

	public int Cups;

	public long TotalScore;

	public float TotalPlaySeconds;

	public int TotalNotes;

	public float AverageAccuracyPercent;

	public List<HistoryEntry> Entries = new List<HistoryEntry>();
}
