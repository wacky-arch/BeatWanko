using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Util;
using HMUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BeatWanko;

internal sealed class SummaryHost
{
	private sealed class SummaryRowHost
	{
		public int EntryIndex;

		[UIComponent("row-root")]
		public HorizontalLayoutGroup Root;

		[UIComponent("row-label")]
		public TextMeshProUGUI Label;

		[UIComponent("row-fav")]
		public Button FavButton;

		[UIAction("toggle-fav")]
		public void OnToggleFav()
		{
			Session.ToggleEntryFavorite(EntryIndex);
		}
	}

	private const string RowTemplateBsml =
		"<horizontal id=\"row-root\" pref-width=\"96\" min-height=\"5.4\" pref-height=\"5.4\" spacing=\"1.5\" child-control-height=\"true\" child-expand-height=\"false\" child-control-width=\"true\" child-expand-width=\"false\" child-align=\"MiddleLeft\">" +
		"<text id=\"row-label\" text=\"\" font-size=\"3.1\" align=\"MidlineLeft\" overflow-mode=\"Ellipsis\" word-wrapping=\"false\" />" +
		"<button id=\"row-fav\" text=\"♡\" on-click=\"toggle-fav\" font-size=\"3.0\" min-width=\"7.5\" pref-width=\"7.5\" min-height=\"4.8\" pref-height=\"4.8\" pad=\"0\" />" +
		"</horizontal>";

	private const string HeartIconObjectName = "BeatWankoHeartIcon";

	private static readonly FieldInfo DetailFavoriteToggleField = typeof(StandardLevelDetailView).GetField("_favoriteToggle", BindingFlags.Instance | BindingFlags.NonPublic);

	private static readonly FieldInfo BuyFavoriteToggleField = typeof(StandardLevelBuyView).GetField("_favoriteToggle", BindingFlags.Instance | BindingFlags.NonPublic);

	private static Sprite _outlineHeartSprite;

	private static Sprite _filledHeartSprite;

	private static Material _heartMaterial;

	private static Color _outlineHeartColor = new Color(1f, 1f, 1f, 0.502f);

	private static Color _filledHeartColor = Color.white;

	private static Color _disabledHeartColor = new Color(0.784f, 0.784f, 0.784f, 0.502f);

	private static bool _loggedHeartSource;

	private static bool _loggedHeartFallback;

	private readonly List<SummaryRowHost> _rows = new List<SummaryRowHost>();

	private Coroutine _scrollRefreshCoroutine;

	[UIComponent("summary-header")]
	public TextMeshProUGUI HeaderText;

	[UIComponent("summary-title")]
	public TextMeshProUGUI TitleText;

	[UIComponent("summary-stats")]
	public TextMeshProUGUI StatsText;

	[UIComponent("summary-note")]
	public TextMeshProUGUI NoteText;

	[UIComponent("history-scroll")]
	public ScrollView HistoryScrollView;

	[UIComponent("history-container")]
	public VerticalLayoutGroup HistoryContainer;

	[UIAction("#post-parse")]
	public void OnPostParse()
	{
		try
		{
			EnsureTextLayoutElement(HeaderText, 110f, 4.5f);
			if (HeaderText != null)
			{
				HeaderText.overflowMode = TextOverflowModes.Overflow;
			}
			EnsureTextLayoutElement(TitleText, 110f, 6.8f);
			if (TitleText != null)
			{
				TitleText.enableWordWrapping = false;
				TitleText.overflowMode = TextOverflowModes.Overflow;
				TitleText.enableAutoSizing = true;
				TitleText.fontSizeMin = 2.8f;
				TitleText.fontSizeMax = 4.2f;
			}
			EnsureTextLayoutElement(StatsText, 110f, 10.5f);
			if (StatsText != null)
			{
				StatsText.enableWordWrapping = false;
				StatsText.overflowMode = TextOverflowModes.Overflow;
				StatsText.enableAutoSizing = true;
				StatsText.fontSizeMin = 2.1f;
				StatsText.fontSizeMax = 2.9f;
			}
			EnsureTextLayoutElement(NoteText, 110f, 5.2f);
			if (NoteText != null)
			{
				NoteText.enableWordWrapping = false;
				NoteText.overflowMode = TextOverflowModes.Overflow;
				NoteText.enableAutoSizing = true;
				NoteText.fontSizeMin = 2.0f;
				NoteText.fontSizeMax = 2.9f;
			}
			if (HistoryContainer != null)
			{
				ContentSizeFitter fitter = ((Component)HistoryContainer).GetComponent<ContentSizeFitter>();
				if (fitter != null)
				{
					fitter.enabled = false;
				}
				LayoutElement containerLe = ((Component)HistoryContainer).GetComponent<LayoutElement>() ?? ((Component)HistoryContainer).gameObject.AddComponent<LayoutElement>();
				containerLe.minWidth = 96f;
				containerLe.preferredWidth = 96f;
				containerLe.minHeight = -1f;
				containerLe.preferredHeight = -1f;
				containerLe.flexibleHeight = 0f;
				Transform contentContainer = ((Component)HistoryContainer).transform.parent;
				if (contentContainer != null)
				{
					VerticalLayoutGroup vlg = contentContainer.GetComponent<VerticalLayoutGroup>();
					if (vlg != null)
					{
						vlg.childControlHeight = true;
						vlg.childForceExpandHeight = false;
						vlg.childControlWidth = true;
						vlg.childForceExpandWidth = false;
						vlg.childAlignment = TextAnchor.UpperCenter;
					}
				}
			}
		}
		catch (Exception ex)
		{
			Plugin.Log?.Warn("[BeatWanko] SummaryHost OnPostParse warning: " + ex.Message);
		}
	}

