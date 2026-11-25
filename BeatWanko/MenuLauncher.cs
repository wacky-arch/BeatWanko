using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading;
using BGLib.Polyglot;
using HMUI;
using UnityEngine;

namespace BeatWanko;

internal static class MenuLauncher
{
	public static IEnumerator PrepareSoloFlowForFirstBowlRoutine(CancellationToken token, Action<bool, string> onCompleted)
	{
		float scWait = 0f;
		while (SongCoreBridge.IsLoading() && scWait < 3.0f)
		{
			if (token.IsCancellationRequested)
			{
				onCompleted?.Invoke(false, "cancelled");
				yield break;
			}
			yield return null;
			scWait += Time.unscaledDeltaTime;
		}
		for (int i = 0; i < 3; i++)
		{
			if (token.IsCancellationRequested)
			{
				onCompleted?.Invoke(false, "cancelled");
				yield break;
			}
			yield return null;
		}
		SoloFreePlayFlowCoordinator soloFc = Resources.FindObjectsOfTypeAll<SoloFreePlayFlowCoordinator>().FirstOrDefault();
		if ((UnityEngine.Object)(object)soloFc == null)
		{
			onCompleted?.Invoke(false, "SoloFreePlayFlowCoordinator not found (solo menu not initialised yet)");
			yield break;
		}
		MainFlowCoordinator mainFc = Resources.FindObjectsOfTypeAll<MainFlowCoordinator>().FirstOrDefault();
		if ((UnityEngine.Object)(object)mainFc == null)
		{
			onCompleted?.Invoke(false, "MainFlowCoordinator not found");
			yield break;
		}
		GameScenesManager gsm = Resources.FindObjectsOfTypeAll<GameScenesManager>().FirstOrDefault();
		float preWait = 0f;
		while (((UnityEngine.Object)(object)gsm != null && gsm.isInTransition) || mainFc.isInTransition || soloFc.isInTransition)
		{
			if (preWait >= 3.0f)
			{
				break;
			}
			if (token.IsCancellationRequested)
			{
				onCompleted?.Invoke(false, "cancelled");
				yield break;
			}
			yield return null;
			preWait += Time.unscaledDeltaTime;
		}
		WankoUI.CloseMainFlowIfOpen(immediately: true);
		yield return null;
		if (token.IsCancellationRequested)
		{
			onCompleted?.Invoke(false, "cancelled");
			yield break;
		}
		float dismissWait = 0f;
		while (mainFc.isInTransition && dismissWait < 1.0f)
		{
			if (token.IsCancellationRequested)
			{
				onCompleted?.Invoke(false, "cancelled");
				yield break;
			}
			yield return null;
			dismissWait += Time.unscaledDeltaTime;
		}
		FlowCoordinator child = GetChildFlowCoordinator(mainFc);
		if ((UnityEngine.Object)(object)child != null && (object)child != soloFc)
		{
			if (!TryDismissChildFlow(mainFc, child, out string dismissErr))
			{
				onCompleted?.Invoke(false, "dismissing active child flow failed: " + dismissErr);
				yield break;
			}
			yield return null;
			if (token.IsCancellationRequested)
			{
				onCompleted?.Invoke(false, "cancelled");
				yield break;
			}
		}
		child = GetChildFlowCoordinator(mainFc);
		if ((object)child != soloFc || !soloFc.isActivated)
		{
			float waitMain = 0f;
			while (mainFc.isInTransition && waitMain < 1.0f)
			{
				if (token.IsCancellationRequested)
				{
					onCompleted?.Invoke(false, "cancelled");
					yield break;
				}
				yield return null;
				waitMain += Time.unscaledDeltaTime;
			}
			if ((object)GetChildFlowCoordinator(mainFc) != soloFc)
			{
				if (!TryPresentSoloFlow(mainFc, soloFc, immediately: false, out string presentErr))
				{
					onCompleted?.Invoke(false, "presenting the solo flow failed: " + presentErr);
					yield break;
				}
			}
			float presentWait = 0f;
			while ((mainFc.isInTransition || soloFc.isInTransition || !soloFc.isActivated) && presentWait < 2.5f)
			{
				if (token.IsCancellationRequested)
				{
					onCompleted?.Invoke(false, "cancelled");
					yield break;
				}
				yield return null;
				presentWait += Time.unscaledDeltaTime;
			}
			TryForceUpdateCanvases();
			for (int i = 0; i < 6; i++)
			{
				if (token.IsCancellationRequested)
				{
					onCompleted?.Invoke(false, "cancelled");
					yield break;
				}
				yield return null;
			}
			float postScWait = 0f;
			while (SongCoreBridge.IsLoading() && postScWait < 0.5f)
			{
				if (token.IsCancellationRequested)
				{
					onCompleted?.Invoke(false, "cancelled");
					yield break;
				}
				yield return null;
				postScWait += Time.unscaledDeltaTime;
			}
		}
		if (IsResultsPresented(soloFc))
		{
			DismissResultsIfPresented(soloFc);
			yield return null;
			yield return null;
			if (token.IsCancellationRequested)
			{
				onCompleted?.Invoke(false, "cancelled");
				yield break;
			}
		}
		TryStopSongPreview();
		float finalWait = 0f;
		while (((UnityEngine.Object)(object)gsm != null && gsm.isInTransition) || mainFc.isInTransition)
		{
			if (finalWait >= 1.5f)
			{
				break;
			}
			if (token.IsCancellationRequested)
			{
				onCompleted?.Invoke(false, "cancelled");
				yield break;
			}
			yield return null;
			finalWait += Time.unscaledDeltaTime;
		}
		onCompleted?.Invoke(true, null);
	}

