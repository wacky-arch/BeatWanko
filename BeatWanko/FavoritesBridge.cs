using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HMUI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BeatWanko;

internal static class FavoritesBridge
{
	private static readonly FieldInfo FavoriteLevelsChangedEventField = FindFieldInHierarchy(typeof(PlayerData), "favoriteLevelsSetDidChangeEvent");

	private static readonly FieldInfo NavBeatmapLevelsModelField = FindFieldInHierarchy(typeof(LevelFilteringNavigationController), "_beatmapLevelsModel");

	private static readonly FieldInfo NavAnnotatedCollectionsViewControllerField = FindFieldInHierarchy(typeof(LevelFilteringNavigationController), "_annotatedBeatmapLevelCollectionsViewController");

	private static readonly FieldInfo NavCustomLevelPacksField = FindFieldInHierarchy(typeof(LevelFilteringNavigationController), "_customLevelPacks");

	private static readonly FieldInfo NavAllLevelPacksField = FindFieldInHierarchy(typeof(LevelFilteringNavigationController), "_allBeatmapLevelPacks");

	private static readonly FieldInfo NavOstPacksField = FindFieldInHierarchy(typeof(LevelFilteringNavigationController), "_ostBeatmapLevelPacks");

	private static readonly FieldInfo NavMusicPacksField = FindFieldInHierarchy(typeof(LevelFilteringNavigationController), "_musicPacksBeatmapLevelPacks");

	private static readonly FieldInfo NavLevelSearchViewControllerField = FindFieldInHierarchy(typeof(LevelFilteringNavigationController), "_levelSearchViewController");

	private static readonly FieldInfo LevelCollectionTableViewField = FindFieldInHierarchy(typeof(LevelCollectionTableView), "_tableView");

	private static readonly MethodInfo UpdateCustomSongsMethod = FindMethodInHierarchy(typeof(LevelFilteringNavigationController), "UpdateCustomSongs");

	private static Coroutine _pendingSoloRefresh;

	private static GameScenesManager _cachedScenesManager;

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

	private static PlayerDataModel FindPlayerDataModel()
	{
		PlayerDataModel playerDataModel = null;
		PlayerDataModel[] array = Resources.FindObjectsOfTypeAll<PlayerDataModel>();
		foreach (PlayerDataModel playerDataModel2 in array)
		{
			if (playerDataModel2?.playerData?.favoritesLevelIds != null)
			{
				if (playerDataModel2.gameObject != null && playerDataModel2.gameObject.scene.IsValid())
				{
					return playerDataModel2;
				}
				if (playerDataModel == null)
				{
					playerDataModel = playerDataModel2;
				}
			}
		}
		return playerDataModel;
	}

	private static BeatmapLevel TryResolveBeatmapLevel(string levelId)
	{
		if (string.IsNullOrEmpty(levelId))
		{
			return null;
		}
		if (SongCoreBridge.Available && SongCoreBridge.TryGetCustomLevel(levelId, out var level) && level != null)
		{
			return level;
		}
		try
		{
			if (NavBeatmapLevelsModelField != null)
			{
				LevelFilteringNavigationController[] array = Resources.FindObjectsOfTypeAll<LevelFilteringNavigationController>();
				foreach (LevelFilteringNavigationController levelFilteringNavigationController in array)
				{
					if ((UnityEngine.Object)(object)levelFilteringNavigationController == null)
					{
						continue;
					}
					object levelsModel = NavBeatmapLevelsModelField.GetValue(levelFilteringNavigationController);
					BeatmapLevel beatmapLevel = InvokeGetBeatmapLevel(levelsModel, levelId);
					if (beatmapLevel != null)
					{
						return beatmapLevel;
					}
				}
			}
		}
		catch (Exception ex)
		{
			Plugin.Log?.Warn("[BeatWanko] BeatmapLevelsModel lookup failed: " + ex.Message);
		}
		return null;
	}

	private static BeatmapLevel InvokeGetBeatmapLevel(object levelsModel, string levelId)
	{
		if (levelsModel == null || string.IsNullOrEmpty(levelId))
		{
			return null;
		}
		if (levelsModel is BeatmapLevelsModel typedModel)
		{
			return GetBeatmapLevelDynamic(typedModel, levelId);
		}
		return GetBeatmapLevelDynamic(levelsModel, levelId);
	}

