using System;
using System.Reflection;
using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.FloatingScreen;
using BeatSaberMarkupLanguage.MenuButtons;
using BeatSaberMarkupLanguage.Util;
using HMUI;
using UnityEngine;

namespace BeatWanko;

internal static class WankoUI
{
	private static FloatingScreen _screen;

	private static ViewController _panelVC;

	private static ViewController _summaryVC;

	private static PanelHost _panelHost;

	private static SummaryHost _summaryHost;

	private static MenuButton _menuButton;

	private static MenuButtons _registeredButtonsInstance;

	private static WankoFlowCoordinator _mainFlowCoordinator;

	public static void RegisterMenuButton()
	{
		try
		{
			MenuButtons instance = ZenjectSingleton<MenuButtons>.Instance;
			if (instance != null && _registeredButtonsInstance != instance)
			{
				if (_menuButton == null)
				{
					_menuButton = new MenuButton("BeatWanko\ud83c\udf5c", "BeatWanko わんこそばチャレンジ設定・開始", OpenMainFlow);
				}
				instance.RegisterButton(_menuButton);
				_registeredButtonsInstance = instance;
				Plugin.Log?.Info("[BeatWanko] Left-panel menu button 'BeatWanko\ud83c\udf5c' registered.");
			}
		}
		catch (Exception ex)
		{
			Plugin.Log?.Warn("[BeatWanko] Menu button registration deferred: " + ex.Message);
		}
	}

	public static void UnregisterMenuButton()
	{
		try
		{
			CloseMainFlowIfOpen();
			if (_menuButton != null)
			{
				ZenjectSingleton<MenuButtons>.Instance?.UnregisterButton(_menuButton);
			}
		}
		catch
		{
		}
		finally
		{
			_registeredButtonsInstance = null;
			_menuButton = null;
		}
	}

	public static void OpenMainFlow()
	{
		try
		{
			MainFlowCoordinator mainFlowCoordinator = BeatSaberUI.MainFlowCoordinator;
			if (mainFlowCoordinator == null || mainFlowCoordinator.isInTransition)
			{
				return;
			}
			if (_mainFlowCoordinator == null)
			{
				_mainFlowCoordinator = BeatSaberUI.CreateFlowCoordinator<WankoFlowCoordinator>();
			}
			if (!(_mainFlowCoordinator == null) && !_mainFlowCoordinator.isActivated)
			{
				FlowCoordinator childFlowCoordinator = mainFlowCoordinator.childFlowCoordinator;
				if (childFlowCoordinator != null && (object)childFlowCoordinator != _mainFlowCoordinator)
				{
					mainFlowCoordinator.DismissFlowCoordinator(childFlowCoordinator, ViewController.AnimationDirection.Horizontal, null, immediately: true);
				}
				mainFlowCoordinator.PresentFlowCoordinator(_mainFlowCoordinator);
			}
		}
		catch (Exception arg)
		{
			Plugin.Log?.Error($"[BeatWanko] OpenMainFlow failed: {arg}");
		}
	}

	public static void CloseMainFlowIfOpen(bool immediately = true)
	{
		try
		{
			MainFlowCoordinator mainFlowCoordinator = BeatSaberUI.MainFlowCoordinator;
			if (_mainFlowCoordinator != null && mainFlowCoordinator != null && _mainFlowCoordinator.isActivated)
			{
				mainFlowCoordinator.DismissFlowCoordinator(_mainFlowCoordinator, ViewController.AnimationDirection.Horizontal, null, immediately);
			}
		}
		catch (Exception ex)
		{
			Plugin.Log?.Warn("[BeatWanko] CloseMainFlowIfOpen failed: " + ex.Message);
		}
	}