	public static bool LaunchPreparedSoloFirstBowl(BeatmapLevel level, BeatmapKey key, out string error)
	{
		error = null;
		try
		{
			if (level == null || !key.IsValid())
			{
				error = "level/key is not valid";
				return false;
			}
			SoloFreePlayFlowCoordinator soloFreePlayFlowCoordinator = Resources.FindObjectsOfTypeAll<SoloFreePlayFlowCoordinator>().FirstOrDefault();
			if ((UnityEngine.Object)(object)soloFreePlayFlowCoordinator == null)
			{
				error = "SoloFreePlayFlowCoordinator not found (solo menu not initialised yet)";
				return false;
			}
			DismissResultsIfPresented(soloFreePlayFlowCoordinator);
			TryStopSongPreview();
			return InvokeSoloStandardLevel(level, key, soloFreePlayFlowCoordinator, out error);
		}
		catch (Exception ex)
		{
			Exception ex2 = ((ex is TargetInvocationException && ex.InnerException != null) ? ex.InnerException : ex);
			error = ex2.Message;
			Plugin.Log?.Error($"[BeatWanko] Prepared Menu START launch failed: {ex2}");
			return false;
		}
	}

	public static bool TryLaunchSoloFirstBowl(BeatmapLevel level, BeatmapKey key, out string error)
	{
		error = null;
		try
		{
			if (level == null || !key.IsValid())
			{
				error = "level/key is not valid";
				return false;
			}
			SoloFreePlayFlowCoordinator soloFreePlayFlowCoordinator = Resources.FindObjectsOfTypeAll<SoloFreePlayFlowCoordinator>().FirstOrDefault();
			if ((UnityEngine.Object)(object)soloFreePlayFlowCoordinator == null)
			{
				error = "SoloFreePlayFlowCoordinator not found (solo menu not initialised yet)";
				return false;
			}
			MainFlowCoordinator mainFlowCoordinator = Resources.FindObjectsOfTypeAll<MainFlowCoordinator>().FirstOrDefault();
			if ((UnityEngine.Object)(object)mainFlowCoordinator == null)
			{
				error = "MainFlowCoordinator not found";
				return false;
			}
			try
			{
				WankoUI.CloseMainFlowIfOpen();
				FlowCoordinator childFlowCoordinator = GetChildFlowCoordinator(mainFlowCoordinator);
				if (childFlowCoordinator != null && (object)childFlowCoordinator != soloFreePlayFlowCoordinator)
				{
					InvokeFlowMethod(mainFlowCoordinator, "DismissFlowCoordinator", childFlowCoordinator, ViewController.AnimationDirection.Horizontal, true);
				}
				if ((object)childFlowCoordinator != soloFreePlayFlowCoordinator)
				{
					InvokeFlowMethod(mainFlowCoordinator, "PresentFlowCoordinator", soloFreePlayFlowCoordinator, ViewController.AnimationDirection.Horizontal, true);
				}
			}
			catch (Exception ex)
			{
				error = "presenting the solo flow failed: " + ex.Message;
				return false;
			}
			DismissResultsIfPresented(soloFreePlayFlowCoordinator);
			TryStopSongPreview();
			return InvokeSoloStandardLevel(level, key, soloFreePlayFlowCoordinator, out error);
		}
		catch (Exception ex2)
		{
			Exception ex3 = ((ex2 is TargetInvocationException && ex2.InnerException != null) ? ex2.InnerException : ex2);
			error = ex3.Message;
			Plugin.Log?.Error($"[BeatWanko] Menu START launch failed: {ex3}");
			return false;
		}
	}

