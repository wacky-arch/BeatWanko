using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HMUI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BeatWanko;

internal static class Session
{
	private const int MaxFirstBowlAttempts = 5;

	private const int MaxDrawAttempts = 10;

	public static bool ExpectingChainedLaunch;

	private static readonly List<HistoryEntry> Entries = new List<HistoryEntry>();

	private static readonly HashSet<string> PlayedHashes = new HashSet<string>();

	private static Coroutine _interstitial;

	private static CancellationTokenSource _downloadCts;

	private static CancellationTokenSource _menuStartCts;

	private static bool _recordPersisted;

	private static string _persistedSessionDate;

	private static int _summaryToken;

	private static GameScenesManager _cachedScenesManager;

	private static ChallengeMode _pendingMenuStartMode = ChallengeMode.Normal;

	private static DrawCandidate _currentDraw;

	private static DateTimeOffset _sessionStartedAt;

	private static bool _pendingFirstBowlFadeGuard;

	private static bool _pendingMenuStartDownloaded;

	private static bool _downloadedAnySongInSession;

	private static bool _favoritesChangedInSession;

	private static bool _playerMenuQuitPressed;

	internal static bool ExpectingMenuStartLaunch { get; set; }

	internal static void NotifyPlayerMenuQuitPressed()
	{
		_playerMenuQuitPressed = true;
	}

