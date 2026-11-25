namespace BeatWanko;

public enum ChallengeMode
{
	Normal = 0,
	Unconditional = 1,
	StepUp = 2,
	Endurance = 3
}

internal static class ChallengeModeExtensions
{
	public const int StepUpMaxStep = 14;

	public const float EnduranceMaxNps = 8f;

	public static bool IsRanked(this ChallengeMode mode)
	{
		return mode != ChallengeMode.Normal;
	}

	public static bool IsPauseDisabled(this ChallengeMode mode)
	{
		return mode == ChallengeMode.Endurance;
	}

	public static string ToApiMode(this ChallengeMode mode)
	{
		return mode switch
		{
			ChallengeMode.Unconditional => "unconditional",
			ChallengeMode.StepUp => "stepup",
			ChallengeMode.Endurance => "endurance",
			_ => "normal"
		};
	}

	public static string DisplayName(this ChallengeMode mode)
	{
		return mode switch
		{
			ChallengeMode.Unconditional => "無条件☠️ランキング",
			ChallengeMode.StepUp => "SS⭐️ステップアップ",
			ChallengeMode.Endurance => "耐久💪ランキング",
			_ => "ノーマル"
		};
	}

	public static string ShortBadge(this ChallengeMode mode)
	{
		return mode switch
		{
			ChallengeMode.Unconditional => "無条件☠️",
			ChallengeMode.StepUp => "SS⭐️ステップアップ",
			ChallengeMode.Endurance => "耐久💪",
			_ => "ノーマル"
		};
	}
}