	private static bool InvokeSoloStandardLevel(BeatmapLevel level, BeatmapKey key, SoloFreePlayFlowCoordinator soloFreePlayFlowCoordinator, out string error)
	{
		error = null;
		MenuTransitionsHelper menuTransitionsHelper = Resources.FindObjectsOfTypeAll<MenuTransitionsHelper>().FirstOrDefault();
		if (menuTransitionsHelper == null)
		{
			error = "MenuTransitionsHelper not found";
			return false;
		}
		EnvironmentsListModel environmentsListModel = GetEnvironmentsListModel(soloFreePlayFlowCoordinator, menuTransitionsHelper);
		if (environmentsListModel == null)
		{
			error = "EnvironmentsListModel not available on the solo coordinator";
			return false;
		}
		PlayerData playerData = Resources.FindObjectsOfTypeAll<PlayerDataModel>().FirstOrDefault()?.playerData;
		if (playerData == null)
		{
			error = "PlayerData not available";
			return false;
		}
		MethodInfo finishMethod = FindMethodInHierarchy(soloFreePlayFlowCoordinator.GetType(), "HandleStandardLevelDidFinish");
		if (finishMethod == null)
		{
			error = "SinglePlayerLevelSelectionFlowCoordinator.HandleStandardLevelDidFinish not found";
			return false;
		}
		MethodInfo startMethod = SelectStartStandardLevelMethod();
		if (startMethod == null)
		{
			error = "MenuTransitionsHelper.StartStandardLevel overload not found";
			return false;
		}
		ParameterInfo callbackParam = startMethod.GetParameters().FirstOrDefault((ParameterInfo p) => p.Name == "levelFinishedCallback");
		Type callbackType = callbackParam?.ParameterType ?? typeof(Action<StandardLevelScenesTransitionSetupDataSO, LevelCompletionResults>);
		Delegate levelFinishedCallback = Delegate.CreateDelegate(callbackType, soloFreePlayFlowCoordinator, finishMethod, throwOnBindFailure: false);
		if (levelFinishedCallback == null)
		{
			error = "Failed to bind HandleStandardLevelDidFinish delegate";
			return false;
		}
		OverrideEnvironmentSettings overrideEnvironmentSettings = new OverrideEnvironmentSettings();
		overrideEnvironmentSettings.overrideEnvironments = false;
		ColorSchemesSettings colorSchemesSettings = playerData.colorSchemesSettings;
		ColorScheme playerOverrideColorScheme = InvokeNoArgMethod<ColorScheme>(colorSchemesSettings, "GetOverrideColorScheme");
		bool playerOverrideLightshowColors = InvokeBoolNoArgMethod(colorSchemesSettings, "ShouldOverrideLightshowColors", fallback: false);
		ColorScheme colorScheme = GetLevelColorSchemeSafe(level, key);
		bool rankedMode = Session.GetEffectiveModeForLaunch(expectingMenuStart: true, expectingChained: false).IsRanked();
		GameplayModifiers gameplayModifiers = (rankedMode ? StartLevelPatch.CreateNoModifiers() : playerData.gameplayModifiers);
		PlayerSpecificSettings playerSpecificSettings = playerData.playerSpecificSettings;
		string backButtonText = "Menu";
		try
		{
			string text = Localization.Get("BUTTON_MENU");
			if (!string.IsNullOrEmpty(text))
			{
				backButtonText = text;
			}
		}
		catch
		{
		}
		Plugin.Log?.Info("[BeatWanko] Menu START launch attempt: '" + level.songName + "' [" + SongSource.DifficultyName((int)key.difficulty) + " / " + key.beatmapCharacteristic?.serializedName + "] via MenuTransitionsHelper.StartStandardLevel (solo flow).");
		BeatLeaderBridge.ArmRecorderForSoloLaunch();
		Session.ArmFirstBowlFadeGuard();
		InvokeStartStandardLevelDynamic(menuTransitionsHelper, startMethod, key, level, overrideEnvironmentSettings, playerOverrideColorScheme, playerOverrideLightshowColors, colorScheme, gameplayModifiers, playerSpecificSettings, environmentsListModel, backButtonText, levelFinishedCallback);
		Plugin.Log?.Info("[BeatWanko] Menu START launch invoked for bowl #1: '" + level.songName + "'.");
		return true;
	}

