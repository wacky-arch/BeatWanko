using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace BeatWanko;

internal static class HistoryStore
{
	private const int MaxSessions = 50;

	private static string FilePath
	{
		get
		{
			try
			{
				return Path.Combine(Environment.CurrentDirectory, "UserData", "BeatWanko", "History.json");
			}
			catch
			{
				return null;
			}
		}
	}

	public static void AppendSession(SessionRecord record)
	{
		try
		{
			string filePath = FilePath;
			if (!string.IsNullOrEmpty(filePath) && record != null)
			{
				List<SessionRecord> list = LoadAll();
				list.Insert(0, record);
				if (list.Count > 50)
				{
					list.RemoveRange(50, list.Count - 50);
				}
				Directory.CreateDirectory(Path.GetDirectoryName(filePath));
				File.WriteAllText(filePath, JsonConvert.SerializeObject(list, Formatting.Indented));
			}
		}
		catch (Exception ex)
		{
			Plugin.Log?.Warn("[BeatWanko] Writing history failed: " + ex.Message);
		}
	}

	public static void UpdateSession(SessionRecord updated)
	{
		try
		{
			string filePath = FilePath;
			if (!string.IsNullOrEmpty(filePath) && updated != null)
			{
				List<SessionRecord> list = LoadAll();
				int num = list.FindIndex((SessionRecord s) => s != null && s.Date == updated.Date && s.Cups == updated.Cups && s.TotalNotes == updated.TotalNotes);
				if (num >= 0)
				{
					list[num] = updated;
					Directory.CreateDirectory(Path.GetDirectoryName(filePath));
					File.WriteAllText(filePath, JsonConvert.SerializeObject(list, Formatting.Indented));
				}
			}
		}
		catch (Exception ex)
		{
			Plugin.Log?.Warn("[BeatWanko] Updating history failed: " + ex.Message);
		}
	}

	public static List<SessionRecord> LoadAll()
	{
		try
		{
			string filePath = FilePath;
			if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
			{
				return new List<SessionRecord>();
			}
			return JsonConvert.DeserializeObject<List<SessionRecord>>(File.ReadAllText(filePath)) ?? new List<SessionRecord>();
		}
		catch (Exception ex)
		{
			Plugin.Log?.Warn("[BeatWanko] Reading history failed: " + ex.Message);
			return new List<SessionRecord>();
		}
	}
}
