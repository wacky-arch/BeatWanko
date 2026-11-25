using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using SongDetailsCache;
using SongDetailsCache.Structs;

namespace BeatWanko;

internal static class SongDetailsImpl
{
	private struct MatchingDifficulty
	{
		public int Rank;

		public string CharacteristicName;

		public float Stars;
	}

	private static readonly Random Rng = new Random();

	public static Task<SongDetails> InitTask;

	public static bool DataReady
	{
		get
		{
			Task<SongDetails> initTask = InitTask;
			if (initTask != null && initTask.IsCompleted && !initTask.IsFaulted)
			{
				return !initTask.IsCanceled;
			}
			return false;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void KickInit()
	{
		InitTask = SongDetails.Init();
		InitTask.ContinueWith(delegate(Task<SongDetails> t)
		{
			if (t.IsFaulted)
			{
				Plugin.Log?.Warn("[BeatWanko] SongDetailsCache init failed: " + t.Exception?.GetBaseException().Message);
			}
			else
			{
				Plugin.Log?.Info("[BeatWanko] SongDetailsCache data is ready.");
			}
		});
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static List<DrawCandidate> CollectCandidates(PluginConfig config, HashSet<string> excludeHashesUpper)
	{
		return CollectCandidates(config, excludeHashesUpper, ChallengeMode.Normal, 0);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static List<DrawCandidate> CollectCandidates(PluginConfig config, HashSet<string> excludeHashesUpper, ChallengeMode mode, int stepBand)
	{
		List<DrawCandidate> list = new List<DrawCandidate>();
		SongDetails songDetails = InitTask?.Result;
		if (songDetails == null)
		{
			return list;
		}
		if (mode == ChallengeMode.StepUp && (stepBand < 1 || stepBand > ChallengeModeExtensions.StepUpMaxStep))
		{
			return list;
		}
		int minDiff = 0;
		int maxDiff = 4;
		int minLen = 0;
		int maxLen = 0;
		if (mode == ChallengeMode.Normal && config != null)
		{
			int d1 = Math.Max(0, Math.Min(4, config.MinDifficulty));
			int d2 = Math.Max(0, Math.Min(4, config.MaxDifficulty));
			minDiff = Math.Min(d1, d2);
			maxDiff = Math.Max(d1, d2);
			NormalizePositiveRange(config.MinLengthSeconds, config.MaxLengthSeconds, out minLen, out maxLen);
		}
		bool strictDifficulty = mode == ChallengeMode.Normal || mode == ChallengeMode.StepUp || mode == ChallengeMode.Endurance;
		List<MatchingDifficulty> matching = new List<MatchingDifficulty>();
		foreach (Song song in songDetails.songs)
		{
			try
			{
				string hashUpper = song.hash?.ToUpperInvariant();
				if (string.IsNullOrEmpty(hashUpper) || (excludeHashesUpper != null && excludeHashesUpper.Contains(hashUpper)))
				{
					continue;
				}
				uint songDurationSeconds = song.songDurationSeconds;
				if (mode == ChallengeMode.Normal && config != null)
				{
					if ((minLen > 0 && songDurationSeconds < (uint)minLen) || (maxLen > 0 && songDurationSeconds > (uint)maxLen))
					{
						continue;
					}
				}
				matching.Clear();
				foreach (SongDifficulty difficulty in song.difficulties)
				{
					if (DifficultyMatches(difficulty, songDurationSeconds, config, mode, stepBand, minDiff, maxDiff))
					{
						string charName = CharacteristicSerializedName(difficulty.characteristic);
						if (charName != null)
						{
							matching.Add(new MatchingDifficulty
							{
								Rank = (int)difficulty.difficulty,
								CharacteristicName = charName,
								Stars = difficulty.stars
							});
						}
					}
				}
				if (matching.Count != 0)
				{
					MatchingDifficulty picked = matching[Rng.Next(matching.Count)];
					list.Add(new DrawCandidate
					{
						HashUpper = hashUpper,
						LevelId = "custom_level_" + hashUpper,
						DisplayName = song.songName + " / " + song.songAuthorName,
						DifficultyRank = picked.Rank,
						CharacteristicName = picked.CharacteristicName,
						Stars = picked.Stars,
						StepBand = ((mode == ChallengeMode.StepUp) ? stepBand : 0),
						StrictDifficulty = strictDifficulty
					});
				}
			}
			catch
			{
			}
		}
		return list;
	}

	private static bool DifficultyMatches(SongDifficulty diff, uint durationSeconds, PluginConfig config, ChallengeMode mode, int stepBand, int minDiff, int maxDiff)
	{
		MapMods mapMods = MapMods.NoodleExtensions | MapMods.MappingExtensions;
		if ((diff.mods & mapMods) != 0)
		{
			return false;
		}
		if (diff.notes == 0)
		{
			return false;
		}
		switch (mode)
		{
		case ChallengeMode.Unconditional:
			return true;
		case ChallengeMode.StepUp:
		{
			if ((diff.song.rankedStates & RankedStates.ScoresaberRanked) == 0 || diff.stars <= 0f)
			{
				return false;
			}
			float minStars = ((stepBand == 1) ? 0f : ((float)stepBand));
			return diff.stars >= minStars && diff.stars < (float)(stepBand + 1);
		}
		case ChallengeMode.Endurance:
			if (durationSeconds == 0)
			{
				return false;
			}
			return ((float)diff.notes / (float)durationSeconds) <= ChallengeModeExtensions.EnduranceMaxNps;
		default:
		{
			int difficulty = (int)diff.difficulty;
			if (difficulty < minDiff || difficulty > maxDiff)
			{
				return false;
			}
			if (config == null)
			{
				return true;
			}
			float nps = ((durationSeconds != 0) ? ((float)diff.notes / (float)durationSeconds) : 0f);
			NormalizePositiveRange(config.MinNps, config.MaxNps, out float minNps, out float maxNps);
			if (minNps > 0f && nps < minNps)
			{
				return false;
			}
			if (maxNps > 0f && nps > maxNps)
			{
				return false;
			}
			NormalizePositiveRange(config.MinStars, config.MaxStars, out float minStars, out float maxStars);
			if (minStars > 0f || maxStars > 0f)
			{
				if ((diff.song.rankedStates & RankedStates.ScoresaberRanked) == 0 || diff.stars <= 0f)
				{
					return false;
				}
				if (minStars > 0f && diff.stars < minStars)
				{
					return false;
				}
				if (maxStars > 0f && diff.stars > maxStars)
				{
					return false;
				}
			}
			return true;
		}
		}
	}

	private static void NormalizePositiveRange(float a, float b, out float min, out float max)
	{
		if (a > 0f && b > 0f && a > b)
		{
			min = b;
			max = a;
		}
		else
		{
			min = a;
			max = b;
		}
	}

	private static void NormalizePositiveRange(int a, int b, out int min, out int max)
	{
		if (a > 0 && b > 0 && a > b)
		{
			min = b;
			max = a;
		}
		else
		{
			min = a;
			max = b;
		}
	}

	private static string CharacteristicSerializedName(MapCharacteristic characteristic)
	{
		return characteristic switch
		{
			MapCharacteristic.Standard => "Standard",
			MapCharacteristic.OneSaber => "OneSaber",
			MapCharacteristic.NoArrows => "NoArrows",
			MapCharacteristic.NinetyDegree => "90Degree",
			MapCharacteristic.ThreeSixtyDegree => "360Degree",
			MapCharacteristic.Lightshow => "Lightshow",
			MapCharacteristic.Lawless => "Lawless",
			_ => null,
		};
	}
}
