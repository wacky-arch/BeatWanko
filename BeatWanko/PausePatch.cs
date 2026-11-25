using System;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BeatWanko;

internal static class PausePatch
{
	private const string ButtonObjectName = "BeatWankoCloseLidButton";

	private const string ButtonLabel = "蓋を閉じる";

	private const string RewinderControllerObjectName = "NalulunaRewinderController";

	private const string RewinderScreenObjectName = "NalulunaRewinderFloatingScreen";

	private static GameObject _hiddenRewinderScreen;

	private static float _lastPauseBlockLogTime = -10f;

	private static readonly FieldInfo BackButtonField = typeof(PauseMenuManager).GetField("_backButton", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

	private static readonly FieldInfo RestartButtonField = typeof(PauseMenuManager).GetField("_restartButton", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

	private static readonly MethodInfo MenuButtonPressedMethod = typeof(PauseMenuManager).GetMethod("MenuButtonPressed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);

	public static int ApplyPatches(Harmony harmony)
	{
		int num = 0;
		try
		{
			MethodInfo method = typeof(PauseMenuManager).GetMethod("ShowMenu", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
			if (method == null)
			{
				Plugin.Log?.Warn("[BeatWanko] PauseMenuManager.ShowMenu not found; mid-song close-lid button disabled.");
				return 0;
			}
			harmony.Patch(method, null, new HarmonyMethod(typeof(PausePatch), "ShowMenuPostfix"));
			num++;
			MethodInfo method2 = typeof(PauseController).GetMethod("Pause", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
			if (method2 != null)
			{
				harmony.Patch(method2, new HarmonyMethod(typeof(PausePatch), "PausePrefix"), new HarmonyMethod(typeof(PausePatch), "PausePostfix"));
				num++;
			}
			num += PatchPauseExit(harmony, "StartResumeAnimation");
			num += PatchPauseExit(harmony, "MenuButtonPressed");
			num += PatchPauseExit(harmony, "RestartButtonPressed");
			if (MenuButtonPressedMethod != null)
			{
				harmony.Patch(MenuButtonPressedMethod, new HarmonyMethod(typeof(PausePatch), "OnMenuButtonPressedPrefix"));
				num++;
			}
		}
		catch (Exception arg)
		{
			Plugin.Log?.Error($"[BeatWanko] Failed to patch PauseMenuManager: {arg}");
		}
		return num;
	}

	private static void OnMenuButtonPressedPrefix()
	{
		try
		{
			if (Session.State == SessionState.Playing)
			{
				Session.NotifyPlayerMenuQuitPressed();
			}
		}
		catch
		{
		}
	}

	private static int PatchPauseExit(Harmony harmony, string methodName)
	{
		try
		{
			MethodInfo method = typeof(PauseMenuManager).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
			if (method == null)
			{
				return 0;
			}
			harmony.Patch(method, null, new HarmonyMethod(typeof(PausePatch), "OnPauseMenuClosingPostfix"));
			return 1;
		}
		catch (Exception ex)
		{
			Plugin.Log?.Warn("[BeatWanko] Optional pause-exit patch " + methodName + " failed: " + ex.Message);
			return 0;
		}
	}

	private static bool PausePrefix()
	{
		try
		{
			if (Session.IsPauseBlocked)
			{
				if (Time.realtimeSinceStartup - _lastPauseBlockLogTime > 5f)
				{
					_lastPauseBlockLogTime = Time.realtimeSinceStartup;
					Plugin.Log?.Info("[BeatWanko] Pause blocked in 耐久💪ランキング mode.");
				}
				return false;
			}
		}
		catch
		{
		}
		return true;
	}

	private static void ShowMenuPostfix(PauseMenuManager __instance)
	{
		try
		{
			if (__instance == null || BackButtonField == null)
			{
				return;
			}
			Button restartButton = RestartButtonField?.GetValue(__instance) as Button;
			Button button = BackButtonField.GetValue(__instance) as Button;
			if (button == null || button.transform == null || button.transform.parent == null)
			{
				return;
			}
			Transform parent = button.transform.parent;
			Transform transform = parent.Find("BeatWankoCloseLidButton");
			if (Session.State != SessionState.Playing)
			{
				if (restartButton != null && !restartButton.interactable)
				{
					restartButton.interactable = true;
				}
				if (transform != null && transform.gameObject.activeSelf)
				{
					transform.gameObject.SetActive(value: false);
				}
				RestoreHiddenRewinderIfNeeded();
				return;
			}
			if (restartButton != null)
			{
				restartButton.interactable = !Session.ActiveMode.IsRanked();
			}
			if (transform == null)
			{
				transform = CreateCloseLidButton(__instance, button, parent);
				if (transform == null)
				{
					return;
				}
			}
			SetButtonLabel(transform.gameObject, "蓋を閉じる");
			Button component = transform.GetComponent<Button>();
			if (component != null)
			{
				component.interactable = true;
			}
			if (!transform.gameObject.activeSelf)
			{
				transform.gameObject.SetActive(value: true);
			}
			SuppressRewinderIfActive();
		}
		catch (Exception arg)
		{
			Plugin.Log?.Error($"[BeatWanko] PauseMenuManager.ShowMenu postfix failed: {arg}");
		}
	}

	private static void PausePostfix()
	{
		try
		{
			if (Session.State == SessionState.Playing)
			{
				SuppressRewinderIfActive();
			}
			else
			{
				RestoreHiddenRewinderIfNeeded();
			}
		}
		catch (Exception arg)
		{
			Plugin.Log?.Error($"[BeatWanko] PauseController.Pause postfix failed: {arg}");
		}
	}

	private static void OnPauseMenuClosingPostfix()
	{
		try
		{
			_hiddenRewinderScreen = null;
		}
		catch (Exception ex)
		{
			Plugin.Log?.Warn("[BeatWanko] OnPauseMenuClosing postfix failed: " + ex.Message);
		}
	}

	private static GameObject FindRewinderScreenObject()
	{
		GameObject gameObject = GameObject.Find("NalulunaRewinderController");
		if (gameObject != null)
		{
			Transform transform = gameObject.transform.Find("NalulunaRewinderFloatingScreen");
			if (transform != null)
			{
				return transform.gameObject;
			}
		}
		return GameObject.Find("NalulunaRewinderFloatingScreen");
	}

	private static void SuppressRewinderIfActive()
	{
		try
		{
			GameObject gameObject = FindRewinderScreenObject();
			if (gameObject != null && gameObject.activeSelf)
			{
				_hiddenRewinderScreen = gameObject;
				gameObject.SetActive(value: false);
				Plugin.Log?.Info("[BeatWanko] Hid NalulunaRewinderFloatingScreen during BeatWanko session pause.");
			}
		}
		catch (Exception ex)
		{
			Plugin.Log?.Warn("[BeatWanko] Failed to hide NalulunaRewinderFloatingScreen: " + ex.Message);
		}
	}

	private static void RestoreHiddenRewinderIfNeeded()
	{
		try
		{
			if (!(_hiddenRewinderScreen == null))
			{
				if (!_hiddenRewinderScreen.activeSelf)
				{
					_hiddenRewinderScreen.SetActive(value: true);
					Plugin.Log?.Info("[BeatWanko] Restored NalulunaRewinderFloatingScreen outside BeatWanko session.");
				}
				_hiddenRewinderScreen = null;
			}
		}
		catch (Exception ex)
		{
			Plugin.Log?.Warn("[BeatWanko] Failed to restore NalulunaRewinderFloatingScreen: " + ex.Message);
			_hiddenRewinderScreen = null;
		}
	}

	private static Transform CreateCloseLidButton(PauseMenuManager pauseMenu, Button backButton, Transform parent)
	{
		try
		{
			GameObject gameObject = UnityEngine.Object.Instantiate(backButton.gameObject, parent);
			gameObject.name = "BeatWankoCloseLidButton";
			MonoBehaviour[] componentsInChildren = gameObject.GetComponentsInChildren<MonoBehaviour>(includeInactive: true);
			foreach (MonoBehaviour monoBehaviour in componentsInChildren)
			{
				if (!(monoBehaviour == null) && (monoBehaviour.GetType().FullName ?? string.Empty).IndexOf("LocalizedText", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					UnityEngine.Object.Destroy(monoBehaviour);
				}
			}
			SetButtonLabel(gameObject, "蓋を閉じる");
			if (parent.GetComponent<LayoutGroup>() != null)
			{
				gameObject.transform.SetAsLastSibling();
			}
			else
			{
				RectTransform component = gameObject.GetComponent<RectTransform>();
				RectTransform component2 = backButton.GetComponent<RectTransform>();
				if (component != null && component2 != null)
				{
					float num = ((component2.rect.height > 0f) ? component2.rect.height : 10f);
					component.anchoredPosition = component2.anchoredPosition + new Vector2(0f, 0f - (num + 8f));
				}
			}
			Button component3 = gameObject.GetComponent<Button>();
			if (component3 != null)
			{
				component3.onClick = new Button.ButtonClickedEvent();
				component3.onClick.AddListener(delegate
				{
					OnCloseLidClicked(pauseMenu);
				});
			}
			return gameObject.transform;
		}
		catch (Exception arg)
		{
			Plugin.Log?.Error($"[BeatWanko] Failed to create pause close-lid button: {arg}");
			return null;
		}
	}

	private static void SetButtonLabel(GameObject buttonGo, string label)
	{
		TMP_Text[] componentsInChildren = buttonGo.GetComponentsInChildren<TMP_Text>(includeInactive: true);
		for (int i = 0; i < componentsInChildren.Length; i++)
		{
			if (componentsInChildren[i] != null)
			{
				componentsInChildren[i].text = label;
			}
		}
	}

	private static void OnCloseLidClicked(PauseMenuManager pauseMenu)
	{
		try
		{
			if (Session.State == SessionState.Playing && (!(pauseMenu != null) || pauseMenu.enabled))
			{
				Plugin.Log?.Info("[BeatWanko] 蓋を閉じる pressed on pause menu.");
				Session.NotifyPlayerMenuQuitPressed();
				if (pauseMenu != null && MenuButtonPressedMethod != null)
				{
					MenuButtonPressedMethod.Invoke(pauseMenu, null);
					return;
				}
				Plugin.Log?.Warn("[BeatWanko] MenuButtonPressed unavailable; ending session directly.");
				Session.RequestCloseLid();
			}
		}
		catch (Exception arg)
		{
			Plugin.Log?.Error($"[BeatWanko] Pause close-lid click failed: {arg}");
			try
			{
				Session.RequestCloseLid();
			}
			catch
			{
			}
		}
	}
}