	private static bool TryDismissChildFlow(MainFlowCoordinator mainFc, FlowCoordinator child, out string error)
	{
		error = null;
		try
		{
			InvokeFlowMethod(mainFc, "DismissFlowCoordinator", child, ViewController.AnimationDirection.Horizontal, true);
			return true;
		}
		catch (Exception ex)
		{
			error = ex.Message;
			return false;
		}
	}

	private static bool TryPresentSoloFlow(MainFlowCoordinator mainFc, SoloFreePlayFlowCoordinator soloFc, bool immediately, out string error)
	{
		error = null;
		try
		{
			InvokeFlowMethod(mainFc, "PresentFlowCoordinator", soloFc, ViewController.AnimationDirection.Horizontal, immediately);
			return true;
		}
		catch (Exception ex)
		{
			error = ex.Message;
			return false;
		}
	}

	private static void TryForceUpdateCanvases()
	{
		try
		{
			Canvas.ForceUpdateCanvases();
		}
		catch
		{
		}
	}

	internal static void TryStopSongPreview()
	{
		try
		{
			SongPreviewPlayer songPreviewPlayer = Resources.FindObjectsOfTypeAll<SongPreviewPlayer>().FirstOrDefault((SongPreviewPlayer p) => (UnityEngine.Object)(object)p != null && ((Behaviour)(object)p).isActiveAndEnabled);
			songPreviewPlayer?.CrossfadeToDefault();
		}
		catch
		{
		}
	}

	private static EnvironmentsListModel GetEnvironmentsListModel(SoloFreePlayFlowCoordinator solo, MenuTransitionsHelper helper)
	{
		if (solo != null)
		{
			FieldInfo field = FindFieldInHierarchy(solo.GetType(), "_environmentsListModel");
			if (field != null && field.GetValue(solo) is EnvironmentsListModel model)
			{
				return model;
			}
			for (Type t = solo.GetType(); t != null; t = t.BaseType)
			{
				foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
				{
					if (typeof(EnvironmentsListModel).IsAssignableFrom(f.FieldType) && f.GetValue(solo) is EnvironmentsListModel m)
					{
						return m;
					}
				}
			}
		}
		if (helper != null)
		{
			for (Type t = helper.GetType(); t != null; t = t.BaseType)
			{
				foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
				{
					if (typeof(EnvironmentsListModel).IsAssignableFrom(f.FieldType) && f.GetValue(helper) is EnvironmentsListModel m)
					{
						return m;
					}
				}
			}
		}
		return null;
	}

	private static FieldInfo FindFieldInHierarchy(Type type, string fieldName)
	{
		for (Type t = type; t != null; t = t.BaseType)
		{
			FieldInfo field = t.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
			if (field != null)
			{
				return field;
			}
		}
		return null;
	}

	private static MethodInfo FindMethodInHierarchy(Type type, string methodName)
	{
		for (Type t = type; t != null; t = t.BaseType)
		{
			MethodInfo method = t.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
			if (method != null)
			{
				return method;
			}
		}
		return null;
	}