	private static BeatmapLevel GetBeatmapLevelDynamic(object levelsModel, string levelId)
	{
		MethodInfo[] methods = levelsModel.GetType()
			.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
			.Where((MethodInfo m) => m.Name == "GetBeatmapLevel")
			.OrderBy((MethodInfo m) => m.GetParameters().Length)
			.ToArray();
		foreach (MethodInfo method in methods)
		{
			ParameterInfo[] ps = method.GetParameters();
			if (ps.Length == 0 || ps[0].ParameterType != typeof(string))
			{
				continue;
			}
			object[] args = new object[ps.Length];
			args[0] = levelId;
			bool ok = true;
			for (int i = 1; i < ps.Length; i++)
			{
				if (ps[i].HasDefaultValue)
				{
					object def = ps[i].DefaultValue;
					args[i] = ((def != DBNull.Value && def != Type.Missing) ? def : null);
				}
				else
				{
					Type t = ps[i].ParameterType;
					if (t.IsValueType && Nullable.GetUnderlyingType(t) == null)
					{
						args[i] = Activator.CreateInstance(t);
					}
					else
					{
						ok = false;
						break;
					}
				}
			}
			if (ok)
			{
				return method.Invoke(levelsModel, args) as BeatmapLevel;
			}
		}
		return null;
	}

	private static void InvokeFavoriteChangedEvent(PlayerData playerData)
	{
		try
		{
			if (playerData != null && !(FavoriteLevelsChangedEventField == null))
			{
				(FavoriteLevelsChangedEventField.GetValue(playerData) as Action)?.Invoke();
			}
		}
		catch (Exception ex)
		{
			Plugin.Log?.Warn("[BeatWanko] Invoking favoriteLevelsSetDidChangeEvent failed: " + ex.Message);
		}
	}

	private static void RefreshActiveLevelCollectionFavorites()
	{
		try
		{
			LevelCollectionViewController[] array = Resources.FindObjectsOfTypeAll<LevelCollectionViewController>();
			foreach (LevelCollectionViewController levelCollectionViewController in array)
			{
				if ((UnityEngine.Object)(object)levelCollectionViewController != null && ((Component)(object)levelCollectionViewController).gameObject != null && ((Component)(object)levelCollectionViewController).gameObject.scene.IsValid() && ((Behaviour)(object)levelCollectionViewController).isActiveAndEnabled)
				{
					MethodInfo refreshMethod = FindMethodInHierarchy(((object)levelCollectionViewController).GetType(), "RefreshFavorites");
					refreshMethod?.Invoke(levelCollectionViewController, null);
				}
			}
		}
		catch (Exception ex)
		{
			Plugin.Log?.Warn("[BeatWanko] RefreshFavorites UI sync failed: " + ex.Message);
		}
	}

	private static void SavePlayerDataSafely(PlayerDataModel model)
	{
		if ((UnityEngine.Object)(object)model == null)
		{
			return;
		}
		try
		{
			MethodInfo saveAsync = model.GetType()
				.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
				.Where((MethodInfo m) => m.Name == "SaveAsync")
				.OrderBy((MethodInfo m) => m.GetParameters().Length)
				.FirstOrDefault();
			if (saveAsync != null)
			{
				ParameterInfo[] ps = saveAsync.GetParameters();
				object[] args = new object[ps.Length];
				bool ok = true;
				for (int i = 0; i < ps.Length; i++)
				{
					if (ps[i].HasDefaultValue)
					{
						object def = ps[i].DefaultValue;
						args[i] = ((def != DBNull.Value && def != Type.Missing) ? def : null);
					}
					else
					{
						ok = false;
						break;
					}
				}
				if (ok && saveAsync.Invoke(model, args) is Task task)
				{
					task.ContinueWith(delegate(Task t)
					{
						if (t.IsFaulted && t.Exception != null)
						{
							Plugin.Log?.Warn("[BeatWanko] PlayerData SaveAsync failed: " + t.Exception.GetBaseException().Message);
						}
					}, TaskContinuationOptions.OnlyOnFaulted);
					return;
				}
			}
		}
		catch (Exception ex)
		{
			Plugin.Log?.Warn("[BeatWanko] SaveAsync invocation failed (" + ex.Message + "); trying synchronous Save.");
		}
		try
		{
			MethodInfo save = FindMethodInHierarchy(model.GetType(), "Save");
			if (save != null && save.GetParameters().Length == 0)
			{
				save.Invoke(model, null);
			}
		}
		catch (Exception ex2)
		{
			Plugin.Log?.Warn("[BeatWanko] Synchronous Save failed: " + ex2.Message);
		}
	}