	public void Apply(SessionSummary summary)
	{
		try
		{
			if (TitleText != null)
			{
				if (summary.Mode == ChallengeMode.StepUp && summary.Cups >= ChallengeModeExtensions.StepUpMaxStep)
				{
					TitleText.text = $"🏆 [{summary.Mode.ShortBadge()}] {summary.Cups}杯 完走！";
				}
				else if (summary.Mode.IsRanked())
				{
					TitleText.text = ((summary.Cups > 0) ? $"🍜 [{summary.Mode.ShortBadge()}] 合計 {summary.Cups}杯 完食！" : $"🍜 [{summary.Mode.ShortBadge()}] 合計 0杯（完食なし）");
				}
				else
				{
					TitleText.text = ((summary.Cups > 0) ? $"🍜 合計 {summary.Cups}杯 完食！" : "🍜 合計 0杯（完食なし）");
				}
			}
			if (StatsText != null)
			{
				TimeSpan timeSpan = TimeSpan.FromSeconds(summary.TotalPlaySeconds);
				string bestTitle = TruncateSongTitle(summary.BestEntry?.SongName);
				string arg = ((summary.BestEntry != null) ? $"最高精度：{summary.BestEntry.AccuracyPercent:0.0}% 『{bestTitle}』" : "最高精度：-");
				StatsText.text = $"合計スコア：{summary.TotalScore:N0}\u3000時間：{(int)timeSpan.TotalMinutes}分{timeSpan.Seconds:00}秒\u3000ノーツ：{summary.TotalNotes}\n" + $"平均精度：{summary.AverageAccuracyPercent:0.0}%\u3000{arg}";
			}
			ApplyHistory(summary, resetScrollToTop: true);
			if (NoteText != null)
			{
				NoteText.text = ((!string.IsNullOrEmpty(summary.Notice)) ? summary.Notice : ((summary.Cups > 0) ? "ごちそうさまでした！" : "おつかれさまでした！"));
			}
		}
		catch (Exception arg2)
		{
			Plugin.Log?.Error($"[BeatWanko] Summary apply failed: {arg2}");
		}
	}

	public void ApplyHistory(SessionSummary summary)
	{
		ApplyHistory(summary, resetScrollToTop: false);
	}

	public void ApplyHistory(SessionSummary summary, bool resetScrollToTop)
	{
		if (summary == null || HistoryContainer == null)
		{
			return;
		}
		List<HistoryEntry> list = summary.Entries ?? new List<HistoryEntry>();
		EnsurePoolSize(list.Count);
		for (int i = 0; i < _rows.Count; i++)
		{
			SummaryRowHost row = _rows[i];
			if (row == null)
			{
				continue;
			}
			if (i < list.Count)
			{
				HistoryEntry historyEntry = list[i];
				row.EntryIndex = i;
				if (row.Root != null)
				{
					row.Root.gameObject.SetActive(value: true);
				}
				if (row.Label != null && historyEntry != null)
				{
					string starTag = ((historyEntry.StepBand > 0) ? ((historyEntry.StepBand == 1) ? "[★0〜1] " : $"[★{historyEntry.StepBand}] ") : string.Empty);
					string scoreTag = ((historyEntry.MultipliedScore > 0) ? $" {historyEntry.MultipliedScore:N0}pt" : string.Empty);
					string failTag = (historyEntry.Cleared ? string.Empty : "（Fail）");
					row.Label.text = $"{i + 1}. {starTag}{historyEntry.SongName} [{historyEntry.Difficulty}]{scoreTag} {historyEntry.AccuracyPercent:0.0}%{failTag}";
				}
				UpdateFavButton(row.FavButton, historyEntry != null && historyEntry.Favorite, historyEntry != null && !string.IsNullOrEmpty(historyEntry.LevelId));
			}
			else
			{
				if (row.Label != null)
				{
					row.Label.text = string.Empty;
				}
				if (row.Root != null)
				{
					row.Root.gameObject.SetActive(value: false);
				}
			}
		}
		RefreshScrollViewLayout(resetScrollToTop);
	}