	private static void DestroyScreenObjects()
	{
		try
		{
			if ((UnityEngine.Object)(object)_panelVC != null && ((Component)(object)_panelVC).gameObject != null)
			{
				UnityEngine.Object.Destroy(((Component)(object)_panelVC).gameObject);
			}
		}
		catch
		{
		}
		try
		{
			if ((UnityEngine.Object)(object)_summaryVC != null && ((Component)(object)_summaryVC).gameObject != null)
			{
				UnityEngine.Object.Destroy(((Component)(object)_summaryVC).gameObject);
			}
		}
		catch
		{
		}
		try
		{
			if ((UnityEngine.Object)(object)_screen != null && ((Component)(object)_screen).gameObject != null)
			{
				UnityEngine.Object.Destroy(((Component)(object)_screen).gameObject);
			}
		}
		catch
		{
		}
		_screen = null;
		_panelVC = null;
		_summaryVC = null;
		_panelHost = null;
		_summaryHost = null;
	}

	private static bool EnsureScreen()
	{
		try
		{
			if ((UnityEngine.Object)(object)_screen != null && (UnityEngine.Object)(object)_panelVC != null && (UnityEngine.Object)(object)_summaryVC != null)
			{
				return true;
			}
			DestroyScreenObjects();
			_screen = FloatingScreen.CreateFloatingScreen(new Vector2(118f, 74f), createHandle: false, new Vector3(0f, 1.35f, 2.5f), Quaternion.identity, 0f, hasBackground: true);
			((UnityEngine.Object)(object)_screen).name = "BeatWankoScreen";
			UnityEngine.Object.DontDestroyOnLoad(((Component)(object)_screen).gameObject);
			_panelHost = new PanelHost();
			_panelVC = BeatSaberUI.CreateViewController<ViewController>();
			((Component)(object)_panelVC).transform.SetParent(((Component)(object)_screen).transform, worldPositionStays: false);
			ZenjectSingleton<BSMLParser>.Instance.Parse(GetResource("BeatWanko.UI.panel.bsml"), ((Component)(object)_panelVC).gameObject, _panelHost);
			_summaryHost = new SummaryHost();
			_summaryVC = BeatSaberUI.CreateViewController<ViewController>();
			((Component)(object)_summaryVC).transform.SetParent(((Component)(object)_screen).transform, worldPositionStays: false);
			ZenjectSingleton<BSMLParser>.Instance.Parse(GetResource("BeatWanko.UI.summary.bsml"), ((Component)(object)_summaryVC).gameObject, _summaryHost);
			return true;
		}
		catch (Exception arg)
		{
			Plugin.Log?.Error($"[BeatWanko] Failed to create the floating screen: {arg}");
			DestroyScreenObjects();
			return false;
		}
	}

	internal static string GetResource(string name)
	{
		return Utilities.GetResourceContent(Assembly.GetExecutingAssembly(), name);
	}

	private static void EnsureScreenActiveBeforeRootSwitch()
	{
		if (((Component)(object)_screen).transform.parent != null)
		{
			((Component)(object)_screen).transform.SetParent(null, worldPositionStays: true);
		}
		if (!((Component)(object)_screen).gameObject.activeSelf)
		{
			((Component)(object)_screen).gameObject.SetActive(value: true);
		}
	}

	public static void ShowPanel()
	{
		try
		{
			if (EnsureScreen())
			{
				EnsureScreenActiveBeforeRootSwitch();
				_screen.SetRootViewController(_panelVC, ViewController.AnimationType.None);
				if (!((Component)(object)_panelVC).gameObject.activeSelf)
				{
					((Component)(object)_panelVC).gameObject.SetActive(value: true);
				}
				Plugin.Log?.Info("[BeatWanko] Interval panel shown.");
			}
		}
		catch (Exception ex)
		{
			Plugin.Log?.Error("[BeatWanko] ShowPanel failed: " + ex.Message);
		}
	}

	public static void HidePanel()
	{
		try
		{
			if ((UnityEngine.Object)(object)_screen != null)
			{
				((Component)(object)_screen).gameObject.SetActive(value: false);
			}
		}
		catch
		{
		}
	}

