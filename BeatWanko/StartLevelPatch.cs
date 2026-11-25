using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BeatWanko;

internal static class StartLevelPatch
{
	private static readonly FieldInfo FadeEaseValueField = typeof(FadeInOutController).GetField("_easeValue", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

	private static readonly PropertyInfo FadeInTransitionProp = typeof(FadeInOutController).GetProperty("inTransition", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

	private static bool _floatSOProbeDone;

	private static PropertyInfo _floatSOValueProp;

	private static FieldInfo _floatSOValueField;

	public static int ApplyPatches(Harmony harmony)
	{
		int num = 0;
		try
		{
			MethodInfo[] array = (from m in typeof(MenuTransitionsHelper).GetMethods(BindingFlags.Instance | BindingFlags.Public)
				where m.Name == "StartStandardLevel"
				where m.GetParameters().Any((ParameterInfo p) => p.Name == "levelFinishedCallback" && p.ParameterType == typeof(Action<StandardLevelScenesTransitionSetupDataSO, LevelCompletionResults>))
				select m).ToArray();
			HarmonyMethod prefix = new HarmonyMethod(typeof(StartLevelPatch), "Prefix");
			MethodInfo[] array2 = array;
			foreach (MethodInfo original in array2)
			{
				harmony.Patch(original, prefix);
				num++;
			}
			MethodInfo method = typeof(MenuTransitionsHelper).GetMethod("HandleMainGameSceneDidFinish", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[2]
			{
				typeof(StandardLevelScenesTransitionSetupDataSO),
				typeof(LevelCompletionResults)
			}, null);
			if (method != null)
			{
				harmony.Patch(method, null, new HarmonyMethod(typeof(StartLevelPatch), "FinishPostfix"));
				num++;
			}
			else
			{
				Plugin.Log?.Error("[BeatWanko] HandleMainGameSceneDidFinish not found; finish detection is unavailable.");
			}
			MethodInfo gameplayStartMethod = typeof(StandardLevelGameplayManager).GetMethod("Start", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null)
				?? typeof(StandardLevelGameplayManager).GetMethod("Start", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (gameplayStartMethod != null)
			{
				harmony.Patch(gameplayStartMethod, null, new HarmonyMethod(typeof(StartLevelPatch), "GameplayStartPostfix"));
				num++;
			}
			else
			{
				Plugin.Log?.Warn("[BeatWanko] StandardLevelGameplayManager.Start not found; first-bowl fade guard disabled.");
			}
		}
		catch (Exception arg)
		{
			Plugin.Log?.Error($"[BeatWanko] Failed to apply patches: {arg}");
		}
		return num;
	}

	private static void GameplayStartPostfix(StandardLevelGameplayManager __instance, ref IEnumerator __result)
	{
		try
		{
			if (__result == null || !Session.ConsumeFirstBowlFadeGuard())
			{
				return;
			}
			__result = WrapFirstBowlGameplayStart(__instance, __result);
		}
		catch (Exception ex)
		{
			Plugin.Log?.Warn("[BeatWanko] GameplayStartPostfix failed: " + ex.Message);
		}
	}

	private static IEnumerator WrapFirstBowlGameplayStart(StandardLevelGameplayManager manager, IEnumerator original)
	{
		if (original == null)
		{
			yield break;
		}
		if (!original.MoveNext())
		{
			yield break;
		}
		yield return original.Current;
		GameScenesManager gsm = Resources.FindObjectsOfTypeAll<GameScenesManager>().FirstOrDefault();
		float gsmWait = 0f;
		while ((UnityEngine.Object)(object)gsm != null && gsm.isInTransition && gsmWait < 3.0f)
		{
			yield return null;
			gsmWait += Time.unscaledDeltaTime;
		}
		yield return null;
		yield return null;
		yield return null;
		float waited = 0f;
		FadeInOutController fade = FindActiveFadeInOutController();
		if ((UnityEngine.Object)(object)fade != null)
		{
			while (waited < 2.2f)
			{
				bool fading = IsFadeInTransition(fade);
				float ease = TryReadFadeEaseValue(fade);
				if ((!fading && ease >= 0.95f) || (waited >= 0.15f && !fading && ease < 0f))
				{
					break;
				}
				yield return null;
				waited += Time.unscaledDeltaTime;
			}
		}
		else
		{
			while (waited < 0.45f)
			{
				yield return null;
				waited += Time.unscaledDeltaTime;
			}
		}
		yield return null;
		yield return null;
		Plugin.Log?.Info($"[BeatWanko] First-bowl fade-in settled ({waited:0.00}s); starting song.");
		while (original.MoveNext())
		{
			yield return original.Current;
		}
	}

	private static FadeInOutController FindActiveFadeInOutController()
	{
		try
		{
			FadeInOutController[] array = Resources.FindObjectsOfTypeAll<FadeInOutController>();
			FadeInOutController fallback = null;
			foreach (FadeInOutController fadeInOutController in array)
			{
				if ((UnityEngine.Object)(object)fadeInOutController == null || fadeInOutController.gameObject == null)
				{
					continue;
				}
				if (((Behaviour)(object)fadeInOutController).isActiveAndEnabled && fadeInOutController.gameObject.scene.IsValid())
				{
					return fadeInOutController;
				}
				if (fallback == null)
				{
					fallback = fadeInOutController;
				}
			}
			return fallback;
		}
		catch
		{
			return null;
		}
	}

	private static bool IsFadeInTransition(FadeInOutController fade)
	{
		try
		{
			if ((UnityEngine.Object)(object)fade != null && FadeInTransitionProp != null && FadeInTransitionProp.GetValue(fade) is bool b)
			{
				return b;
			}
		}
		catch
		{
		}
		return false;
	}

	private static float TryReadFadeEaseValue(FadeInOutController fade)
	{
		try
		{
			if ((UnityEngine.Object)(object)fade == null || FadeEaseValueField == null)
			{
				return -1f;
			}
			object easeObj = FadeEaseValueField.GetValue(fade);
			if (easeObj == null)
			{
				return -1f;
			}
			if (!_floatSOProbeDone)
			{
				_floatSOProbeDone = true;
				Type type = easeObj.GetType();
				_floatSOValueProp = type.GetProperty("value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				_floatSOValueField = type.GetField("_value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
					?? type.GetField("value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			}
			if (_floatSOValueProp != null && _floatSOValueProp.GetValue(easeObj) is float pv)
			{
				return pv;
			}
			if (_floatSOValueField != null && _floatSOValueField.GetValue(easeObj) is float fv)
			{
				return fv;
			}
		}
		catch
		{
		}
		return -1f;
	}

	private static void FinishPostfix(StandardLevelScenesTransitionSetupDataSO standardLevelScenesTransitionSetupData, LevelCompletionResults levelCompletionResults)
	{
		try
		{
			Session.OnGameSceneFinished(standardLevelScenesTransitionSetupData, levelCompletionResults);
		}
		catch (Exception arg)
		{
			Plugin.Log?.Error($"[BeatWanko] FinishPostfix failed: {arg}");
		}
	}

	private static void Prefix(MenuTransitionsHelper __instance, MethodBase __originalMethod, object[] __args)
	{
		bool expectingChainedLaunch = Session.ExpectingChainedLaunch;
		bool expectingMenuStartLaunch = Session.ExpectingMenuStartLaunch;
		try
		{
			bool flag = Session.State == SessionState.Playing || Session.State == SessionState.Interstitial;
			if ((!expectingChainedLaunch && !expectingMenuStartLaunch && !flag) || __args == null || __originalMethod == null)
			{
				return;
			}
			ParameterInfo[] parameters = __originalMethod.GetParameters();
			int num = IndexOfParameter(parameters, "beatmapKey");
			int num2 = IndexOfParameter(parameters, "beatmapLevel");
			int num3 = IndexOfParameter(parameters, "levelFinishedCallback");
			int practiceSettingsIndex = IndexOfParameter(parameters, "practiceSettings");
			int num4 = IndexOfParameter(parameters, "gameMode");
			if (num < 0 || num2 < 0 || num3 < 0)
			{
				return;
			}
			string text = ((num4 >= 0) ? (__args[num4] as string) : null);
			if ((!(text != "Solo") || !(text != "Party")) && __args[num] is BeatmapKey currentKey && currentKey.IsValid() && __args[num2] is BeatmapLevel currentLevel && __args[num3] is Action<StandardLevelScenesTransitionSetupDataSO, LevelCompletionResults> originalLevelFinished)
			{
				ChallengeMode effectiveMode = Session.GetEffectiveModeForLaunch(expectingMenuStartLaunch, expectingChainedLaunch);
				int num5 = IndexOfParameter(parameters, "gameplayModifiers");
				if (effectiveMode.IsRanked())
				{
					if (num5 >= 0 && num5 < __args.Length)
					{
						__args[num5] = CreateNoModifiers();
					}
					if (practiceSettingsIndex >= 0 && practiceSettingsIndex < __args.Length)
					{
						__args[practiceSettingsIndex] = null;
					}
					Plugin.Log?.Info($"[BeatWanko] All modifiers forced OFF for ranked mode launch ({effectiveMode}).");
				}
				else if (num5 >= 0 && num5 < __args.Length && __args[num5] is GameplayModifiers gameplayModifiers && IsNoFailEnabled(gameplayModifiers))
				{
					GameplayModifiers updated = DisableNoFailViaReflection(gameplayModifiers);
					if (updated != null)
					{
						__args[num5] = updated;
						Plugin.Log?.Info("[BeatWanko] NoFail disabled for this session launch (player setting untouched).");
					}
				}
				LevelLaunchContext ctx = new LevelLaunchContext
				{
					Helper = __instance,
					Method = (__originalMethod as MethodInfo),
					Args = (object[])__args.Clone(),
					BeatmapKeyIndex = num,
					BeatmapLevelIndex = num2,
					PracticeSettingsIndex = practiceSettingsIndex,
					LevelFinishedIndex = num3,
					OriginalLevelFinished = originalLevelFinished,
					CurrentKey = currentKey,
					CurrentLevel = currentLevel,
					GameMode = text
				};
				if (expectingChainedLaunch)
				{
					Session.OnChainedLaunch(ctx);
				}
				else
				{
					Session.OnManualLaunch(ctx);
				}
			}
		}
		catch (Exception arg)
		{
			Plugin.Log?.Error($"[BeatWanko] StartStandardLevel prefix failed: {arg}");
		}
		finally
		{
			Session.ExpectingChainedLaunch = false;
			Session.ExpectingMenuStartLaunch = false;
		}
	}

	private static int IndexOfParameter(ParameterInfo[] parameters, string name)
	{
		for (int i = 0; i < parameters.Length; i++)
		{
			if (parameters[i].Name == name)
			{
				return i;
			}
		}
		return -1;
	}

	private static bool IsNoFailEnabled(GameplayModifiers gm)
	{
		if (gm == null)
		{
			return false;
		}
		try
		{
			PropertyInfo prop = typeof(GameplayModifiers).GetProperty("noFailOn0Energy", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (prop != null && prop.GetValue(gm) is bool pb)
			{
				return pb;
			}
			FieldInfo field = typeof(GameplayModifiers).GetField("_noFailOn0Energy", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
				?? typeof(GameplayModifiers).GetField("noFailOn0Energy", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (field != null && field.GetValue(gm) is bool fb)
			{
				return fb;
			}
		}
		catch
		{
		}
		return false;
	}

	private static GameplayModifiers DisableNoFailViaReflection(GameplayModifiers gm)
	{
		if (gm == null)
		{
			return null;
		}
		try
		{
			MethodInfo copyWith = typeof(GameplayModifiers)
				.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
				.Where((MethodInfo m) => m.Name == "CopyWith")
				.FirstOrDefault((MethodInfo m) => m.GetParameters().Any((ParameterInfo p) => string.Equals(p.Name, "noFailOn0Energy", StringComparison.OrdinalIgnoreCase)));
			if (copyWith != null)
			{
				ParameterInfo[] ps = copyWith.GetParameters();
				object[] args = new object[ps.Length];
				for (int i = 0; i < ps.Length; i++)
				{
					ParameterInfo p = ps[i];
					if (string.Equals(p.Name, "noFailOn0Energy", StringComparison.OrdinalIgnoreCase))
					{
						args[i] = false;
					}
					else if (p.HasDefaultValue)
					{
						object def = p.DefaultValue;
						args[i] = ((def != DBNull.Value && def != Type.Missing) ? def : null);
					}
					else
					{
						Type t = p.ParameterType;
						args[i] = ((t.IsValueType && Nullable.GetUnderlyingType(t) == null) ? Activator.CreateInstance(t) : null);
					}
				}
				if (copyWith.Invoke(gm, args) is GameplayModifiers result)
				{
					return result;
				}
			}
		}
		catch (Exception ex)
		{
			Plugin.Log?.Warn("[BeatWanko] GameplayModifiers.CopyWith reflection failed: " + ex.Message);
		}
		return null;
	}

	internal static GameplayModifiers CreateNoModifiers()
	{
		try
		{
			FieldInfo field = typeof(GameplayModifiers).GetField("noModifiers", BindingFlags.Static | BindingFlags.Public);
			if (field != null && field.GetValue(null) is GameplayModifiers noMods)
			{
				return noMods;
			}
		}
		catch
		{
		}
		return new GameplayModifiers();
	}
}
