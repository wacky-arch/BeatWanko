using System;
using BeatSaberMarkupLanguage;
using HMUI;
using UnityEngine;

namespace BeatWanko;

internal sealed class WankoFlowCoordinator : FlowCoordinator
{
	private WankoMainViewController _mainViewController;

	protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
	{
		try
		{
			if (addedToHierarchy)
			{
				SetTitle("BeatWanko\ud83c\udf5c");
				base.showBackButton = true;
				if ((UnityEngine.Object)(object)_mainViewController == null)
				{
					_mainViewController = BeatSaberUI.CreateViewController<WankoMainViewController>();
				}
				ProvideInitialViewControllers(_mainViewController);
			}
		}
		catch (Exception arg)
		{
			Plugin.Log?.Error($"[BeatWanko] WankoFlowCoordinator.DidActivate failed: {arg}");
		}
	}

	protected override void BackButtonWasPressed(ViewController topViewController)
	{
		SettingsHost.Instance.FlushPendingSettings();
		WankoUI.CloseMainFlowIfOpen(immediately: false);
	}
}