	private static MethodInfo SelectStartStandardLevelMethod()
	{
		MethodInfo[] candidates = (from m in typeof(MenuTransitionsHelper).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
			where m.Name == "StartStandardLevel"
			where m.GetParameters().Any((ParameterInfo p) => p.Name == "levelFinishedCallback")
			select m).ToArray();
		if (candidates.Length == 0)
		{
			return null;
		}
		return candidates
			.OrderBy((MethodInfo m) => m.GetParameters().Any((ParameterInfo p) => p.Name == "beatmapLevelData") ? 1 : 0)
			.ThenBy((MethodInfo m) => m.GetParameters().Length)
			.FirstOrDefault();
	}

	private static void InvokeStartStandardLevelDynamic(MenuTransitionsHelper helper, MethodInfo method, BeatmapKey beatmapKey, BeatmapLevel level, OverrideEnvironmentSettings overrideEnvironmentSettings, ColorScheme playerOverrideColorScheme, bool playerOverrideLightshowColors, ColorScheme beatmapOverrideColorScheme, GameplayModifiers gameplayModifiers, PlayerSpecificSettings playerSpecificSettings, EnvironmentsListModel environmentsListModel, string backButtonText, Delegate levelFinishedCallback)
	{
		ParameterInfo[] parameters = method.GetParameters();
		object[] args = new object[parameters.Length];
		for (int i = 0; i < parameters.Length; i++)
		{
			ParameterInfo p = parameters[i];
			Type elemType = (p.ParameterType.IsByRef ? p.ParameterType.GetElementType() : p.ParameterType);
			string name = p.Name ?? string.Empty;
			if (string.Equals(name, "gameMode", StringComparison.Ordinal))
			{
				args[i] = "Solo";
			}
			else if (string.Equals(name, "beatmapKey", StringComparison.Ordinal) || elemType == typeof(BeatmapKey))
			{
				args[i] = beatmapKey;
			}
			else if (string.Equals(name, "beatmapLevel", StringComparison.Ordinal) || elemType == typeof(BeatmapLevel))
			{
				args[i] = level;
			}
			else if (string.Equals(name, "overrideEnvironmentSettings", StringComparison.Ordinal) || elemType == typeof(OverrideEnvironmentSettings))
			{
				args[i] = overrideEnvironmentSettings;
			}
			else if (string.Equals(name, "playerOverrideColorScheme", StringComparison.Ordinal))
			{
				args[i] = playerOverrideColorScheme;
			}
			else if (string.Equals(name, "playerOverrideLightshowColors", StringComparison.Ordinal))
			{
				args[i] = playerOverrideLightshowColors;
			}
			else if (string.Equals(name, "beatmapOverrideColorScheme", StringComparison.Ordinal) || string.Equals(name, "colorScheme", StringComparison.Ordinal))
			{
				args[i] = beatmapOverrideColorScheme;
			}
			else if (string.Equals(name, "gameplayModifiers", StringComparison.Ordinal) || elemType == typeof(GameplayModifiers))
			{
				args[i] = gameplayModifiers;
			}
			else if (string.Equals(name, "playerSpecificSettings", StringComparison.Ordinal) || elemType == typeof(PlayerSpecificSettings))
			{
				args[i] = playerSpecificSettings;
			}
			else if (string.Equals(name, "environmentsListModel", StringComparison.Ordinal) || elemType == typeof(EnvironmentsListModel))
			{
				args[i] = environmentsListModel;
			}
			else if (string.Equals(name, "backButtonText", StringComparison.Ordinal))
			{
				args[i] = backButtonText;
			}
			else if (string.Equals(name, "gameplayAdditionalInformation", StringComparison.Ordinal) || string.Equals(elemType?.Name, "GameplayAdditionalInformation", StringComparison.Ordinal))
			{
				args[i] = CreateGameplayAdditionalInformation(elemType, backButtonText);
			}
			else if (string.Equals(name, "useTestNoteCutSoundEffects", StringComparison.Ordinal) || string.Equals(name, "startPaused", StringComparison.Ordinal))
			{
				args[i] = false;
			}
			else if (string.Equals(name, "levelFinishedCallback", StringComparison.Ordinal))
			{
				args[i] = levelFinishedCallback;
			}
			else
			{
				args[i] = GetDefaultArg(p, elemType);
			}
		}
		method.Invoke(helper, args);
	}