	internal static bool IsAbortedLevelLoad(StandardLevelScenesTransitionSetupDataSO setupData, LevelCompletionResults results, bool playerMenuQuitPressed)
	{
		if (results == null || playerMenuQuitPressed)
		{
			return false;
		}
		if (results.levelEndAction != LevelCompletionResults.LevelEndAction.Quit || results.levelEndStateType != LevelCompletionResults.LevelEndStateType.Incomplete)
		{
			return false;
		}
		bool setupProvided = !object.ReferenceEquals(setupData, null);
		IReadonlyBeatmapData bd = null;
		if (setupProvided)
		{
			try
			{
				bd = setupData.transformedBeatmapData;
			}
			catch
			{
			}
		}
		bool beatmapInvalid = setupProvided && bd == null;
		if (bd != null)
		{
			try
			{
				PropertyInfo validProp = bd.GetType().GetProperty("areValid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (validProp != null && validProp.GetValue(bd) is bool valid && !valid)
				{
					beatmapInvalid = true;
				}
			}
			catch
			{
			}
		}
		bool zeroPlayQuit = results.endSongTime <= 0.05f && results.goodCutsCount == 0 && results.badCutsCount == 0 && results.missedCount == 0;
		return beatmapInvalid || zeroPlayQuit;
	}

	internal static void ArmFirstBowlFadeGuard()
	{
		_pendingFirstBowlFadeGuard = true;
	}

	internal static void ClearFirstBowlFadeGuard()
	{
		_pendingFirstBowlFadeGuard = false;
	}

	internal static bool ConsumeFirstBowlFadeGuard()
	{
		if (!_pendingFirstBowlFadeGuard)
		{
			return false;
		}
		_pendingFirstBowlFadeGuard = false;
		return State == SessionState.Playing;
	}

	internal static SessionState State { get; private set; } = SessionState.Idle;

	internal static ChallengeMode ActiveMode { get; private set; } = ChallengeMode.Normal;

	internal static bool IsPauseBlocked => State == SessionState.Playing && ActiveMode.IsPauseDisabled();

	internal static LevelLaunchContext Current { get; private set; }

	internal static ChallengeMode GetEffectiveModeForLaunch(bool expectingMenuStart, bool expectingChained)
	{
		if (expectingMenuStart)
		{
			return _pendingMenuStartMode;
		}
		if (expectingChained || State == SessionState.Playing || State == SessionState.Interstitial)
		{
			return ActiveMode;
		}
		return ChallengeMode.Normal;
	}

	public static void OnManualLaunch(LevelLaunchContext ctx)
	{
		try
		{
			_summaryToken++;
			if (CancelMenuStart(disposeNow: false))
			{
				Plugin.Log?.Info("[BeatWanko] Menu START preparation cancelled by manual launch.");
			}
			bool continuing = State == SessionState.Playing || State == SessionState.Interstitial;
			CancelInterstitial();
			if (!continuing)
			{
				Entries.Clear();
				PlayedHashes.Clear();
				_recordPersisted = false;
				_persistedSessionDate = null;
				ActiveMode = (ExpectingMenuStartLaunch ? _pendingMenuStartMode : ChallengeMode.Normal);
				_downloadedAnySongInSession = ExpectingMenuStartLaunch && _pendingMenuStartDownloaded;
				_favoritesChangedInSession = false;
				_sessionStartedAt = DateTimeOffset.Now;
				PlayerIdentity.CaptureOnMainThread();
			}
			Current = ctx;
			State = SessionState.Playing;
			_playerMenuQuitPressed = false;
			WankoUI.ResetForNewSession();
			WankoUI.RegisterMenuButton();
			string text = ExtractHash(ctx.CurrentLevel?.levelID);
			if (text != null)
			{
				PlayedHashes.Add(text);
			}
			if (continuing)
			{
				Plugin.Log?.Info($"[BeatWanko] Session continued ({ActiveMode}, manual/restart launch): '{ctx.CurrentLevel?.songName}' ({ctx.GameMode})");
			}
			else
			{
				Plugin.Log?.Info($"[BeatWanko] わんこそばチャレンジ開始 ({ActiveMode}): '{ctx.CurrentLevel?.songName}' ({ctx.GameMode})");
			}
		}
		catch (Exception arg)
		{
			Plugin.Log?.Error($"[BeatWanko] OnManualLaunch failed: {arg}");
		}
	}

	public static void OnChainedLaunch(LevelLaunchContext ctx)
	{
		try
		{
			Current = ctx;
			State = SessionState.Playing;
			_playerMenuQuitPressed = false;
			Plugin.Log?.Info($"[BeatWanko] Next bowl served ({ActiveMode}): '{ctx.CurrentLevel?.songName}'");
		}
		catch (Exception arg)
		{
			Plugin.Log?.Error($"[BeatWanko] OnChainedLaunch failed: {arg}");
		}
	}

	public static void OnGameSceneFinished(StandardLevelScenesTransitionSetupDataSO setupData, LevelCompletionResults results)
	{
		try
		{
			if (State == SessionState.Playing && Current != null && !((UnityEngine.Object)(object)setupData == null) && results != null)
			{
				string text = null;
				try
				{
					text = setupData.beatmapLevel?.levelID;
				}
				catch
				{
				}
				string text2 = Current.CurrentLevel?.levelID;
				Plugin.Log?.Info($"[BeatWanko] Level finished ({ActiveMode}): '{Current.CurrentLevel?.songName}' endState={results.levelEndStateType} endAction={results.levelEndAction} levelMatch={text == text2}");
				if (text == null || text2 == null || !(text != text2))
				{
					OnLevelFinished(Current, setupData, results);
				}
			}
		}
		catch (Exception arg)
		{
			Plugin.Log?.Error($"[BeatWanko] OnGameSceneFinished failed: {arg}");
		}
	}

	public static void OnLevelFinished(LevelLaunchContext ctx, StandardLevelScenesTransitionSetupDataSO setupData, LevelCompletionResults results)
	{
		try
		{
			if (ctx != Current || State != SessionState.Playing || results == null)
			{
				Plugin.Log?.Warn("[BeatWanko] OnLevelFinished ignored (session not armed for this finish).");
			}
			else if (results.levelEndAction == LevelCompletionResults.LevelEndAction.Quit)
			{
				if (IsAbortedLevelLoad(setupData, results, _playerMenuQuitPressed))
				{
					Plugin.Log?.Warn($"[BeatWanko] Level '{ctx.CurrentLevel?.songName}' failed to load in Beat Saber (Incomplete/Quit without player Menu press, endSongTime={results.endSongTime:0.00}s) — discarding draw and re-drawing.");
					State = SessionState.Interstitial;
					_interstitial = Plugin.Runner?.Run(InterstitialRoutine(ctx));
					return;
				}
				EndSession(showSummary: true, SessionEndReason.Quit, null);
			}
			else if (results.levelEndAction == LevelCompletionResults.LevelEndAction.Restart)
			{
				if (ActiveMode.IsRanked())
				{
					Plugin.Log?.Info("[BeatWanko] Restart triggered in ranked mode; ending session.");
					EndSession(showSummary: true, SessionEndReason.Quit, "リスタートによりチャレンジを終了しました");
				}
			}
			else if (results.levelEndStateType == LevelCompletionResults.LevelEndStateType.Cleared || results.levelEndStateType == LevelCompletionResults.LevelEndStateType.Failed)
			{
				IReadonlyBeatmapData readonlyBeatmapData = null;
				try
				{
					readonlyBeatmapData = setupData?.transformedBeatmapData;
				}
				catch (Exception ex)
				{
					Plugin.Log?.Warn("[BeatWanko] Failed to inspect transformedBeatmapData: " + ex.Message);
				}
				if (readonlyBeatmapData != null && readonlyBeatmapData.cuttableNotesCount == 0 && ScoreModel.ComputeMaxMultipliedScoreForBeatmap(readonlyBeatmapData) == 0)
				{
					Plugin.Log?.Warn("[BeatWanko] Empty beatmap (0 notes) — draw discarded, re-drawing.");
					State = SessionState.Interstitial;
					_interstitial = Plugin.Runner?.Run(InterstitialRoutine(ctx));
					return;
				}
				bool cleared = results.levelEndStateType == LevelCompletionResults.LevelEndStateType.Cleared;
				RecordEntry(ctx, setupData, results, cleared);
				if (!cleared)
				{
					bool continueOnFail = !ActiveMode.IsRanked() && (PluginConfig.Instance?.ContinueOnFail ?? false);
					if (!continueOnFail)
					{
						Plugin.Log?.Info("[BeatWanko] Fail — challenge over （お腹いっぱい判定）.");
						EndSession(showSummary: true, SessionEndReason.Fail, "Failしたためチャレンジ終了です");
						return;
					}
				}
				if (cleared && ActiveMode == ChallengeMode.StepUp)
				{
					int clearedCups = Entries.Count((HistoryEntry e) => e != null && e.Cleared);
					if (clearedCups >= ChallengeModeExtensions.StepUpMaxStep)
					{
						Plugin.Log?.Info("[BeatWanko] StepUp 14 songs cleared — challenge completed!");
						EndSession(showSummary: true, SessionEndReason.Completed, "🎉 SS⭐️ステップアップ 14曲完走おめでとうございます！");
						return;
					}
				}
				State = SessionState.Interstitial;
				_interstitial = Plugin.Runner?.Run(InterstitialRoutine(ctx));
			}
			else
			{
				Plugin.Log?.Warn($"[BeatWanko] OnLevelFinished: unhandled end state {results.levelEndStateType}/{results.levelEndAction}; ending session quietly.");
				EndSession(showSummary: false, SessionEndReason.Quit, null);
			}
		}
		catch (Exception arg)
		{
			Plugin.Log?.Error($"[BeatWanko] OnLevelFinished failed: {arg}");
		}
	}

	public static void RequestCloseLid()
	{
		try
		{
			if (State == SessionState.Interstitial || State == SessionState.Playing)
			{
				_playerMenuQuitPressed = true;
				Plugin.Log?.Info("[BeatWanko] 蓋を閉じる — challenge ends by player request.");
				EndSession(showSummary: true, SessionEndReason.Quit, null);
			}
		}
		catch (Exception arg)
		{
			Plugin.Log?.Error($"[BeatWanko] RequestCloseLid failed: {arg}");
		}
	}

	public static void StartChallengeFromMenu()
	{
		StartChallengeFromMenu(ChallengeMode.Normal);
	}

	public static void StartChallengeFromMenu(ChallengeMode mode)
	{
		try
		{
			Plugin.Log?.Info($"[BeatWanko] START pressed on the settings page (mode={mode}).");
			PlayerIdentity.CaptureOnMainThread();
			PlayerIdentity.KickResolve();
			if (Plugin.Runner == null)
			{
				Plugin.Log?.Error("[BeatWanko] START aborted: coroutine runner is not available.");
			}
			else if (PluginConfig.Instance == null)
			{
				Plugin.Log?.Warn("[BeatWanko] START ignored: plugin config is not available.");
				WankoUI.SetStartStatusText("設定が読み込めませんでした");
			}
			else if (State == SessionState.Playing || State == SessionState.Interstitial)
			{
				Plugin.Log?.Warn("[BeatWanko] START ignored: a challenge is already active.");
				WankoUI.SetStartStatusText("チャレンジ進行中です");
			}
			else if (_menuStartCts != null)
			{
				Plugin.Log?.Warn("[BeatWanko] START ignored: bowl #1 is already being prepared.");
			}
			else if (mode.IsRanked() && !SongDetailsBridge.DataReady)
			{
				Plugin.Log?.Warn("[BeatWanko] START deferred: SongDetailsCache is not ready yet for ranked mode.");
				WankoUI.SetStartStatusText("SongDetailsCache を読み込み中です…数秒後にもう一度押してください");
			}
			else
			{
				_pendingMenuStartMode = mode;
				_menuStartCts = new CancellationTokenSource();
				Plugin.Runner.Run(MenuStartRoutine(_menuStartCts, mode));
			}
		}
		catch (Exception arg)
		{
			Plugin.Log?.Error($"[BeatWanko] StartChallengeFromMenu failed: {arg}");
		}
	}

	private static DrawCandidate TryDrawFirstBowl(ChallengeMode mode, HashSet<string> triedHashes)
	{
		try
		{
			int stepBand = ((mode == ChallengeMode.StepUp) ? 1 : 0);
			return SongSource.Draw(mode, triedHashes, stepBand);
		}
		catch (Exception arg)
		{
			Plugin.Log?.Error($"[BeatWanko] Menu START draw failed: {arg}");
			return null;
		}
	}

	private static IEnumerator MenuStartRoutine(CancellationTokenSource cts, ChallengeMode mode)
	{
		HashSet<string> firstBowlTried = new HashSet<string>();
		try
		{
			CancellationToken token = cts.Token;
			string modePrefix = $"[{mode.ShortBadge()}] ";
			DrawCandidate draw = null;
			BeatmapLevel level = null;
			BeatmapKey key = default(BeatmapKey);
			bool readyToLaunch = false;
			bool downloadedBowl1 = false;
			for (int attempt = 1; attempt <= MaxFirstBowlAttempts; attempt++)
			{
				if (token.IsCancellationRequested)
				{
					Plugin.Log?.Info("[BeatWanko] Menu START preparation was cancelled.");
					yield break;
				}
				SafeUi(delegate
				{
					WankoUI.SetStartStatusText(modePrefix + (attempt == 1 ? "抽選中…" : $"再抽選中… ({attempt}/{MaxFirstBowlAttempts})"));
				});
				draw = TryDrawFirstBowl(mode, firstBowlTried);
				if (token.IsCancellationRequested)
				{
					Plugin.Log?.Info("[BeatWanko] Menu START preparation was cancelled.");
					yield break;
				}
				if (draw == null)
				{
					Plugin.Log?.Warn("[BeatWanko] Menu START: no song matches the current mode/filters; nothing launched.");
					SafeUi(delegate
					{
						WankoUI.SetStartStatusText(modePrefix + "条件に合う曲が見つかりませんでした");
					});
					yield break;
				}
				if (!string.IsNullOrEmpty(draw.HashUpper))
				{
					firstBowlTried.Add(draw.HashUpper);
					PlayedHashes.Add(draw.HashUpper);
				}
				Plugin.Log?.Info($"[BeatWanko] Menu START draw result ({mode}): '{draw.DisplayName}'.");
				string stepTag = ((mode == ChallengeMode.StepUp) ? "[★0〜1台] " : string.Empty);
				SafeUi(delegate
				{
					WankoUI.SetStartStatusText(modePrefix + "1曲目：" + stepTag + draw.DisplayName + "（準備中…）");
				});
				level = draw.OwnedLevel;
				if (level == null && draw.LevelId != null && SongCoreBridge.TryGetCustomLevel(draw.LevelId, out level))
				{
					Plugin.Log?.Info("[BeatWanko] Menu START: '" + draw.DisplayName + "' is already installed; using the fast path (no download).");
				}
				if (level == null)
				{
					if (!SongCoreBridge.Available)
					{
						Plugin.Log?.Error("[BeatWanko] Menu START: SongCore is not available, so an unowned song cannot be prepared.");
						SafeUi(delegate
						{
							WankoUI.SetStartStatusText("SongCore が見つからないため開始できませんでした");
						});
						yield break;
					}
					SafeUi(delegate
					{
						WankoUI.SetStartStatusText(modePrefix + "ダウンロード中：" + stepTag + draw.DisplayName);
					});
					using CancellationTokenSource dlCts = CancellationTokenSource.CreateLinkedTokenSource(token);
					Task<BeatmapLevel> prepTask = Downloader.DownloadAndLoadAsync(draw, dlCts.Token);
					float waited = 0f;
					float timeout = Math.Max(1f, PluginConfig.Instance?.DownloadTimeoutSeconds ?? 15f);
					bool timedOut = false;
					while (!prepTask.IsCompleted && !token.IsCancellationRequested)
					{
						float effectiveTimeout = (Downloader.IsWaitingForSongCoreRegistration ? (timeout + 8f) : timeout);
						if (waited >= effectiveTimeout)
						{
							timedOut = true;
							break;
						}
						yield return null;
						waited += Time.unscaledDeltaTime;
					}
					if (timedOut)
					{
						Plugin.Log?.Warn($"[BeatWanko] Menu START download timeout ({timeout:0}s) for '{draw.DisplayName}'; cancelling and re-drawing.");
						try
						{
							dlCts.Cancel();
						}
						catch
						{
						}
						float grace = 0f;
						while (!prepTask.IsCompleted && grace < 2f)
						{
							yield return null;
							grace += Time.unscaledDeltaTime;
						}
						Downloader.CleanupIncomplete();
						SafeUi(delegate
						{
							WankoUI.SetStartStatusText($"{modePrefix}ダウンロードがタイムアウトしました。別の曲を抽選します… ({attempt}/{MaxFirstBowlAttempts})");
						});
						continue;
					}
					if (token.IsCancellationRequested)
					{
						Plugin.Log?.Info("[BeatWanko] Menu START preparation was cancelled.");
						float grace = 0f;
						while (!prepTask.IsCompleted && grace < 1f)
						{
							yield return null;
							grace += Time.unscaledDeltaTime;
						}
						Downloader.CleanupIncomplete();
						if (State != SessionState.Playing && State != SessionState.Interstitial)
						{
							PlayedHashes.Clear();
						}
						yield break;
					}
					level = ((prepTask.IsFaulted || prepTask.IsCanceled) ? null : prepTask.Result);
					if (level == null)
					{
						Plugin.Log?.Warn("[BeatWanko] Menu START: preparing '" + draw.DisplayName + "' failed; trying another song.");
						continue;
					}
					downloadedBowl1 = true;
				}
				if (token.IsCancellationRequested)
				{
					Plugin.Log?.Info("[BeatWanko] Menu START preparation was cancelled.");
					yield break;
				}
				if (!SongSource.TryBuildKey(draw, level, out key))
				{
					Plugin.Log?.Warn("[BeatWanko] Menu START: no exact playable difficulty found on '" + draw.DisplayName + "'; trying another song.");
					continue;
				}
				readyToLaunch = true;
				break;
			}
			if (!readyToLaunch || draw == null || level == null)
			{
				if (State != SessionState.Playing && State != SessionState.Interstitial)
				{
					PlayedHashes.Clear();
				}
				SafeUi(delegate
				{
					WankoUI.SetStartStatusText("曲の準備に失敗しました（再試行上限）");
				});
				yield break;
			}
			SafeUi(delegate
			{
				WankoUI.SetStartStatusText(modePrefix + "準備中：" + draw.DisplayName);
			});
			bool prepared = false;
			string prepError = null;
			yield return MenuLauncher.PrepareSoloFlowForFirstBowlRoutine(token, delegate(bool ok, string err)
			{
				prepared = ok;
				prepError = err;
			});
			if (token.IsCancellationRequested)
			{
				Plugin.Log?.Info("[BeatWanko] Menu START preparation was cancelled.");
				yield break;
			}
			if (!prepared)
			{
				Plugin.Log?.Error("[BeatWanko] Menu START: solo flow preparation failed: " + prepError);
				if (State != SessionState.Playing && State != SessionState.Interstitial)
				{
					PlayedHashes.Clear();
				}
				SafeUi(delegate
				{
					WankoUI.SetStartStatusText("開始できませんでした（ログを確認してください）");
				});
				yield break;
			}
			SafeUi(delegate
			{
				WankoUI.SetStartStatusText(modePrefix + "開始します：" + draw.DisplayName);
			});
			_currentDraw = draw;
			_pendingMenuStartMode = mode;
			_pendingMenuStartDownloaded = downloadedBowl1;
			ExpectingChainedLaunch = false;
			FinishMenuStart(cts);
			ExpectingMenuStartLaunch = true;
			bool launched;
			string error;
			try
			{
				launched = MenuLauncher.LaunchPreparedSoloFirstBowl(level, key, out error);
			}
			finally
			{
				ExpectingMenuStartLaunch = false;
				_pendingMenuStartMode = ChallengeMode.Normal;
				_pendingMenuStartDownloaded = false;
			}
			if (!launched)
			{
				ClearFirstBowlFadeGuard();
				if (State != SessionState.Playing && State != SessionState.Interstitial)
				{
					PlayedHashes.Clear();
				}
				Plugin.Log?.Error("[BeatWanko] Menu START: launch failed: " + error);
				SafeUi(delegate
				{
					WankoUI.SetStartStatusText("開始できませんでした（ログを確認してください）");
				});
			}
		}
		finally
		{
			FinishMenuStart(cts);
		}
	}

	private static void FinishMenuStart(CancellationTokenSource cts)
	{
		if (cts == null)
		{
			return;
		}
		if (_menuStartCts == cts)
		{
			_menuStartCts = null;
		}
		try
		{
			cts.Dispose();
		}
		catch
		{
		}
	}

	private static bool CancelMenuStart(bool disposeNow)
	{
		CancellationTokenSource menuStartCts = _menuStartCts;
		if (menuStartCts == null)
		{
			return false;
		}
		_menuStartCts = null;
		_pendingMenuStartMode = ChallengeMode.Normal;
		_pendingMenuStartDownloaded = false;
		try
		{
			menuStartCts.Cancel();
		}
		catch
		{
		}
		if (disposeNow)
		{
			try
			{
				menuStartCts.Dispose();
			}
			catch
			{
			}
		}
		Downloader.CleanupIncomplete();
		return true;
	}

	public static void StopChallengeFromMenu()
	{
		try
		{
			Plugin.Log?.Info("[BeatWanko] STOP pressed on the settings page.");
			bool flag = CancelMenuStart(disposeNow: false);
			if (flag)
			{
				Plugin.Log?.Info("[BeatWanko] Menu START preparation cancelled by STOP.");
			}
			if (State == SessionState.Interstitial)
			{
				EndSession(showSummary: true, SessionEndReason.Quit, null);
				WankoUI.SetStartStatusText("チャレンジを終了しました");
			}
			else if (State == SessionState.Playing)
			{
				CancelInterstitial();
				State = SessionState.Ended;
				PersistCurrentSession(SessionEndReason.Quit);
				Plugin.Log?.Info("[BeatWanko] Session disarmed during play; the current song will finish as a normal solo song.");
				WankoUI.SetStartStatusText("チャレンジを終了しました（再生中の曲はそのまま終わります）");
			}
			else if (flag)
			{
				PlayedHashes.Clear();
				State = SessionState.Idle;
				WankoUI.SetStartStatusText("チャレンジ開始をキャンセルしました");
			}
			else
			{
				Plugin.Log?.Info("[BeatWanko] STOP ignored: no active challenge.");
				WankoUI.SetStartStatusText("進行中のチャレンジはありません");
			}
		}
		catch (Exception arg)
		{
			Plugin.Log?.Error($"[BeatWanko] StopChallengeFromMenu failed: {arg}");
		}
	}

	public static void ToggleEntryFavorite(int index)
	{
		try
		{
			if (index < 0 || index >= Entries.Count)
			{
				return;
			}
			HistoryEntry historyEntry = Entries[index];
			if (historyEntry == null || string.IsNullOrEmpty(historyEntry.LevelId))
			{
				WankoUI.SetSummaryNote("この曲はお気に入り登録できません");
				return;
			}
			bool favorite = !FavoritesBridge.IsFavorite(historyEntry.LevelId);
			if (FavoritesBridge.TrySetFavorite(historyEntry.LevelId, favorite, out var actualFavorite))
			{
				_favoritesChangedInSession = true;
				foreach (HistoryEntry entry in Entries)
				{
					if (entry != null && string.Equals(entry.LevelId, historyEntry.LevelId, StringComparison.Ordinal))
					{
						entry.Favorite = actualFavorite;
					}
				}
				if (_recordPersisted && !string.IsNullOrEmpty(_persistedSessionDate))
				{
					HistoryStore.UpdateSession(ToRecord(_persistedSessionDate, SessionEndReason.Quit));
				}
				WankoUI.RefreshSummaryHistory(BuildSummary(null));
				WankoUI.SetSummaryNote(actualFavorite ? ("『" + historyEntry.SongName + "』をゲームのお気に入りに登録しました") : ("『" + historyEntry.SongName + "』をゲームのお気に入りから解除しました"));
			}
			else
			{
				WankoUI.SetSummaryNote("お気に入り更新に失敗しました");
			}
		}
		catch (Exception arg)
		{
			Plugin.Log?.Error($"[BeatWanko] ToggleEntryFavorite failed: {arg}");
		}
	}

	public static void EndSession(bool showSummary, SessionEndReason reason, string notice)
	{
		try
		{
			_pendingFirstBowlFadeGuard = false;
			CancelInterstitial();
			State = SessionState.Ended;
			WankoUI.HidePanel();
			int token = ++_summaryToken;
			ChallengeMode modeSnapshot = ActiveMode;
			DateTimeOffset playedAt = ((_sessionStartedAt == default) ? DateTimeOffset.Now : _sessionStartedAt);
			List<HistoryEntry> entriesSnapshot = new List<HistoryEntry>(Entries);
			PersistCurrentSession(reason);
			bool quiet = !showSummary || (reason == SessionEndReason.Quit && Entries.Count == 0);
			bool shouldSubmit = modeSnapshot.IsRanked() && entriesSnapshot.Count > 0;
			if (!quiet)
			{
				int cups = Entries.Count((HistoryEntry e) => e != null && e.Cleared);
				if (reason == SessionEndReason.Fail && cups == 0)
				{
					Plugin.Log?.Info($"[BeatWanko] First-bowl Fail — showing summary (entries: {Entries.Count}, notice: {notice}).");
				}
				string initialNote = notice;
				if (shouldSubmit && string.IsNullOrEmpty(initialNote))
				{
					initialNote = ((PluginConfig.Instance?.EnableRankingSubmission ?? true) ? "ランキングへ送信中…" : "ランキング送信オフ（ローカル保存済み）");
				}
				SessionSummary summary = BuildSummary(initialNote);
				FlowCoordinator resultsFc = Current?.OriginalLevelFinished?.Target as FlowCoordinator;
				bool fromGameplay = string.Equals(SceneManager.GetActiveScene().name, "GameCore", StringComparison.Ordinal) || IsSceneInTransition();
				bool expectResults = fromGameplay && notice != null;
				Plugin.Runner?.Run(ShowSummaryDelayed(summary, resultsFc, fromGameplay, expectResults, token));
				if (shouldSubmit)
				{
					StartRankingUpload(modeSnapshot, reason, entriesSnapshot, playedAt, summary, token);
				}
			}
			else
			{
				if (reason == SessionEndReason.Quit && Entries.Count == 0)
				{
					Plugin.Log?.Info("[BeatWanko] First-bowl Quit with 0 recorded bowls — returning quietly to Idle without summary.");
				}
				State = SessionState.Idle;
				FavoritesBridge.ScheduleSafeSoloRefresh(_downloadedAnySongInSession, _favoritesChangedInSession);
			}
			Plugin.Log?.Info($"[BeatWanko] Session ended (mode: {modeSnapshot}, reason: {reason}, entries: {Entries.Count}, quiet: {quiet}). Cups: {Entries.Count((HistoryEntry e) => e != null && e.Cleared)}");
		}
		catch (Exception arg)
		{
			Plugin.Log?.Error($"[BeatWanko] EndSession failed: {arg}");
		}
	}

	private static void StartRankingUpload(ChallengeMode mode, SessionEndReason reason, List<HistoryEntry> entries, DateTimeOffset playedAt, SessionSummary summary, int summaryToken)
	{
		Task<LeaderboardSubmissionResult> uploadTask = LeaderboardClient.SubmitRunAsync(mode, reason, entries, playedAt, CancellationToken.None);
		uploadTask.ContinueWith(delegate(Task<LeaderboardSubmissionResult> t)
		{
			try
			{
				LeaderboardSubmissionResult result = (t.IsFaulted || t.IsCanceled) ? null : t.Result;
				if (result == null || string.IsNullOrEmpty(result.UserMessage))
				{
					return;
				}
				MainThread.Post(delegate
				{
					if (summaryToken != _summaryToken)
					{
						return;
					}
					string merged = result.UserMessage;
					if (reason == SessionEndReason.Fail || reason == SessionEndReason.Completed)
					{
						string baseNotice = (reason == SessionEndReason.Completed)
							? "🎉 14曲完走おめでとうございます！"
							: "Failしたためチャレンジ終了です";
						merged = baseNotice + " ／ " + result.UserMessage;
					}
					if (summary != null)
					{
						summary.Notice = merged;
					}
					WankoUI.SetSummaryNote(merged);
				});
			}
			catch
			{
			}
		}, TaskScheduler.Default);
	}

	public static void OnSummaryClosed()
	{
		try
		{
			_summaryToken++;
			if (State == SessionState.Ended)
			{
				State = SessionState.Idle;
			}
			WankoUI.HideAll();
			FavoritesBridge.ScheduleSafeSoloRefresh(_downloadedAnySongInSession, _favoritesChangedInSession);
		}
		catch (Exception ex)
		{
			Plugin.Log?.Warn("[BeatWanko] OnSummaryClosed failed: " + ex.Message);
		}
	}

	public static void ResetAll()
	{
		try
		{
			_summaryToken++;
			CancelInterstitial();
			CancelMenuStart(disposeNow: true);
			Downloader.CleanupIncomplete();
			Entries.Clear();
			PlayedHashes.Clear();
			_recordPersisted = false;
			_persistedSessionDate = null;
			Current = null;
			_currentDraw = null;
			ActiveMode = ChallengeMode.Normal;
			_pendingMenuStartMode = ChallengeMode.Normal;
			_pendingFirstBowlFadeGuard = false;
			_pendingMenuStartDownloaded = false;
			_downloadedAnySongInSession = false;
			_favoritesChangedInSession = false;
			_playerMenuQuitPressed = false;
			State = SessionState.Idle;
			ExpectingChainedLaunch = false;
			ExpectingMenuStartLaunch = false;
		}
		catch
		{
		}
	}

	private static bool IsSceneInTransition()
	{
		try
		{
			if (_cachedScenesManager == null)
			{
				_cachedScenesManager = Resources.FindObjectsOfTypeAll<GameScenesManager>().FirstOrDefault();
			}
			return _cachedScenesManager != null && _cachedScenesManager.isInTransition;
		}
		catch
		{
			return false;
		}
	}

	private static IEnumerator ShowSummaryDelayed(SessionSummary summary, FlowCoordinator resultsFc, bool fromGameplay, bool expectResults, int token)
	{
		if (fromGameplay)
		{
			float sceneElapsed = 0f;
			while (sceneElapsed < 5f)
			{
				if (State != SessionState.Ended || token != _summaryToken)
				{
					yield break;
				}
				if (string.Equals(SceneManager.GetActiveScene().name, "GameCore", StringComparison.Ordinal) || IsSceneInTransition())
				{
					yield return null;
					sceneElapsed += Time.unscaledDeltaTime;
					continue;
				}
				break;
			}
			if (expectResults && resultsFc != null)
			{
				float resElapsed = 0f;
				while (resElapsed < 2f && !MenuLauncher.IsResultsPresented(resultsFc))
				{
					if (State != SessionState.Ended || token != _summaryToken)
					{
						yield break;
					}
					yield return null;
					resElapsed += Time.unscaledDeltaTime;
				}
			}
			float settle = (expectResults ? 0.15f : 0.3f);
			float t = 0f;
			while (t < settle)
			{
				if (State != SessionState.Ended || token != _summaryToken)
				{
					yield break;
				}
				yield return null;
				t += Time.unscaledDeltaTime;
			}
		}
		else
		{
			yield return null;
		}
		if (State != SessionState.Ended || token != _summaryToken)
		{
			yield break;
		}
		try
		{
			WankoUI.ShowSummary(summary);
		}
		catch (Exception ex)
		{
			Plugin.Log?.Error("[BeatWanko] ShowSummary failed: " + ex.Message);
		}
		float check = 0f;
		while (check < 0.5f)
		{
			if (State != SessionState.Ended || token != _summaryToken)
			{
				yield break;
			}
			yield return null;
			check += Time.unscaledDeltaTime;
		}
		if (State == SessionState.Ended && token == _summaryToken && !WankoUI.IsSummaryVisible())
		{
			Plugin.Log?.Warn("[BeatWanko] Summary was hidden after menu settle; re-presenting.");
			try
			{
				WankoUI.ShowSummary(summary);
			}
			catch (Exception ex2)
			{
				Plugin.Log?.Error("[BeatWanko] Summary re-present failed: " + ex2.Message);
			}
		}
	}

	private static void CancelInterstitial()
	{
		try
		{
			if (_interstitial != null && Plugin.Runner != null)
			{
				Plugin.Runner.Stop(_interstitial);
			}
		}
		catch
		{
		}
		_interstitial = null;
		CancellationTokenSource downloadCts = _downloadCts;
		_downloadCts = null;
		if (downloadCts != null)
		{
			try
			{
				downloadCts.Cancel();
			}
			catch
			{
			}
			try
			{
				downloadCts.Dispose();
			}
			catch
			{
			}
			Downloader.CleanupIncomplete();
		}
	}

	private static string FormatPanelTitle(ChallengeMode mode, int nextStep)
	{
		return mode switch
		{
			ChallengeMode.Unconditional => "☠️ 無条件ランキング（モディファイアOFF）",
			ChallengeMode.StepUp => (nextStep == 1)
				? $"⭐️ SSステップアップ ★0〜1台 (1/{ChallengeModeExtensions.StepUpMaxStep})"
				: $"⭐️ SSステップアップ ★{nextStep}台 ({nextStep}/{ChallengeModeExtensions.StepUpMaxStep})",
			ChallengeMode.Endurance => "💪 耐久ランキング（NPS≦8 / ポーズ禁止）",
			_ => "🍜 わんこそばチャレンジ",
		};
	}

	private static IEnumerator InterstitialRoutine(LevelLaunchContext ctx)
	{
		int attempts = 0;
		bool resultsWaitDone = false;
		while (State == SessionState.Interstitial)
		{
			attempts++;
			if (attempts > MaxDrawAttempts)
			{
				EndSession(showSummary: true, SessionEndReason.DrawExhausted, "候補曲を用意できなかったため、チャレンジを終了しました");
				yield break;
			}
			int nextStep = ((ActiveMode == ChallengeMode.StepUp) ? (Entries.Count((HistoryEntry e) => e != null && e.Cleared) + 1) : 0);
			DrawCandidate draw = null;
			try
			{
				draw = SongSource.Draw(ActiveMode, PlayedHashes, nextStep);
			}
			catch (Exception arg)
			{
				Plugin.Log?.Error($"[BeatWanko] Draw failed: {arg}");
			}
			if (draw == null)
			{
				EndSession(showSummary: true, SessionEndReason.DrawExhausted, "条件に合う曲が見つかりませんでした");
				yield break;
			}
			if (!string.IsNullOrEmpty(draw.HashUpper))
			{
				PlayedHashes.Add(draw.HashUpper);
			}
			string modeTitle = FormatPanelTitle(ActiveMode, nextStep);
			string stepLabel = ((ActiveMode == ChallengeMode.StepUp) ? ((nextStep == 1) ? "[★0〜1台] " : $"[★{nextStep}台] ") : string.Empty);
			SafeUi(delegate
			{
				WankoUI.ShowPanel();
			});
			SafeUi(delegate
			{
				WankoUI.SetModeTitleText(modeTitle);
			});
			SafeUi(delegate
			{
				WankoUI.SetSongText("次のお椀：" + stepLabel + draw.DisplayName);
			});
			SafeUi(delegate
			{
				WankoUI.SetStatusText("給仕の準備中…");
			});
			SafeUi(delegate
			{
				WankoUI.SetCountText("");
			});
			BeatmapLevel level = draw.OwnedLevel;
			BeatmapKey key = default(BeatmapKey);
			CancellationTokenSource dlCts = null;
			Task<BeatmapLevel> prepTask = null;
			try
			{
				if (level != null)
				{
					if (!SongSource.TryBuildKey(draw, level, out key))
					{
						continue;
					}
				}
				else if (draw.LevelId != null && SongCoreBridge.TryGetCustomLevel(draw.LevelId, out level))
				{
					if (!SongSource.TryBuildKey(draw, level, out key))
					{
						continue;
					}
				}
				else
				{
					if (!SongCoreBridge.Available)
					{
						EndSession(showSummary: true, SessionEndReason.DrawExhausted, "SongCore が見つからないため、新しい曲をダウンロードできませんでした");
						yield break;
					}
					dlCts = new CancellationTokenSource();
					_downloadCts = dlCts;
					prepTask = Downloader.DownloadAndLoadAsync(draw, dlCts.Token);
				}
				if (!resultsWaitDone)
				{
					FlowCoordinator resultsFc = ctx.OriginalLevelFinished?.Target as FlowCoordinator;
					float resultsWaitElapsed = 0f;
					while (!MenuLauncher.IsResultsPresented(resultsFc) && resultsWaitElapsed < 5f)
					{
						if (State != SessionState.Interstitial)
						{
							yield break;
						}
						bool ready = prepTask == null || prepTask.IsCompleted;
						SafeUi(delegate
						{
							WankoUI.SetStatusText(ready ? "準備ができました" : "裏で次の曲をダウンロード中…");
						});
						SafeUi(delegate
						{
							WankoUI.SetCountText("結果画面を表示中…");
						});
						yield return null;
						resultsWaitElapsed += Time.unscaledDeltaTime;
					}
					if (State != SessionState.Interstitial)
					{
						yield break;
					}
					resultsWaitDone = true;
				}
				float interval = Math.Max(0f, PluginConfig.Instance?.IntervalSeconds ?? 5f);
				float elapsed = 0f;
				while (elapsed < interval)
				{
					if (State != SessionState.Interstitial)
					{
						yield break;
					}
					bool ready = prepTask == null || prepTask.IsCompleted;
					SafeUi(delegate
					{
						WankoUI.SetCountText($"あと {Mathf.CeilToInt(interval - elapsed)} 秒で次の曲");
					});
					SafeUi(delegate
					{
						WankoUI.SetStatusText(ready ? "準備ができました" : "裏で次の曲をダウンロード中…");
					});
					yield return null;
					elapsed += Time.unscaledDeltaTime;
				}
				if (prepTask != null)
				{
					float waited = 0f;
					float timeout = Math.Max(1f, PluginConfig.Instance.DownloadTimeoutSeconds);
					bool timedOut = false;
					while (!prepTask.IsCompleted)
					{
						if (State != SessionState.Interstitial)
						{
							yield break;
						}
						float effectiveTimeout = (Downloader.IsWaitingForSongCoreRegistration ? (timeout + 8f) : timeout);
						if (waited >= effectiveTimeout)
						{
							timedOut = true;
							break;
						}
						SafeUi(delegate
						{
							WankoUI.SetStatusText(Downloader.IsWaitingForSongCoreRegistration ? "譜面データを登録中…（SongCore待機中）" : "湯切り中…（ロード待機中）");
						});
						SafeUi(delegate
						{
							WankoUI.SetCountText($"待機 {waited:0} 秒 / 上限 {effectiveTimeout:0} 秒");
						});
						yield return null;
						waited += Time.unscaledDeltaTime;
					}
					if (timedOut)
					{
						Plugin.Log?.Warn($"[BeatWanko] Download timeout ({timeout:0}s) for '{draw.DisplayName}'; cancelling and re-drawing.");
						try
						{
							dlCts?.Cancel();
						}
						catch
						{
						}
						float grace = 0f;
						while (!prepTask.IsCompleted && grace < 2f)
						{
							yield return null;
							grace += Time.unscaledDeltaTime;
						}
						Downloader.CleanupIncomplete();
						if (State != SessionState.Interstitial)
						{
							yield break;
						}
						SafeUi(delegate
						{
							WankoUI.SetStatusText("ダウンロードがタイムアウトしました。別の曲を抽選します…");
						});
						yield return PauseRealtime(1.2f);
						continue;
					}
					if (State != SessionState.Interstitial)
					{
						yield break;
					}
					level = ((prepTask.IsFaulted || prepTask.IsCanceled) ? null : prepTask.Result);
					if (level == null)
					{
						SafeUi(delegate
						{
							WankoUI.SetStatusText("ダウンロードに失敗しました。別の曲を抽選します…");
						});
						yield return PauseRealtime(1.2f);
						continue;
					}
					_downloadedAnySongInSession = true;
					if (!SongSource.TryBuildKey(draw, level, out key))
					{
						continue;
					}
				}
			}
			finally
			{
				DisposeDownloadCts(dlCts, prepTask);
			}
			float scSettle = 0f;
			while (SongCoreBridge.IsLoading() && scSettle < 6f)
			{
				if (State != SessionState.Interstitial)
				{
					yield break;
				}
				SafeUi(delegate
				{
					WankoUI.SetStatusText("SongCore の読み込み完了を待機中…");
				});
				yield return null;
				scSettle += Time.unscaledDeltaTime;
			}
			_currentDraw = draw;
			SafeUi(delegate
			{
				WankoUI.HidePanel();
			});
			State = SessionState.Playing;
			if (!TryStartNext(ctx, level, key))
			{
				EndSession(showSummary: true, SessionEndReason.DrawExhausted, "次の曲を開始できませんでした");
			}
			yield break;
		}
	}

	private static void SafeUi(Action action)
	{
		try
		{
			action();
		}
		catch (Exception ex)
		{
			Plugin.Log?.Warn("[BeatWanko] UI call failed (continuing without panel): " + ex.Message);
		}
	}

	private static void DisposeDownloadCts(CancellationTokenSource cts, Task<BeatmapLevel> prepTask)
	{
		if (cts == null)
		{
			return;
		}
		if (_downloadCts == cts)
		{
			_downloadCts = null;
		}
		if (prepTask != null && !prepTask.IsCompleted)
		{
			try
			{
				cts.Cancel();
			}
			catch
			{
			}
		}
		try
		{
			cts.Dispose();
		}
		catch
		{
		}
	}

	private static IEnumerator PauseRealtime(float seconds)
	{
		float t = 0f;
		while (t < seconds && State == SessionState.Interstitial)
		{
			yield return null;
			t += Time.unscaledDeltaTime;
		}
	}

	private static bool TryStartNext(LevelLaunchContext ctx, BeatmapLevel nextLevel, BeatmapKey nextKey)
	{
		try
		{
			if (ctx.Helper == null || ctx.Method == null || ctx.Args == null)
			{
				return false;
			}
			object[] array = (object[])ctx.Args.Clone();
			array[ctx.BeatmapKeyIndex] = nextKey;
			array[ctx.BeatmapLevelIndex] = nextLevel;
			array[ctx.LevelFinishedIndex] = ctx.OriginalLevelFinished;
			if (ctx.PracticeSettingsIndex >= 0)
			{
				if (ActiveMode.IsRanked())
				{
					array[ctx.PracticeSettingsIndex] = null;
				}
				else if (array[ctx.PracticeSettingsIndex] is PracticeSettings practiceSettings)
				{
					PracticeSettings practiceSettings2 = new PracticeSettings(practiceSettings);
					practiceSettings2.startSongTime = 0f;
					array[ctx.PracticeSettingsIndex] = practiceSettings2;
				}
			}
			ParameterInfo[] parameters = ctx.Method.GetParameters();
			for (int i = 0; i < parameters.Length && i < array.Length; i++)
			{
				if (parameters[i].Name.StartsWith("beforeSceneSwitch", StringComparison.Ordinal) || parameters[i].Name.StartsWith("afterSceneSwitch", StringComparison.Ordinal))
				{
					array[i] = null;
				}
				else if ((string.Equals(parameters[i].Name, "gameplayAdditionalInformation", StringComparison.Ordinal) || string.Equals(parameters[i].ParameterType.Name, "GameplayAdditionalInformation", StringComparison.Ordinal)) && array[i] == null)
				{
					array[i] = MenuLauncher.CreateGameplayAdditionalInformation(parameters[i].ParameterType, "Menu");
				}
			}
			MenuLauncher.DismissResultsIfPresented(ctx.OriginalLevelFinished?.Target as FlowCoordinator);
			if (string.Equals(ctx.GameMode, "Solo", StringComparison.Ordinal))
			{
				BeatLeaderBridge.ArmRecorderForSoloLaunch();
			}
			ExpectingChainedLaunch = true;
			ctx.Method.Invoke(ctx.Helper, array);
			return true;
		}
		catch (Exception ex)
		{
			Exception arg = ((ex is TargetInvocationException && ex.InnerException != null) ? ex.InnerException : ex);
			Plugin.Log?.Error($"[BeatWanko] Failed to start next level: {arg}");
			return false;
		}
		finally
		{
			ExpectingChainedLaunch = false;
		}
	}

	private static void RecordEntry(LevelLaunchContext ctx, StandardLevelScenesTransitionSetupDataSO setupData, LevelCompletionResults results, bool cleared)
	{
		try
		{
			int num = results.goodCutsCount + results.badCutsCount + results.missedCount + results.notGoodCount;
			IReadonlyBeatmapData readonlyBeatmapData = null;
			try
			{
				readonlyBeatmapData = setupData?.transformedBeatmapData;
			}
			catch (Exception ex)
			{
				Plugin.Log?.Warn("[BeatWanko] Failed to read transformedBeatmapData: " + ex.Message);
			}
			int maxMultipliedScore = 0;
			if (readonlyBeatmapData != null)
			{
				maxMultipliedScore = ScoreModel.ComputeMaxMultipliedScoreForBeatmap(readonlyBeatmapData);
			}
			else
			{
				Plugin.Log?.Warn("[BeatWanko] transformedBeatmapData is null; accuracy recorded as 0.0%.");
			}
			int rawScore = Math.Max(0, results.multipliedScore);
			float accuracy = ((maxMultipliedScore > 0 && rawScore > 0) ? (100f * (float)rawScore / (float)maxMultipliedScore) : 0f);
			string levelId = ctx.CurrentLevel?.levelID;
			string hashUpper = ExtractHash(levelId) ?? _currentDraw?.HashUpper ?? string.Empty;
			string characteristic = ctx.CurrentKey.beatmapCharacteristic?.serializedName ?? _currentDraw?.CharacteristicName ?? "Standard";
			HistoryEntry historyEntry = new HistoryEntry
			{
				LevelId = levelId,
				HashUpper = hashUpper,
				SongName = (ctx.CurrentLevel?.songName ?? ctx.CurrentKey.levelId),
				Difficulty = SongSource.DifficultyName((int)ctx.CurrentKey.difficulty),
				Characteristic = characteristic,
				MultipliedScore = rawScore,
				AccuracyPercent = accuracy,
				TotalNotes = num,
				DurationSeconds = results.endSongTime,
				Stars = (_currentDraw?.Stars ?? 0f),
				StepBand = (_currentDraw?.StepBand ?? 0),
				Cleared = cleared,
				Favorite = FavoritesBridge.IsFavorite(levelId)
			};
			Entries.Add(historyEntry);
			Plugin.Log?.Info($"[BeatWanko] Recorded ({ActiveMode}) '{historyEntry.SongName}' [{historyEntry.Characteristic}/{historyEntry.Difficulty}] accuracy {accuracy:0.0}% (score {rawScore}/{maxMultipliedScore}) notes {num} cleared={cleared}. Cups so far: {Entries.Count((HistoryEntry e) => e != null && e.Cleared)}");
		}
		catch (Exception arg)
		{
			Plugin.Log?.Error($"[BeatWanko] RecordEntry failed: {arg}");
		}
	}

	internal static SessionSummary BuildSummary(string notice)
	{
		foreach (HistoryEntry entry in Entries)
		{
			if (entry != null && !string.IsNullOrEmpty(entry.LevelId))
			{
				entry.Favorite = FavoritesBridge.IsFavorite(entry.LevelId);
			}
		}
		return new SessionSummary
		{
			Mode = ActiveMode,
			Cups = Entries.Count((HistoryEntry e) => e != null && e.Cleared),
			TotalScore = Entries.Where((HistoryEntry e) => e != null).Sum((HistoryEntry e) => (long)Math.Max(0, e.MultipliedScore)),
			TotalPlaySeconds = Entries.Sum((HistoryEntry e) => e.DurationSeconds),
			TotalNotes = Entries.Sum((HistoryEntry e) => e.TotalNotes),
			AverageAccuracyPercent = ((Entries.Count > 0) ? Entries.Average((HistoryEntry e) => e.AccuracyPercent) : 0f),
			Entries = new List<HistoryEntry>(Entries),
			Notice = notice,
			BestEntry = (from e in Entries
				where e != null && e.Cleared
				orderby e.AccuracyPercent descending
				select e).FirstOrDefault()
		};
	}

	private static void PersistCurrentSession(SessionEndReason endReason)
	{
		if (Entries.Count > 0 && !_recordPersisted)
		{
			_recordPersisted = true;
			_persistedSessionDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
			HistoryStore.AppendSession(ToRecord(_persistedSessionDate, endReason));
		}
	}

	private static SessionRecord ToRecord(string date, SessionEndReason endReason)
	{
		SessionSummary sessionSummary = BuildSummary(null);
		return new SessionRecord
		{
			Date = (date ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")),
			Mode = ActiveMode.ToApiMode(),
			EndReason = endReason.ToString().ToLowerInvariant(),
			Cups = sessionSummary.Cups,
			TotalScore = sessionSummary.TotalScore,
			TotalPlaySeconds = sessionSummary.TotalPlaySeconds,
			TotalNotes = sessionSummary.TotalNotes,
			AverageAccuracyPercent = sessionSummary.AverageAccuracyPercent,
			Entries = sessionSummary.Entries
		};
	}

	private static string ExtractHash(string levelId)
	{
		try
		{
			if (!string.IsNullOrEmpty(levelId) && levelId.StartsWith("custom_level_", StringComparison.Ordinal))
			{
				return levelId.Substring("custom_level_".Length).ToUpperInvariant();
			}
		}
		catch
		{
		}
		return null;
	}
}
