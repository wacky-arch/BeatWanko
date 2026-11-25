using System;
using System.Collections.Generic;

namespace BeatWanko;

internal static class SongDetailsBridge
{
	private static bool? _available;

	public static bool Available
	{
		get
		{
			if (!_available.HasValue)
			{
				bool flag = false;
				try
				{
					flag = Type.GetType("SongDetailsCache.SongDetails, SongDetailsCache") != null;
				}
				catch
				{
					flag = false;
				}
				_available = flag;
			}
			return _available.Value;
		}
	}

	public static bool DataReady
	{
		get
		{
			if (!Available)
			{
				return false;
			}
			try
			{
				return SongDetailsImpl.DataReady;
			}
			catch
			{
				return false;
			}
		}
	}

	public static void KickInit()
	{
		if (!Available)
		{
			return;
		}
		try
		{
			SongDetailsImpl.KickInit();
		}
		catch (Exception ex)
		{
			Plugin.Log?.Warn("[BeatWanko] SongDetailsCache init failed: " + ex.Message);
		}
	}

	public static List<DrawCandidate> CollectCandidates(PluginConfig config, HashSet<string> excludeHashesUpper)
	{
		return CollectCandidates(config, excludeHashesUpper, ChallengeMode.Normal, 0);
	}

	public static List<DrawCandidate> CollectCandidates(PluginConfig config, HashSet<string> excludeHashesUpper, ChallengeMode mode, int stepBand)
	{
		if (!Available || !DataReady)
		{
			return null;
		}
		try
		{
			return SongDetailsImpl.CollectCandidates(config, excludeHashesUpper, mode, stepBand);
		}
		catch (Exception ex)
		{
			Plugin.Log?.Warn("[BeatWanko] SongDetailsCache scan failed: " + ex.Message);
			return null;
		}
	}
}
