using System;
using BeatSaberMarkupLanguage.Util;
using HarmonyLib;
using IPA;
using IPA.Config;
using IPA.Config.Stores;
using IPA.Logging;
using UnityEngine;

namespace BeatWanko;

[Plugin(RuntimeOptions.DynamicInit)]
public class Plugin
{
	public const string HarmonyId = "com.wacky.beatwanko";

	private Harmony _harmony;

	private GameObject _runnerObject;

	internal static Plugin Instance { get; private set; }

	internal static IPA.Logging.Logger Log { get; private set; }

	internal static BeatWankoRunner Runner { get; private set; }

	[Init]
	public void Init(IPA.Logging.Logger logger, Config config)
	{
		Instance = this;
		Log = logger;
		try
		{
			PluginConfig.Instance = config.Generated<PluginConfig>();
		}
		catch (Exception ex)
		{
			Log?.Error("[BeatWanko] Failed to load config, using defaults: " + ex.Message);
			PluginConfig.Instance = new PluginConfig();
		}
	}

	[OnEnable]
	public void OnEnable()
	{
		try
		{
			_runnerObject = new GameObject("BeatWankoRunner");
			UnityEngine.Object.DontDestroyOnLoad(_runnerObject);
			Runner = _runnerObject.AddComponent<BeatWankoRunner>();
			_harmony = new Harmony("com.wacky.beatwanko");
			int num = StartLevelPatch.ApplyPatches(_harmony);
			int num2 = PausePatch.ApplyPatches(_harmony);
			if (num > 0)
			{
				Log?.Info($"BeatWanko enabled (build v1.1.7, ranking spec). MenuTransitionsHelper patches applied: {num}, PauseMenuManager/PauseController patches applied: {num2}.");
			}
			else
			{
				Log?.Error("[BeatWanko] No MenuTransitionsHelper method could be patched; BeatWanko is inactive.");
			}
			SongDetailsBridge.KickInit();
			PlayerIdentity.KickResolve();
			try
			{
				MainMenuAwaiter.MainMenuInitializing += OnMainMenuInitializing;
			}
			catch (Exception ex)
			{
				Log?.Warn("[BeatWanko] MainMenuAwaiter hook failed: " + ex.Message);
			}
			WankoUI.RegisterMenuButton();
		}
		catch (Exception arg)
		{
			Log?.Error($"[BeatWanko] Error while enabling: {arg}");
		}
	}

	private static void OnMainMenuInitializing()
	{
		WankoUI.RegisterMenuButton();
		PlayerIdentity.KickResolve();
	}

	[OnDisable]
	public void OnDisable()
	{
		try
		{
			try
			{
				MainMenuAwaiter.MainMenuInitializing -= OnMainMenuInitializing;
			}
			catch
			{
			}
			Session.ResetAll();
			_harmony?.UnpatchSelf();
			WankoUI.UnregisterMenuButton();
			WankoUI.HideAll();
			if (_runnerObject != null)
			{
				UnityEngine.Object.Destroy(_runnerObject);
			}
			Runner = null;
			Log?.Info("BeatWanko disabled.");
		}
		catch (Exception ex)
		{
			Log?.Error("[BeatWanko] Error while disabling: " + ex.Message);
		}
	}
}
