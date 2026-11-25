namespace BeatWanko;

public class PluginConfig
{
	public static PluginConfig Instance { get; set; }

	public virtual float IntervalSeconds { get; set; } = 5f;

	public virtual float DownloadTimeoutSeconds { get; set; } = 15f;

	public virtual bool EnableRankingSubmission { get; set; } = true;

	public virtual string LeaderboardApiUrl { get; set; } = "https://beatwanko-api.wackimu-vr.workers.dev";

	public virtual string LeaderboardWebUrl { get; set; } = "https://beatwanko.pages.dev/";

	public virtual bool ContinueOnFail { get; set; }

	public virtual int MinDifficulty { get; set; }

	public virtual int MaxDifficulty { get; set; } = 4;

	public virtual float MinStars { get; set; }

	public virtual float MaxStars { get; set; }

	public virtual float MinNps { get; set; }

	public virtual float MaxNps { get; set; }

	public virtual int MinLengthSeconds { get; set; }

	public virtual int MaxLengthSeconds { get; set; }
}
