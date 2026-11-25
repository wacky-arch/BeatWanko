using System;
using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Parser;
using BeatSaberMarkupLanguage.Util;
using HMUI;
using UnityEngine;

namespace BeatWanko;

internal sealed class WankoMainViewController : ViewController
{
	protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
	{
		base.DidActivate(firstActivation, addedToHierarchy, screenSystemEnabling);
		if (firstActivation)
		{
			try
			{
				string resource = WankoUI.GetResource("BeatWanko.UI.settings.bsml");
				SettingsHost.Instance.ParserParams = ZenjectSingleton<BSMLParser>.Instance.Parse(resource, ((Component)(object)this).gameObject, SettingsHost.Instance);
			}
			catch (Exception arg)
			{
				Plugin.Log?.Error($"[BeatWanko] Failed to parse main ViewController BSML: {arg}");
			}
		}
	}
}