	internal static object CreateGameplayAdditionalInformation(Type infoType, string backButtonText)
	{
		if (infoType == null)
		{
			return null;
		}
		string normalizedBackText = (string.IsNullOrEmpty(backButtonText) ? "Menu" : backButtonText);
		try
		{
			ConstructorInfo[] ctors = (from c in infoType.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
				orderby c.GetParameters().Length descending
				select c).ToArray();
			foreach (ConstructorInfo ctor in ctors)
			{
				ParameterInfo[] ps = ctor.GetParameters();
				object[] ctorArgs = new object[ps.Length];
				bool usedFirstString = false;
				for (int i = 0; i < ps.Length; i++)
				{
					ParameterInfo cp = ps[i];
					string cpName = cp.Name ?? string.Empty;
					if (string.Equals(cpName, "backButtonText", StringComparison.Ordinal) || (!usedFirstString && cp.ParameterType == typeof(string) && !string.Equals(cpName, "recordingRelativePath", StringComparison.Ordinal)))
					{
						ctorArgs[i] = normalizedBackText;
						usedFirstString = true;
					}
					else if (string.Equals(cpName, "useTestNoteCutSoundEffects", StringComparison.Ordinal) || string.Equals(cpName, "startPaused", StringComparison.Ordinal))
					{
						ctorArgs[i] = false;
					}
					else if (string.Equals(cpName, "recordingRelativePath", StringComparison.Ordinal))
					{
						ctorArgs[i] = null;
					}
					else
					{
						ctorArgs[i] = GetDefaultArg(cp, cp.ParameterType);
					}
				}
				return ctor.Invoke(ctorArgs);
			}
		}
		catch (Exception ex)
		{
			Plugin.Log?.Warn("[BeatWanko] CreateGameplayAdditionalInformation failed: " + ex.Message);
		}
		return null;
	}

	private static object GetDefaultArg(ParameterInfo p, Type elemType)
	{
		if (p.HasDefaultValue)
		{
			object def = p.DefaultValue;
			if (def != DBNull.Value && def != Type.Missing)
			{
				return def;
			}
		}
		if (elemType != null && elemType.IsValueType && Nullable.GetUnderlyingType(elemType) == null)
		{
			return Activator.CreateInstance(elemType);
		}
		return null;
	}

	private static T InvokeNoArgMethod<T>(object target, string methodName) where T : class
	{
		if (target == null)
		{
			return null;
		}
		try
		{
			MethodInfo[] methods = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			foreach (MethodInfo m in methods)
			{
				if (m.Name != methodName)
				{
					continue;
				}
				ParameterInfo[] ps = m.GetParameters();
				object[] args = new object[ps.Length];
				bool ok = true;
				for (int i = 0; i < ps.Length; i++)
				{
					if (!ps[i].HasDefaultValue)
					{
						ok = false;
						break;
					}
					args[i] = GetDefaultArg(ps[i], ps[i].ParameterType);
				}
				if (ok)
				{
					return m.Invoke(target, args) as T;
				}
			}
		}
		catch
		{
		}
		return null;
	}

	private static bool InvokeBoolNoArgMethod(object target, string methodName, bool fallback)
	{
		if (target == null)
		{
			return fallback;
		}
		try
		{
			MethodInfo[] methods = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			foreach (MethodInfo m in methods)
			{
				if (m.Name != methodName)
				{
					continue;
				}
				ParameterInfo[] ps = m.GetParameters();
				if (ps.Length == 0 && m.Invoke(target, null) is bool b)
				{
					return b;
				}
			}
		}
		catch
		{
		}
		return fallback;
	}