	public static void ShowSummary(SessionSummary summary)
	{
		try
		{
			if (EnsureScreen() && summary != null)
			{
				EnsureScreenActiveBeforeRootSwitch();
				_screen.SetRootViewController(_summaryVC, ViewController.AnimationType.None);
				if (!((Component)(object)_summaryVC).gameObject.activeSelf)
				{
					((Component)(object)_summaryVC).gameObject.SetActive(value: true);
				}
				_summaryHost.Apply(summary);
				_summaryHost.ScheduleScrollRefresh();
				Plugin.Log?.Info("[BeatWanko] Summary shown.");
			}
		}
		catch (Exception ex)
		{
			Plugin.Log?.Error("[BeatWanko] ShowSummary failed: " + ex.Message);
		}
	}

	public static bool IsSummaryVisible()
	{
		try
		{
			return (UnityEngine.Object)(object)_screen != null && ((Component)(object)_screen).gameObject.activeInHierarchy && (UnityEngine.Object)(object)_summaryVC != null && ((Component)(object)_summaryVC).gameObject.activeInHierarchy;
		}
		catch
		{
			return false;
		}
	}

	public static void ResetForNewSession()
	{
		try
		{
			SetModeTitleText("🍜 わんこそばチャレンジ");
			SetStatusText(string.Empty);
			SetSongText(string.Empty);
			SetCountText(string.Empty);
			SetSummaryNote(string.Empty);
			SetStartStatusText(string.Empty);
			if ((UnityEngine.Object)(object)_screen != null && (UnityEngine.Object)(object)_panelVC != null)
			{
				EnsureScreenActiveBeforeRootSwitch();
				_screen.SetRootViewController(_panelVC, ViewController.AnimationType.None);
				((Component)(object)_screen).gameObject.SetActive(value: false);
			}
			else
			{
				HideAll();
			}
		}
		catch (Exception ex)
		{
			Plugin.Log?.Warn("[BeatWanko] ResetForNewSession failed: " + ex.Message);
			HideAll();
		}
	}

	public static void RefreshSummaryHistory(SessionSummary summary)
	{
		try
		{
			if (_summaryHost != null && summary != null)
			{
				_summaryHost.ApplyHistory(summary);
			}
		}
		catch (Exception ex)
		{
			Plugin.Log?.Warn("[BeatWanko] RefreshSummaryHistory failed: " + ex.Message);
		}
	}

	public static void HideAll()
	{
		HidePanel();
	}

	public static void SetModeTitleText(string text)
	{
		try
		{
			if (_panelHost?.ModeTitleText != null)
			{
				_panelHost.ModeTitleText.text = text ?? string.Empty;
			}
		}
		catch
		{
		}
	}

	public static void SetStatusText(string text)
	{
		try
		{
			if (_panelHost?.StatusText != null)
			{
				_panelHost.StatusText.text = text ?? string.Empty;
			}
		}
		catch
		{
		}
	}

	public static void SetSongText(string text)
	{
		try
		{
			if (_panelHost?.SongText != null)
			{
				_panelHost.SongText.text = text ?? string.Empty;
			}
		}
		catch
		{
		}
	}

	public static void SetCountText(string text)
	{
		try
		{
			if (_panelHost?.CountText != null)
			{
				_panelHost.CountText.text = text ?? string.Empty;
			}
		}
		catch
		{
		}
	}

	public static void SetSummaryNote(string text)
	{
		try
		{
			if (_summaryHost?.NoteText != null)
			{
				_summaryHost.NoteText.text = text ?? string.Empty;
			}
		}
		catch
		{
		}
	}

	public static void SetStartStatusText(string text)
	{
		try
		{
			if (SettingsHost.Instance?.StartStatusText != null)
			{
				string normalized = text ?? string.Empty;
				SettingsHost.Instance.StartStatusText.text = normalized;
				if (SettingsHost.Instance.StartStatusRow != null)
				{
					SettingsHost.Instance.StartStatusRow.SetActive(!string.IsNullOrEmpty(normalized));
				}
				SettingsHost.Instance.RefreshScrollLayout();
			}
		}
		catch
		{
		}
	}
}