	private static bool TryInvokePlayerDataFavoriteMethod(PlayerData playerData, string methodName, BeatmapLevel beatmapLevel)
	{
		if (playerData == null || beatmapLevel == null)
		{
			return false;
		}
		MethodInfo method = playerData.GetType()
			.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
			.Where((MethodInfo m) => m.Name == methodName)
			.FirstOrDefault((MethodInfo m) => m.GetParameters().Length >= 1 && m.GetParameters()[0].ParameterType.IsAssignableFrom(typeof(BeatmapLevel)));
		if (method == null)
		{
			return false;
		}
		ParameterInfo[] ps = method.GetParameters();
		object[] args = new object[ps.Length];
		args[0] = beatmapLevel;
		for (int i = 1; i < ps.Length; i++)
		{
			if (!ps[i].HasDefaultValue)
			{
				return false;
			}
			object def = ps[i].DefaultValue;
			args[i] = ((def != DBNull.Value && def != Type.Missing) ? def : null);
		}
		method.Invoke(playerData, args);
		return true;
	}

	public static bool IsFavorite(string levelId)
	{
		try
		{
			if (string.IsNullOrEmpty(levelId))
			{
				return false;
			}
			PlayerDataModel playerDataModel = FindPlayerDataModel();
			return playerDataModel?.playerData?.favoritesLevelIds != null && playerDataModel.playerData.favoritesLevelIds.Contains(levelId);
		}
		catch
		{
			return false;
		}
	}

	public static bool TrySetFavorite(string levelId, bool favorite, out bool actualFavorite)
	{
		actualFavorite = false;
		try
		{
			if (string.IsNullOrEmpty(levelId))
			{
				return false;
			}
			PlayerDataModel playerDataModel = FindPlayerDataModel();
			if (playerDataModel?.playerData?.favoritesLevelIds == null)
			{
				return false;
			}
			PlayerData playerData = playerDataModel.playerData;
			HashSet<string> favoritesLevelIds = playerData.favoritesLevelIds;
			bool flag = favoritesLevelIds.Contains(levelId);
			if (flag == favorite)
			{
				actualFavorite = flag;
				return true;
			}
			BeatmapLevel beatmapLevel = TryResolveBeatmapLevel(levelId);
			bool invoked = false;
			if (beatmapLevel != null && string.Equals(beatmapLevel.levelID, levelId, StringComparison.Ordinal))
			{
				invoked = TryInvokePlayerDataFavoriteMethod(playerData, favorite ? "AddLevelToFavorites" : "RemoveLevelFromFavorites", beatmapLevel);
			}
			if (!invoked)
			{
				if (favorite)
				{
					favoritesLevelIds.Add(levelId);
				}
				else
				{
					favoritesLevelIds.Remove(levelId);
				}
				InvokeFavoriteChangedEvent(playerData);
			}
			actualFavorite = favoritesLevelIds.Contains(levelId);
			RefreshActiveLevelCollectionFavorites();
			SavePlayerDataSafely(playerDataModel);
			return true;
		}
		catch (Exception ex)
		{
			Plugin.Log?.Warn("[BeatWanko] Updating favourite failed: " + ex.Message);
			return false;
		}
	}

	public static bool TryAddFavorite(string levelId, out bool alreadyFavorite)
	{
		alreadyFavorite = IsFavorite(levelId);
		if (alreadyFavorite)
		{
			return true;
		}
		bool actualFavorite;
		return TrySetFavorite(levelId, favorite: true, out actualFavorite);
	}

	public static void RefreshSoloSelectionIfNeeded()
	{
		ScheduleSafeSoloRefresh(customSongsChanged: false, favoritesChanged: true);
	}

	public static void ScheduleSafeSoloRefresh(bool customSongsChanged, bool favoritesChanged)
	{
		try
		{
			if (Plugin.Runner == null)
			{
				return;
			}
			if (_pendingSoloRefresh != null)
			{
				try
				{
					Plugin.Runner.Stop(_pendingSoloRefresh);
				}
				catch
				{
				}
				_pendingSoloRefresh = null;
			}
			_pendingSoloRefresh = Plugin.Runner.Run(SafeSoloRefreshRoutine(customSongsChanged, favoritesChanged));
		}
		catch (Exception ex)
		{
			Plugin.Log?.Warn("[BeatWanko] ScheduleSafeSoloRefresh failed: " + ex.Message);
		}
	}