	private static ColorScheme GetLevelColorSchemeSafe(BeatmapLevel level, BeatmapKey key)
	{
		if (level == null)
		{
			return null;
		}
		try
		{
			MethodInfo[] methods = level.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			foreach (MethodInfo m in methods)
			{
				if (m.Name != "GetColorScheme")
				{
					continue;
				}
				ParameterInfo[] ps = m.GetParameters();
				object[] args = new object[ps.Length];
				bool ok = true;
				for (int i = 0; i < ps.Length; i++)
				{
					Type t = (ps[i].ParameterType.IsByRef ? ps[i].ParameterType.GetElementType() : ps[i].ParameterType);
					if (t == typeof(BeatmapCharacteristicSO))
					{
						args[i] = key.beatmapCharacteristic;
					}
					else if (t == typeof(BeatmapDifficulty))
					{
						args[i] = key.difficulty;
					}
					else if (t == typeof(BeatmapKey))
					{
						args[i] = key;
					}
					else if (ps[i].HasDefaultValue)
					{
						args[i] = GetDefaultArg(ps[i], t);
					}
					else
					{
						ok = false;
						break;
					}
				}
				if (ok)
				{
					return m.Invoke(level, args) as ColorScheme;
				}
			}
		}
		catch
		{
		}
		return null;
	}

	private static void InvokeFlowMethod(MainFlowCoordinator mainFc, string methodName, FlowCoordinator targetFc, ViewController.AnimationDirection direction, bool immediately)
	{
		MethodInfo method = (from m in typeof(FlowCoordinator).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
			where m.Name == methodName
			orderby m.GetParameters().Length
			select m).FirstOrDefault();
		if (method == null)
		{
			throw new MissingMethodException("FlowCoordinator", methodName);
		}
		ParameterInfo[] ps = method.GetParameters();
		object[] args = new object[ps.Length];
		for (int i = 0; i < ps.Length; i++)
		{
			ParameterInfo p = ps[i];
			string name = p.Name ?? string.Empty;
			if (p.ParameterType == typeof(FlowCoordinator))
			{
				args[i] = targetFc;
			}
			else if (p.ParameterType == typeof(ViewController.AnimationDirection))
			{
				args[i] = direction;
			}
			else if (p.ParameterType == typeof(bool) && (string.Equals(name, "immediately", StringComparison.Ordinal) || i <= 3))
			{
				args[i] = immediately;
			}
			else
			{
				args[i] = GetDefaultArg(p, p.ParameterType);
			}
		}
		method.Invoke(mainFc, args);
	}

	private static FlowCoordinator GetChildFlowCoordinator(FlowCoordinator parent)
	{
		if (parent == null)
		{
			return null;
		}
		PropertyInfo property = typeof(FlowCoordinator).GetProperty("childFlowCoordinator", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (property != null)
		{
			return property.GetValue(parent) as FlowCoordinator;
		}
		return typeof(FlowCoordinator).GetField("_childFlowCoordinator", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(parent) as FlowCoordinator;
	}

	internal static bool IsResultsPresented(FlowCoordinator fc)
	{
		if (fc == null)
		{
			return false;
		}
		try
		{
			ViewController viewController = typeof(FlowCoordinator).GetProperty("topViewController", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(fc) as ViewController;
			return (UnityEngine.Object)(object)viewController != null && string.Equals(((object)viewController).GetType().Name, "ResultsViewController", StringComparison.Ordinal);
		}
		catch
		{
			return false;
		}
	}

	internal static void DismissResultsIfPresented(FlowCoordinator fc)
	{
		if (fc == null)
		{
			return;
		}
		try
		{
			ViewController viewController = typeof(FlowCoordinator).GetProperty("topViewController", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(fc) as ViewController;
			if (!((UnityEngine.Object)(object)viewController == null) && string.Equals(((object)viewController).GetType().Name, "ResultsViewController", StringComparison.Ordinal))
			{
				MethodInfo method = typeof(FlowCoordinator).GetMethod("DismissViewController", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[4]
				{
					typeof(ViewController),
					typeof(ViewController.AnimationDirection),
					typeof(Action),
					typeof(bool)
				}, null);
				if (method != null)
				{
					method.Invoke(fc, new object[4]
					{
						viewController,
						ViewController.AnimationDirection.Horizontal,
						null,
						true
					});
					Plugin.Log?.Info("[BeatWanko] Dismissed ResultsViewController before starting next level.");
				}
			}
		}
		catch (Exception ex)
		{
			Plugin.Log?.Warn("[BeatWanko] Dismissing ResultsViewController failed: " + ex.Message);
		}
	}
}