	public void ScheduleScrollRefresh()
	{
		try
		{
			if (Plugin.Runner == null)
			{
				return;
			}
			if (_scrollRefreshCoroutine != null)
			{
				try
				{
					Plugin.Runner.Stop(_scrollRefreshCoroutine);
				}
				catch
				{
				}
				_scrollRefreshCoroutine = null;
			}
			_scrollRefreshCoroutine = Plugin.Runner.Run(DelayedScrollRefreshRoutine());
		}
		catch
		{
		}
	}

	private IEnumerator DelayedScrollRefreshRoutine()
	{
		try
		{
			yield return null;
			try
			{
				Canvas.ForceUpdateCanvases();
			}
			catch
			{
			}
			RefreshScrollViewLayout(resetScrollToTop: true);
		}
		finally
		{
			_scrollRefreshCoroutine = null;
		}
	}

	private void EnsurePoolSize(int requiredCount)
	{
		if (HistoryContainer == null)
		{
			return;
		}
		while (_rows.Count < requiredCount)
		{
			SummaryRowHost rowHost = new SummaryRowHost { EntryIndex = _rows.Count };
			ZenjectSingleton<BSMLParser>.Instance.Parse(RowTemplateBsml, ((Component)HistoryContainer).gameObject, rowHost);
			ConfigureRowComponents(rowHost);
			_rows.Add(rowHost);
		}
	}

	private static void ConfigureRowComponents(SummaryRowHost row)
	{
		if (row == null)
		{
			return;
		}
		try
		{
			if (row.Root != null)
			{
				ContentSizeFitter rootFitter = row.Root.GetComponent<ContentSizeFitter>();
				if (rootFitter != null)
				{
					rootFitter.enabled = false;
				}
				LayoutElement rootLe = row.Root.GetComponent<LayoutElement>() ?? row.Root.gameObject.AddComponent<LayoutElement>();
				rootLe.minWidth = 96f;
				rootLe.preferredWidth = 96f;
				rootLe.minHeight = 5.4f;
				rootLe.preferredHeight = 5.4f;
				rootLe.flexibleWidth = 0f;
				rootLe.flexibleHeight = 0f;
			}
			if (row.Label != null)
			{
				row.Label.enableWordWrapping = false;
				row.Label.overflowMode = TextOverflowModes.Ellipsis;
				row.Label.alignment = TextAlignmentOptions.MidlineLeft;
				LayoutElement labelLe = row.Label.GetComponent<LayoutElement>() ?? row.Label.gameObject.AddComponent<LayoutElement>();
				labelLe.minWidth = 86f;
				labelLe.preferredWidth = 86f;
				labelLe.minHeight = 5.0f;
				labelLe.preferredHeight = 5.0f;
				labelLe.flexibleWidth = 1f;
				labelLe.flexibleHeight = 0f;
			}
			if (row.FavButton != null)
			{
				ContentSizeFitter btnFitter = row.FavButton.GetComponent<ContentSizeFitter>();
				if (btnFitter != null)
				{
					btnFitter.enabled = false;
				}
				Transform contentTf = row.FavButton.transform.Find("Content");
				if (contentTf != null)
				{
					HorizontalLayoutGroup innerHlg = contentTf.GetComponent<HorizontalLayoutGroup>();
					if (innerHlg != null)
					{
						innerHlg.padding = new RectOffset(1, 1, 0, 0);
					}
				}
				LayoutElement btnLe = row.FavButton.GetComponent<LayoutElement>() ?? row.FavButton.gameObject.AddComponent<LayoutElement>();
				btnLe.minWidth = 7.5f;
				btnLe.preferredWidth = 7.5f;
				btnLe.minHeight = 4.8f;
				btnLe.preferredHeight = 4.8f;
				btnLe.flexibleWidth = 0f;
				btnLe.flexibleHeight = 0f;
			}
		}
		catch (Exception ex)
		{
			Plugin.Log?.Warn("[BeatWanko] ConfigureRowComponents failed: " + ex.Message);
		}
	}