	private static IEnumerator SafeSoloRefreshRoutine(bool customSongsChanged, bool favoritesChanged)
	{
		try
		{
			float elapsed = 0f;
			SoloFreePlayFlowCoordinator soloFc = null;
			LevelFilteringNavigationController filteringNav = null;
			while (elapsed < 12.0f)
			{
				if (Session.State == SessionState.Playing || Session.State == SessionState.Interstitial)
				{
					yield break;
				}
				if (IsSoloSelectionReady(out soloFc, out filteringNav))
				{
					break;
				}
				yield return null;
				elapsed += Time.unscaledDeltaTime;
			}
			if (Session.State == SessionState.Playing || Session.State == SessionState.Interstitial || !IsSoloSelectionReady(out soloFc, out filteringNav))
			{
				yield break;
			}
			yield return null;
			yield return null;
			if (Session.State == SessionState.Playing || Session.State == SessionState.Interstitial || !IsSoloSelectionReady(out soloFc, out filteringNav))
			{
				yield break;
			}
			try
			{
				Canvas.ForceUpdateCanvases();
			}
			catch
			{
			}
			if (favoritesChanged)
			{
				RefreshActiveLevelCollectionFavorites();
			}
			if (customSongsChanged)
			{
				TrySafelySyncCustomSongs(filteringNav);
			}
			RecoverCollapsedLevelCollectionTableViews();
		}
		finally
		{
			_pendingSoloRefresh = null;
		}
	}

	private static bool IsSoloSelectionReady(out SoloFreePlayFlowCoordinator soloFc, out LevelFilteringNavigationController filteringNav)
	{
		soloFc = null;
		filteringNav = null;
		try
		{
			if (string.Equals(SceneManager.GetActiveScene().name, "GameCore", StringComparison.Ordinal))
			{
				return false;
			}
			if ((UnityEngine.Object)(object)_cachedScenesManager == null)
			{
				_cachedScenesManager = Resources.FindObjectsOfTypeAll<GameScenesManager>().FirstOrDefault();
			}
			if ((UnityEngine.Object)(object)_cachedScenesManager != null && _cachedScenesManager.isInTransition)
			{
				return false;
			}
			if (WankoUI.IsSummaryVisible() || SongCoreBridge.IsLoading())
			{
				return false;
			}
			soloFc = Resources.FindObjectsOfTypeAll<SoloFreePlayFlowCoordinator>().FirstOrDefault((SoloFreePlayFlowCoordinator f) => (UnityEngine.Object)(object)f != null && f.isActivated && !f.isInTransition);
			if ((UnityEngine.Object)(object)soloFc == null || MenuLauncher.IsResultsPresented(soloFc))
			{
				return false;
			}
			LevelSelectionNavigationController levelSelectionNav = Resources.FindObjectsOfTypeAll<LevelSelectionNavigationController>().FirstOrDefault((LevelSelectionNavigationController c) => (UnityEngine.Object)(object)c != null && ((Component)(object)c).gameObject != null && ((Component)(object)c).gameObject.scene.IsValid() && ((Behaviour)(object)c).isActiveAndEnabled && ((Component)(object)c).gameObject.activeInHierarchy);
			if ((UnityEngine.Object)(object)levelSelectionNav == null)
			{
				return false;
			}
			filteringNav = Resources.FindObjectsOfTypeAll<LevelFilteringNavigationController>().FirstOrDefault((LevelFilteringNavigationController c) => (UnityEngine.Object)(object)c != null && ((Component)(object)c).gameObject != null && ((Component)(object)c).gameObject.scene.IsValid() && ((Behaviour)(object)c).isActiveAndEnabled && ((Component)(object)c).gameObject.activeInHierarchy);
			return (UnityEngine.Object)(object)filteringNav != null;
		}
		catch
		{
			return false;
		}
	}

