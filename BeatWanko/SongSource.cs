using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BeatWanko;

internal static class SongSource
{
	private static readonly System.Random Rng = new System.Random();

	public static DrawCandidate Draw(HashSet<string> excludeHashesUpper)
	{
		return Draw(ChallengeMode.Normal, excludeHashesUpper, 0);
	}

	public static DrawCandidate Draw(ChallengeMode mode, HashSet<string> excludeHashesUpper, int stepBand = 0)
	{
		PluginConfig instance = PluginConfig.Instance;
		if (mode == ChallengeMode.Normal && instance != null)
		{
			int d1 = Math.Max(0, Math.Min(4, instance.MinDifficulty));
			int d2 = Math.Max(0, Math.Min(4, instance.MaxDifficulty));
			int dMin = Math.Min(d1, d2);
			int dMax = Math.Max(d1, d2);
			Plugin.Log?.Info($"[BeatWanko] Normal filters: diff={DifficultyName(dMin)}..{DifficultyName(dMax)}, stars={instance.MinStars:0.#}..{instance.MaxStars:0.#}, nps={instance.MinNps:0.#}..{instance.MaxNps:0.#}, len={instance.MinLengthSeconds}..{instance.MaxLengthSeconds}s, continueOnFail={instance.ContinueOnFail}");
		}
		try
		{
			List<DrawCandidate> list = SongDetailsBridge.CollectCandidates(instance, excludeHashesUpper, mode, stepBand);
			if (list != null && list.Count > 0)
			{
				DrawCandidate drawCandidate = list[Rng.Next(list.Count)];
				string extra = ((mode == ChallengeMode.StepUp) ? $" ★{drawCandidate.Stars:0.00} step={stepBand}" : string.Empty);
				Plugin.Log?.Info($"[BeatWanko] Draw ({mode}, {list.Count} candidates): '{drawCandidate.DisplayName}' [{drawCandidate.CharacteristicName} / {DifficultyName(drawCandidate.DifficultyRank)}{extra}]");
				return drawCandidate;
			}
			if (mode.IsRanked())
			{
				Plugin.Log?.Warn($"[BeatWanko] Draw ({mode}, step={stepBand}): no catalogue song matches.");
				return null;
			}
			if (list != null && list.Count == 0)
			{
				Plugin.Log?.Warn("[BeatWanko] Draw: no catalogue song matches the current filters.");
				return null;
			}
		}
		catch (Exception ex)
		{
			Plugin.Log?.Warn("[BeatWanko] Metadata draw failed: " + ex.Message);
			if (mode.IsRanked())
			{
				return null;
			}
		}
		if (mode.IsRanked())
		{
			return null;
		}
		return DrawFromOwnedLibrary(instance, excludeHashesUpper);
	}

	private static DrawCandidate DrawFromOwnedLibrary(PluginConfig config, HashSet<string> excludeHashesUpper)
	{
		try
		{
			if (!SongCoreBridge.Available)
			{
				return null;
			}
			int minLen = config.MinLengthSeconds;
			int maxLen = config.MaxLengthSeconds;
			if (minLen > 0 && maxLen > 0 && minLen > maxLen)
			{
				int tmp = minLen;
				minLen = maxLen;
				maxLen = tmp;
			}
			int d1 = Math.Max(0, Math.Min(4, config.MinDifficulty));
			int d2 = Math.Max(0, Math.Min(4, config.MaxDifficulty));
			int minDiff = Math.Min(d1, d2);
			int maxDiff = Math.Max(d1, d2);
			List<DrawCandidate> list = new List<DrawCandidate>();
			foreach (BeatmapLevel item in SongCoreBridge.AllCustomLevels())
			{
				string levelID = item.levelID;
				if (string.IsNullOrEmpty(levelID) || !levelID.StartsWith("custom_level_", StringComparison.Ordinal))
				{
					continue;
				}
				string text = levelID.Substring("custom_level_".Length).ToUpperInvariant();
				if ((excludeHashesUpper == null || !excludeHashesUpper.Contains(text)) && (minLen <= 0 || !(item.songDuration < (float)minLen)) && (maxLen <= 0 || !(item.songDuration > (float)maxLen)))
				{
					BeatmapKey? prebuiltKey = PickKey(item, minDiff, maxDiff, null);
					if (prebuiltKey.HasValue)
					{
						list.Add(new DrawCandidate
						{
							HashUpper = text,
							LevelId = levelID,
							DisplayName = item.songName + " / " + item.songAuthorName,
							DifficultyRank = (int)prebuiltKey.Value.difficulty,
							CharacteristicName = prebuiltKey.Value.beatmapCharacteristic?.serializedName,
							OwnedLevel = item,
							PrebuiltKey = prebuiltKey
						});
					}
				}
			}
			if (list.Count == 0)
			{
				Plugin.Log?.Warn("[BeatWanko] Draw (library fallback): no owned song matches the filters.");
				return null;
			}
			DrawCandidate drawCandidate = list[Rng.Next(list.Count)];
			Plugin.Log?.Info($"[BeatWanko] Draw (library fallback, {list.Count} candidates): '{drawCandidate.DisplayName}'");
			return drawCandidate;
		}
		catch (Exception arg)
		{
			Plugin.Log?.Error($"[BeatWanko] Library draw failed: {arg}");
			return null;
		}
	}