	private void RefreshScrollViewLayout(bool resetScrollToTop)
	{
		try
		{
			if (HistoryContainer == null)
			{
				return;
			}
			float prevPos = ((UnityEngine.Object)(object)HistoryScrollView != null) ? HistoryScrollView.position : 0f;
			RectTransform containerRt = ((Component)HistoryContainer).transform as RectTransform;
			if (containerRt != null)
			{
				LayoutRebuilder.ForceRebuildLayoutImmediate(containerRt);
			}
			Transform parent1 = ((Component)HistoryContainer).transform.parent;
			if (parent1 is RectTransform parent1Rt)
			{
				LayoutRebuilder.ForceRebuildLayoutImmediate(parent1Rt);
			}
			Transform parent2 = parent1?.parent;
			if (parent2 is RectTransform parent2Rt)
			{
				LayoutRebuilder.ForceRebuildLayoutImmediate(parent2Rt);
			}
			try
			{
				Canvas.ForceUpdateCanvases();
			}
			catch
			{
			}
			if ((UnityEngine.Object)(object)HistoryScrollView != null)
			{
				HistoryScrollView.UpdateContentSize();
				HistoryScrollView.ScrollTo(resetScrollToTop ? 0f : prevPos, animated: false);
			}
		}
		catch (Exception ex)
		{
			Plugin.Log?.Warn("[BeatWanko] RefreshScrollViewLayout failed: " + ex.Message);
		}
	}

	private static string TruncateSongTitle(string s, int maxChars = 32)
	{
		if (string.IsNullOrEmpty(s) || s.Length <= maxChars)
		{
			return s ?? string.Empty;
		}
		return s.Substring(0, maxChars) + "…";
	}

	private static void EnsureTextLayoutElement(TextMeshProUGUI text, float width, float height)
	{
		if (text == null)
		{
			return;
		}
		LayoutElement le = text.GetComponent<LayoutElement>() ?? text.gameObject.AddComponent<LayoutElement>();
		le.minWidth = width;
		le.preferredWidth = width;
		le.minHeight = height;
		le.preferredHeight = height;
		le.flexibleWidth = 0f;
		le.flexibleHeight = 0f;
	}

