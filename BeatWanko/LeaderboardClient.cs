using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BeatWanko;

internal sealed class LeaderboardSubmissionResult
{
	public bool Ok { get; set; }

	public bool Saved { get; set; }

	public bool IsNewBest { get; set; }

	public string Reason { get; set; }

	public int Rank { get; set; }

	public int TotalPlayers { get; set; }

	public string DisplayName { get; set; }

	public string UserMessage { get; set; }
}

internal static class LeaderboardClient
{
	public const string ModVersion = "1.1.7";

	private static readonly HttpClient Http = CreateClient();

	private static HttpClient CreateClient()
	{
		try
		{
			ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
		}
		catch
		{
		}
		HttpClient client = new HttpClient();
		client.Timeout = TimeSpan.FromSeconds(15.0);
		client.DefaultRequestHeaders.UserAgent.ParseAdd("BeatWanko/" + ModVersion + " (+https://github.com/wacky-arch/BeatWanko)");
		return client;
	}

	private static string GetGameVersionSafe()
	{
		try
		{
			return string.IsNullOrEmpty(Application.version) ? "1.40.8" : Application.version;
		}
		catch
		{
			return "1.40.8";
		}
	}

	public static async Task<LeaderboardSubmissionResult> SubmitRunAsync(ChallengeMode mode, SessionEndReason endReason, List<HistoryEntry> entries, DateTimeOffset playedAt, CancellationToken token)
	{
		if (!mode.IsRanked())
		{
			return new LeaderboardSubmissionResult { Ok = false, Reason = "unranked", UserMessage = string.Empty };
		}
		PluginConfig cfg = PluginConfig.Instance;
		if (cfg == null || !cfg.EnableRankingSubmission)
		{
			return new LeaderboardSubmissionResult { Ok = false, Reason = "disabled", UserMessage = "ランキング送信オフ（ローカル保存済み）" };
		}
		string baseUrl = (cfg.LeaderboardApiUrl ?? string.Empty).Trim().TrimEnd('/');
		if (string.IsNullOrEmpty(baseUrl))
		{
			Plugin.Log?.Info("[BeatWanko] LeaderboardApiUrl is not configured yet; run saved locally only.");
			return new LeaderboardSubmissionResult { Ok = false, Reason = "no_endpoint", UserMessage = "ランキングURL未設定（ローカル保存済み）" };
		}
		if (entries == null || entries.Count == 0)
		{
			return new LeaderboardSubmissionResult { Ok = false, Reason = "empty", UserMessage = string.Empty };
		}
		// Capture UnityEngine.Application.version on caller (main) thread before first await.
		string gameVersion = GetGameVersionSafe();
		try
		{
			ResolvedPlayerIdentity identity = await PlayerIdentity.ResolveAsync(token).ConfigureAwait(continueOnCapturedContext: false);
			if (identity == null || !identity.IsValid)
			{
				Plugin.Log?.Warn("[BeatWanko] Could not resolve SteamID/PlatformUserId; skipping ranking upload.");
				return new LeaderboardSubmissionResult { Ok = false, Reason = "no_player_id", UserMessage = "プレイヤーIDを取得できなかったため送信をスキップしました" };
			}
			string apiEndReason = endReason switch
			{
				SessionEndReason.Fail => "fail",
				SessionEndReason.Completed => "complete",
				_ => "close_lid"
			};
			int clearedCount = entries.Count((HistoryEntry e) => e != null && e.Cleared);
			long totalScore = entries.Where((HistoryEntry e) => e != null).Sum((HistoryEntry e) => (long)Math.Max(0, e.MultipliedScore));
			float totalSeconds = entries.Where((HistoryEntry e) => e != null).Sum((HistoryEntry e) => Math.Max(0f, e.DurationSeconds));
			List<object> songsPayload = new List<object>(entries.Count);
			for (int i = 0; i < entries.Count; i++)
			{
				HistoryEntry e = entries[i];
				if (e == null)
				{
					continue;
				}
				string hash = e.HashUpper;
				if (string.IsNullOrEmpty(hash) && !string.IsNullOrEmpty(e.LevelId) && e.LevelId.StartsWith("custom_level_", StringComparison.OrdinalIgnoreCase))
				{
					hash = e.LevelId.Substring("custom_level_".Length).ToUpperInvariant();
				}
				songsPayload.Add(new
				{
					order = i + 1,
					song_hash = (hash ?? string.Empty).ToUpperInvariant(),
					song_name = e.SongName ?? "Unknown",
					difficulty = e.Difficulty ?? "Expert",
					characteristic = string.IsNullOrEmpty(e.Characteristic) ? "Standard" : e.Characteristic,
					score = Math.Max(0, e.MultipliedScore),
					accuracy_percent = Math.Round(Math.Max(0f, Math.Min(100f, e.AccuracyPercent)), 2),
					cleared = e.Cleared,
					stars = Math.Round(Math.Max(0f, e.Stars), 2),
					step_band = e.StepBand
				});
			}
			object payload = new
			{
				schema_version = 1,
				mod_version = ModVersion,
				game_version = gameVersion,
				player_id = identity.PlayerId,
				platform_user_name = identity.PlatformUserName ?? string.Empty,
				mode = mode.ToApiMode(),
				played_at = playedAt.ToString("o"),
				total_play_seconds = Math.Round(totalSeconds, 2),
				end_reason = apiEndReason,
				cleared_count = clearedCount,
				total_score = totalScore,
				songs = songsPayload
			};
			string json = JsonConvert.SerializeObject(payload);
			string endpoint = baseUrl + "/api/runs";
			using StringContent content = new StringContent(json, Encoding.UTF8, "application/json");
			using HttpResponseMessage response = await Http.PostAsync(endpoint, content, token).ConfigureAwait(continueOnCapturedContext: false);
			string body = await response.Content.ReadAsStringAsync().ConfigureAwait(continueOnCapturedContext: false);
			if (!response.IsSuccessStatusCode)
			{
				Plugin.Log?.Warn($"[BeatWanko] Ranking upload HTTP {(int)response.StatusCode}: {body}");
				return new LeaderboardSubmissionResult
				{
					Ok = false,
					Reason = "http_error",
					UserMessage = $"ランキング送信失敗 (HTTP {(int)response.StatusCode})"
				};
			}
			JObject obj = JObject.Parse(body);
			bool ok = obj.Value<bool?>("ok") ?? false;
			bool saved = obj.Value<bool?>("saved") ?? false;
			bool isNewBest = obj.Value<bool?>("is_new_best") ?? false;
			string reason = obj.Value<string>("reason") ?? string.Empty;
			int rank = obj.Value<int?>("rank") ?? 0;
			int totalPlayers = obj.Value<int?>("total_players") ?? 0;
			string displayName = obj.Value<string>("display_name") ?? identity.PlatformUserName;
			string rankPart = ((rank > 0 && totalPlayers > 0) ? $"（現在 #{rank} / {totalPlayers}人）" : string.Empty);
			string msg;
			if (!ok)
			{
				msg = "ランキング送信エラー：" + (obj.Value<string>("error") ?? reason);
			}
			else if (!saved && reason == "skipped_not_new_best")
			{
				msg = "蓋を閉じました（自己ベスト未更新のため記録せず）" + rankPart;
			}
			else if (isNewBest)
			{
				msg = "ランキング登録完了！自己ベスト更新！" + rankPart;
			}
			else
			{
				msg = "ランキング履歴を送信しました" + rankPart;
			}
			Plugin.Log?.Info($"[BeatWanko] Ranking upload done: ok={ok}, saved={saved}, isNewBest={isNewBest}, reason={reason}, rank={rank}/{totalPlayers}, player='{displayName}'");
			return new LeaderboardSubmissionResult
			{
				Ok = ok,
				Saved = saved,
				IsNewBest = isNewBest,
				Reason = reason,
				Rank = rank,
				TotalPlayers = totalPlayers,
				DisplayName = displayName,
				UserMessage = msg
			};
		}
		catch (Exception ex)
		{
			Plugin.Log?.Warn("[BeatWanko] Ranking upload failed: " + ex.Message);
			return new LeaderboardSubmissionResult
			{
				Ok = false,
				Reason = "exception",
				UserMessage = "ランキング送信失敗（ネットワークを確認してください）"
			};
		}
	}
}