	private static void TrySafelySyncCustomSongs(LevelFilteringNavigationController filteringNav)
	{
		if ((UnityEngine.Object)(object)filteringNav == null)
		{
			return;
		}
		try
		{
			SelectLevelCategoryViewController.LevelCategory category = filteringNav.selectedLevelCategory;
			AnnotatedBeatmapLevelCollectionsViewController annotatedVc = NavAnnotatedCollectionsViewControllerField?.GetValue(filteringNav) as AnnotatedBeatmapLevelCollectionsViewController;
			bool annotatedActive = (UnityEngine.Object)(object)annotatedVc != null && ((Behaviour)(object)annotatedVc).isActiveAndEnabled && ((Component)(object)annotatedVc).gameObject.activeInHierarchy;
			bool canInvokeUpdateCustomSongs = UpdateCustomSongsMethod != null && UpdateCustomSongsMethod.GetParameters().Length == 0 && (category == SelectLevelCategoryViewController.LevelCategory.Favorites || category == SelectLevelCategoryViewController.LevelCategory.All || (category == SelectLevelCategoryViewController.LevelCategory.CustomSongs && annotatedActive));
			if (canInvokeUpdateCustomSongs)
			{
				UpdateCustomSongsMethod.Invoke(filteringNav, null);
				return;
			}
			BeatmapLevelsRepository customRepo = SongCoreBridge.GetCustomLevelsRepositoryObject() as BeatmapLevelsRepository;
			if (customRepo == null)
			{
				return;
			}
			IReadOnlyList<BeatmapLevelPack> customPacks = customRepo.beatmapLevelPacks;
			NavCustomLevelPacksField?.SetValue(filteringNav, customPacks);
			IEnumerable<BeatmapLevelPack> combined = Enumerable.Empty<BeatmapLevelPack>();
			if (NavOstPacksField?.GetValue(filteringNav) is BeatmapLevelPack[] ost)
			{
				combined = combined.Concat(ost);
			}
			if (NavMusicPacksField?.GetValue(filteringNav) is BeatmapLevelPack[] dlc)
			{
				combined = combined.Concat(dlc);
			}
			if (customPacks != null)
			{
				combined = combined.Concat(customPacks);
			}
			BeatmapLevelPack[] allPacks = combined.ToArray();
			NavAllLevelPacksField?.SetValue(filteringNav, allPacks);
			if (NavLevelSearchViewControllerField?.GetValue(filteringNav) is LevelSearchViewController searchVc)
			{
				searchVc.Setup(allPacks);
			}
		}
		catch (Exception ex)
		{
			Plugin.Log?.Warn("[BeatWanko] TrySafelySyncCustomSongs failed: " + ex.Message);
		}
	}

	private static void RecoverCollapsedLevelCollectionTableViews()
	{
		try
		{
			LevelCollectionTableView[] array = Resources.FindObjectsOfTypeAll<LevelCollectionTableView>();
			foreach (LevelCollectionTableView levelCollectionTableView in array)
			{
				if ((UnityEngine.Object)(object)levelCollectionTableView == null || ((Component)(object)levelCollectionTableView).gameObject == null || !((Component)(object)levelCollectionTableView).gameObject.scene.IsValid() || !((Behaviour)(object)levelCollectionTableView).isActiveAndEnabled || !((Component)(object)levelCollectionTableView).gameObject.activeInHierarchy)
				{
					continue;
				}
				TableView tableView = LevelCollectionTableViewField?.GetValue(levelCollectionTableView) as TableView;
				if ((UnityEngine.Object)(object)tableView == null || !((Behaviour)(object)tableView).isActiveAndEnabled || !((Component)(object)tableView).gameObject.activeInHierarchy || tableView.numberOfCells <= 1 || (UnityEngine.Object)(object)tableView.viewportTransform == null)
				{
					continue;
				}
				float height = tableView.viewportTransform.rect.height;
				int visCount = tableView.visibleCells.Count();
				if (height > tableView.cellSize * 1.8f && visCount <= 1)
				{
					Canvas.ForceUpdateCanvases();
					tableView.ReloadDataKeepingPosition();
					Plugin.Log?.Info($"[BeatWanko] Recovered collapsed LevelCollectionTableView layout (cells={tableView.numberOfCells}, prevVisible={visCount}).");
				}
			}
		}
		catch (Exception ex)
		{
			Plugin.Log?.Warn("[BeatWanko] RecoverCollapsedLevelCollectionTableViews failed: " + ex.Message);
		}
	}
}