	private static void UpdateFavButton(Button favBtn, bool isFavorite, bool interactable)
	{
		if (favBtn == null)
		{
			return;
		}
		favBtn.interactable = interactable;
		TextMeshProUGUI componentInChildren = favBtn.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true);
		if (EnsureVanillaHeartSprites())
		{
			ImageView orCreateHeartIcon = GetOrCreateHeartIcon(favBtn);
			if ((UnityEngine.Object)(object)orCreateHeartIcon != null)
			{
				((Image)(object)orCreateHeartIcon).sprite = (isFavorite ? _filledHeartSprite : _outlineHeartSprite);
				((Graphic)(object)orCreateHeartIcon).color = ((!interactable) ? _disabledHeartColor : (isFavorite ? _filledHeartColor : _outlineHeartColor));
				((Component)(object)orCreateHeartIcon).gameObject.SetActive(value: true);
				if (componentInChildren != null)
				{
					componentInChildren.gameObject.SetActive(value: false);
				}
				return;
			}
		}
		Transform transform = (favBtn.transform.Find("Content") ?? favBtn.transform).Find("BeatWankoHeartIcon");
		if (transform != null)
		{
			transform.gameObject.SetActive(value: false);
		}
		if (componentInChildren != null)
		{
			componentInChildren.gameObject.SetActive(value: true);
			componentInChildren.richText = true;
			componentInChildren.text = (isFavorite ? "<color=#FF69B4>♥</color>" : "♡");
		}
	}

	private static bool EnsureVanillaHeartSprites()
	{
		if (_outlineHeartSprite != null && _filledHeartSprite != null)
		{
			return true;
		}
		try
		{
			if (DetailFavoriteToggleField != null)
			{
				StandardLevelDetailView[] array = Resources.FindObjectsOfTypeAll<StandardLevelDetailView>();
				foreach (StandardLevelDetailView standardLevelDetailView in array)
				{
					if (!(standardLevelDetailView == null) && TryExtractFromFavoriteToggle(DetailFavoriteToggleField.GetValue(standardLevelDetailView) as Toggle, "StandardLevelDetailView._favoriteToggle"))
					{
						return true;
					}
				}
			}
			if (BuyFavoriteToggleField != null)
			{
				StandardLevelBuyView[] array2 = Resources.FindObjectsOfTypeAll<StandardLevelBuyView>();
				foreach (StandardLevelBuyView standardLevelBuyView in array2)
				{
					if (!(standardLevelBuyView == null) && TryExtractFromFavoriteToggle(BuyFavoriteToggleField.GetValue(standardLevelBuyView) as Toggle, "StandardLevelBuyView._favoriteToggle"))
					{
						return true;
					}
				}
			}
		}
		catch (Exception ex)
		{
			Plugin.Log?.Warn("[BeatWanko] Resolving vanilla heart sprites failed: " + ex.Message);
		}
		if (_outlineHeartSprite != null && _filledHeartSprite != null)
		{
			return true;
		}
		if (!_loggedHeartFallback)
		{
			_loggedHeartFallback = true;
			Plugin.Log?.Warn("[BeatWanko] Vanilla heart sprites unavailable; falling back to text ♡/♥.");
		}
		return false;
	}

	private static bool TryExtractFromFavoriteToggle(Toggle toggle, string sourceName)
	{
		if (toggle == null)
		{
			return false;
		}
		Image image = toggle.targetGraphic as Image;
		Image image2 = toggle.graphic as Image;
		if (_outlineHeartSprite == null && image != null && image.sprite != null)
		{
			_outlineHeartSprite = image.sprite;
		}
		if (_filledHeartSprite == null && image2 != null && image2.sprite != null)
		{
			_filledHeartSprite = image2.sprite;
		}
		if (_heartMaterial == null)
		{
			_heartMaterial = image?.material ?? image2?.material;
		}
		if (_outlineHeartSprite == null || _filledHeartSprite == null)
		{
			return false;
		}
		Color normalColor = toggle.colors.normalColor;
		Color color = ((image != null) ? image.color : Color.white);
		float a = Mathf.Clamp(normalColor.a * color.a, 0.45f, 1f);
		_outlineHeartColor = new Color(color.r * normalColor.r, color.g * normalColor.g, color.b * normalColor.b, a);
		Color color2 = ((image2 != null) ? image2.color : Color.white);
		_filledHeartColor = ((color2.a > 0f) ? color2 : Color.white);
		Color disabledColor = toggle.colors.disabledColor;
		_disabledHeartColor = ((disabledColor.a > 0.1f) ? disabledColor : new Color(0.784f, 0.784f, 0.784f, 0.502f));
		if (!_loggedHeartSource)
		{
			_loggedHeartSource = true;
			Plugin.Log?.Info("[BeatWanko] Using vanilla heart sprites from " + sourceName + " (OFF=" + _outlineHeartSprite.name + ", ON=" + _filledHeartSprite.name + ").");
		}
		return true;
	}

	private static ImageView GetOrCreateHeartIcon(Button favBtn)
	{
		try
		{
			Transform transform = favBtn.transform.Find("Content") ?? favBtn.transform;
			Transform transform2 = transform.Find("BeatWankoHeartIcon");
			if (transform2 != null)
			{
				return transform2.GetComponent<ImageView>();
			}
			GameObject gameObject = new GameObject("BeatWankoHeartIcon", typeof(RectTransform));
			RectTransform component = gameObject.GetComponent<RectTransform>();
			component.SetParent(transform, worldPositionStays: false);
			component.anchorMin = new Vector2(0.5f, 0.5f);
			component.anchorMax = new Vector2(0.5f, 0.5f);
			component.pivot = new Vector2(0.5f, 0.5f);
			component.anchoredPosition = Vector2.zero;
			component.sizeDelta = new Vector2(2.8f, 2.8f);
			gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
			ImageView imageView = gameObject.AddComponent<ImageView>();
			((Image)(object)imageView).preserveAspect = true;
			((Graphic)(object)imageView).raycastTarget = false;
			Material material = ((_heartMaterial != null) ? _heartMaterial : Utilities.ImageResources.NoGlowMat);
			if (material != null)
			{
				((Graphic)(object)imageView).material = material;
			}
			return imageView;
		}
		catch (Exception ex)
		{
			Plugin.Log?.Warn("[BeatWanko] Creating heart icon image failed: " + ex.Message);
			return null;
		}
	}

	[UIAction("close-summary")]
	public void OnCloseSummary()
	{
		Session.OnSummaryClosed();
	}
}
