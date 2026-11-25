using System;
using System.Collections.Generic;
using System.Linq;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Parser;
using HMUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BeatWanko;

internal sealed class SettingsHost
{
	public static readonly SettingsHost Instance = new SettingsHost();

	internal BSMLParserParams ParserParams;

	internal void FlushPendingSettings()
	{
		try
		{
			ParserParams?.EmitEvent("apply");
		}
		catch
		{
		}
	}

	[UIObject("start-status-row")]
	public GameObject StartStatusRow;

	[UIComponent("start-status")]
	public TextMeshProUGUI StartStatusText;

	[UIAction("#post-parse")]
	public void OnPostParse()
	{
		try
		{
			bool hasStatus = StartStatusText != null && !string.IsNullOrEmpty(StartStatusText.text);
			if (StartStatusRow != null)
			{
				StartStatusRow.SetActive(hasStatus);
			}
			RefreshScrollLayout();
		}
		catch
		{
		}
	}

	internal void RefreshScrollLayout()
	{
		try
		{
			RectTransform rectTransform = ((StartStatusRow != null) ? StartStatusRow.transform.parent : StartStatusText?.transform?.parent?.parent) as RectTransform;
			if ((UnityEngine.Object)(object)rectTransform == null)
			{
				return;
			}
			LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);
			if (rectTransform.parent is RectTransform parentRect)
			{
				LayoutRebuilder.ForceRebuildLayoutImmediate(parentRect);
			}
			ScrollView scrollView = rectTransform.GetComponentInParent<ScrollView>();
			if ((UnityEngine.Object)(object)scrollView != null)
			{
				float prevPos = scrollView.position;
				scrollView.UpdateContentSize();
				if (prevPos > 0.01f)
				{
					scrollView.ScrollTo(prevPos, animated: false);
				}
			}
		}
		catch
		{
		}
	}

	private static readonly string[] DifficultyNames = new string[5] { "Easy", "Normal", "Hard", "Expert", "ExpertPlus" };

	private static PluginConfig Cfg => PluginConfig.Instance ?? new PluginConfig();

	[UIValue("difficulty-options")]
	public List<object> DifficultyOptions => DifficultyNames.Cast<object>().ToList();

	[UIValue("enable-ranking-submission")]
	public bool EnableRankingSubmission
	{
		get
		{
			return Cfg.EnableRankingSubmission;
		}
		set
		{
			Cfg.EnableRankingSubmission = value;
		}
	}

	[UIValue("continue-on-fail")]
	public bool ContinueOnFail
	{
		get
		{
			return Cfg.ContinueOnFail;
		}
		set
		{
			Cfg.ContinueOnFail = value;
		}
	}

	[UIValue("interval-seconds")]
	public float IntervalSeconds
	{
		get
		{
			return Cfg.IntervalSeconds;
		}
		set
		{
			Cfg.IntervalSeconds = value;
		}
	}

	[UIValue("download-timeout")]
	public float DownloadTimeoutSeconds
	{
		get
		{
			return Cfg.DownloadTimeoutSeconds;
		}
		set
		{
			Cfg.DownloadTimeoutSeconds = value;
		}
	}

	[UIValue("min-difficulty")]
	public object MinDifficulty
	{
		get
		{
			return DifficultyNames[Mathf.Clamp(Cfg.MinDifficulty, 0, 4)];
		}
		set
		{
			int idx = Array.IndexOf(DifficultyNames, value as string);
			if (idx >= 0)
			{
				Cfg.MinDifficulty = idx;
			}
		}
	}

	[UIValue("max-difficulty")]
	public object MaxDifficulty
	{
		get
		{
			return DifficultyNames[Mathf.Clamp(Cfg.MaxDifficulty, 0, 4)];
		}
		set
		{
			int idx = Array.IndexOf(DifficultyNames, value as string);
			if (idx >= 0)
			{
				Cfg.MaxDifficulty = idx;
			}
		}
	}

	[UIValue("min-stars")]
	public float MinStars
	{
		get
		{
			return Cfg.MinStars;
		}
		set
		{
			Cfg.MinStars = value;
		}
	}

	[UIValue("max-stars")]
	public float MaxStars
	{
		get
		{
			return Cfg.MaxStars;
		}
		set
		{
			Cfg.MaxStars = value;
		}
	}

	[UIValue("min-nps")]
	public float MinNps
	{
		get
		{
			return Cfg.MinNps;
		}
		set
		{
			Cfg.MinNps = value;
		}
	}

	[UIValue("max-nps")]
	public float MaxNps
	{
		get
		{
			return Cfg.MaxNps;
		}
		set
		{
			Cfg.MaxNps = value;
		}
	}

	[UIValue("min-length")]
	public float MinLengthSeconds
	{
		get
		{
			return Cfg.MinLengthSeconds;
		}
		set
		{
			Cfg.MinLengthSeconds = Mathf.RoundToInt(value);
		}
	}

	[UIValue("max-length")]
	public float MaxLengthSeconds
	{
		get
		{
			return Cfg.MaxLengthSeconds;
		}
		set
		{
			Cfg.MaxLengthSeconds = Mathf.RoundToInt(value);
		}
	}

	[UIAction("start-challenge")]
	public void OnStartChallenge()
	{
		FlushPendingSettings();
		Session.StartChallengeFromMenu(ChallengeMode.Normal);
	}

	[UIAction("start-normal")]
	public void OnStartNormal()
	{
		FlushPendingSettings();
		Session.StartChallengeFromMenu(ChallengeMode.Normal);
	}

	[UIAction("start-unconditional")]
	public void OnStartUnconditional()
	{
		FlushPendingSettings();
		Session.StartChallengeFromMenu(ChallengeMode.Unconditional);
	}

	[UIAction("start-stepup")]
	public void OnStartStepUp()
	{
		FlushPendingSettings();
		Session.StartChallengeFromMenu(ChallengeMode.StepUp);
	}

	[UIAction("start-endurance")]
	public void OnStartEndurance()
	{
		FlushPendingSettings();
		Session.StartChallengeFromMenu(ChallengeMode.Endurance);
	}

	public const string DefaultLeaderboardWebUrl = "https://beatwanko.pages.dev/";

	[UIAction("open-ranking-web")]
	public void OnOpenRankingWeb()
	{
		FlushPendingSettings();
		string url = (Cfg.LeaderboardWebUrl ?? string.Empty).Trim();
		if (string.IsNullOrEmpty(url) || (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)))
		{
			url = DefaultLeaderboardWebUrl;
		}
		try
		{
			Application.OpenURL(url);
			Plugin.Log?.Info("[BeatWanko] Opened ranking web URL: " + url);
			WankoUI.SetStartStatusText("PCのブラウザでWebランキングを開きました");
		}
		catch (Exception ex)
		{
			Plugin.Log?.Warn("[BeatWanko] Failed to open ranking web URL: " + ex.Message);
			WankoUI.SetStartStatusText("ブラウザを開けませんでした");
		}
	}
}