	public static bool TryBuildKey(DrawCandidate draw, BeatmapLevel level, out BeatmapKey key)
	{
		key = default(BeatmapKey);
		try
		{
			if (draw == null || level == null)
			{
				return false;
			}
			if (draw.PrebuiltKey.HasValue && draw.PrebuiltKey.Value.IsValid())
			{
				key = draw.PrebuiltKey.Value;
				return true;
			}
			if (draw.StrictDifficulty)
			{
				BeatmapKey? exactKey = PickExactKey(level, draw.DifficultyRank, draw.CharacteristicName);
				if (!exactKey.HasValue)
				{
					Plugin.Log?.Warn($"[BeatWanko] Strict difficulty missing on '{level.songName}': {draw.CharacteristicName} / {DifficultyName(draw.DifficultyRank)}");
					return false;
				}
				key = exactKey.Value;
				return key.IsValid();
			}
			BeatmapKey? beatmapKey = PickKey(level, draw.DifficultyRank, draw.DifficultyRank, draw.CharacteristicName)
				?? PickKey(level, PluginConfig.Instance.MinDifficulty, PluginConfig.Instance.MaxDifficulty, draw.CharacteristicName)
				?? PickKey(level, 0, 4, null);
			if (!beatmapKey.HasValue)
			{
				return false;
			}
			key = beatmapKey.Value;
			return key.IsValid();
		}
		catch (Exception ex)
		{
			Plugin.Log?.Warn("[BeatWanko] TryBuildKey failed: " + ex.Message);
			return false;
		}
	}

	private static BeatmapKey? PickExactKey(BeatmapLevel level, int targetRank, string characteristicName)
	{
		if (level == null || string.IsNullOrEmpty(characteristicName))
		{
			return null;
		}
		BeatmapCharacteristicSO charSo = level.GetCharacteristics()
			?.FirstOrDefault((BeatmapCharacteristicSO c) => (UnityEngine.Object)(object)c != null && string.Equals(c.serializedName, characteristicName, StringComparison.Ordinal));
		if ((UnityEngine.Object)(object)charSo == null)
		{
			return null;
		}
		BeatmapDifficulty targetDiff = (BeatmapDifficulty)targetRank;
		List<BeatmapDifficulty> diffs = level.GetDifficulties(charSo)?.ToList();
		if (diffs == null || !diffs.Contains(targetDiff))
		{
			return null;
		}
		if (level.GetDifficultyBeatmapData(charSo, targetDiff) == null)
		{
			return null;
		}
		return new BeatmapKey(level.levelID, charSo, targetDiff);
	}

	private static BeatmapKey? PickKey(BeatmapLevel level, int minRank, int maxRank, string preferredCharacteristicName)
	{
		try
		{
			int r1 = Math.Max(0, Math.Min(4, minRank));
			int r2 = Math.Max(0, Math.Min(4, maxRank));
			int lo = Math.Min(r1, r2);
			int hi = Math.Max(r1, r2);
			List<BeatmapCharacteristicSO> list = new List<BeatmapCharacteristicSO>();
			if (!string.IsNullOrEmpty(preferredCharacteristicName))
			{
				BeatmapCharacteristicSO beatmapCharacteristicSO = level.GetCharacteristics().FirstOrDefault((BeatmapCharacteristicSO c) => (UnityEngine.Object)(object)c != null && c.serializedName == preferredCharacteristicName);
				if ((UnityEngine.Object)(object)beatmapCharacteristicSO != null)
				{
					list.Add(beatmapCharacteristicSO);
				}
			}
			foreach (BeatmapCharacteristicSO characteristic in level.GetCharacteristics())
			{
				if ((UnityEngine.Object)(object)characteristic != null && !list.Contains(characteristic))
				{
					list.Add(characteristic);
				}
			}
			foreach (BeatmapCharacteristicSO item in list)
			{
				List<BeatmapDifficulty> list2 = level.GetDifficulties(item)?.ToList();
				if (list2 == null || list2.Count == 0)
				{
					continue;
				}
				if (lo < hi)
				{
					List<BeatmapDifficulty> list3 = new List<BeatmapDifficulty>();
					foreach (BeatmapDifficulty item2 in list2)
					{
						int num = (int)item2;
						if (num >= lo && num <= hi && level.GetDifficultyBeatmapData(item, item2) != null)
						{
							list3.Add(item2);
						}
					}
					if (list3.Count > 0)
					{
						BeatmapDifficulty difficulty = list3[Rng.Next(list3.Count)];
						return new BeatmapKey(level.levelID, item, difficulty);
					}
				}
				else
				{
					BeatmapDifficulty? beatmapDifficulty = null;
					foreach (BeatmapDifficulty item3 in list2)
					{
						if (item3 == (BeatmapDifficulty)lo)
						{
							beatmapDifficulty = item3;
							break;
						}
					}
					if (!beatmapDifficulty.HasValue)
					{
						using IEnumerator<BeatmapDifficulty> enumerator4 = list2.OrderBy((BeatmapDifficulty d) => Math.Abs((int)(d - lo))).GetEnumerator();
						if (enumerator4.MoveNext())
						{
							BeatmapDifficulty current5 = enumerator4.Current;
							beatmapDifficulty = current5;
						}
					}
					if (beatmapDifficulty.HasValue && level.GetDifficultyBeatmapData(item, beatmapDifficulty.Value) != null)
					{
						return new BeatmapKey(level.levelID, item, beatmapDifficulty.Value);
					}
				}
			}
		}
		catch (Exception ex)
		{
			Plugin.Log?.Warn("[BeatWanko] PickKey failed: " + ex.Message);
		}
		return null;
	}

	public static string DifficultyName(int rank)
	{
		return rank switch
		{
			0 => "Easy",
			1 => "Normal",
			2 => "Hard",
			3 => "Expert",
			4 => "ExpertPlus",
			_ => "?",
		};
	}
}
