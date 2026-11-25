using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace BeatWanko;

internal static class SongCoreBridge
{
	private static bool? _available;

	private static Type _loaderType;

	private static bool _loadingProbeDone;

	private static PropertyInfo _instanceProp;

	private static FieldInfo _instanceField;

	private static PropertyInfo _customLevelsProp;

	private static FieldInfo _customLevelsField;

	private static FieldInfo _customLevelsByIdField;

	private static FieldInfo _customLevelsPathField;

	private static FieldInfo _loadedSaveDataPendingField;

	private static PropertyInfo _areSongsLoadingProp;

	private static PropertyInfo _isLoadingProp;

	private static PropertyInfo _customLevelsRepoProp;

	private static FieldInfo _customLevelsRepoField;

	private static PropertyInfo _beatmapLevelsModelProp;

	private static FieldInfo _beatmapLevelsModelField;

	private static PropertyInfo _customLevelLoaderProp;

	private static FieldInfo _customLevelLoaderField;

	private static MethodInfo _getLevelPackForLevelIdMethod;

	private static FieldInfo _customLevelLoaderSaveDataField;

	public static bool Available
	{
		get
		{
			if (!_available.HasValue)
			{
				_available = GetLoaderType() != null;
			}
			return _available.Value;
		}
	}

	public static bool HasLoadingFlag
	{
		get
		{
			if (!Available)
			{
				return false;
			}
			try
			{
				EnsureLoadingProbe();
				return _areSongsLoadingProp != null || _isLoadingProp != null;
			}
			catch
			{
				return false;
			}
		}
	}

	public static string CustomLevelsDirectory
	{
		get
		{
			try
			{
				if (Available)
				{
					EnsureLoadingProbe();
					object instance = GetLoaderInstance();
					if (instance != null && _customLevelsPathField != null && _customLevelsPathField.GetValue(instance) is string loaderPath && !string.IsNullOrEmpty(loaderPath) && Path.IsPathRooted(loaderPath))
					{
						return loaderPath;
					}
				}
				return Path.Combine(Environment.CurrentDirectory, "Beat Saber_Data", "CustomLevels");
			}
			catch
			{
				return null;
			}
		}
	}

	private static Type GetLoaderType()
	{
		if (_loaderType != null)
		{
			return _loaderType;
		}
		try
		{
			Type type = Type.GetType("SongCore.Loader, SongCore");
			if (type == null)
			{
				Assembly asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault((Assembly a) => string.Equals(a.GetName().Name, "SongCore", StringComparison.OrdinalIgnoreCase));
				type = asm?.GetType("SongCore.Loader");
			}
			_loaderType = type;
		}
		catch
		{
			_loaderType = null;
		}
		return _loaderType;
	}

	private static void EnsureLoadingProbe()
	{
		if (_loadingProbeDone)
		{
			return;
		}
		_loadingProbeDone = true;
		try
		{
			Type loaderType = GetLoaderType();
			if (loaderType == null)
			{
				return;
			}
			BindingFlags anyFlags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
			_instanceProp = loaderType.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			_instanceField = loaderType.GetField("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			_customLevelsProp = loaderType.GetProperty("CustomLevels", anyFlags);
			_customLevelsField = loaderType.GetField("CustomLevels", anyFlags);
			_customLevelsByIdField = loaderType.GetField("CustomLevelsById", anyFlags);
			_customLevelsPathField = loaderType.GetField("_customLevelsPath", anyFlags);
			_loadedSaveDataPendingField = loaderType.GetField("LoadedBeatmapSaveData", anyFlags);
			_areSongsLoadingProp = loaderType.GetProperty("AreSongsLoading", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			_isLoadingProp = loaderType.GetProperty("IsLoading", anyFlags);
			_customLevelsRepoProp = loaderType.GetProperty("CustomLevelsRepository", anyFlags);
			_customLevelsRepoField = loaderType.GetField("CustomLevelsRepository", anyFlags);
			_beatmapLevelsModelProp = loaderType.GetProperty("BeatmapLevelsModelSO", anyFlags);
			_beatmapLevelsModelField = loaderType.GetField("_beatmapLevelsModel", anyFlags);
			_customLevelLoaderProp = loaderType.GetProperty("CustomLevelLoader", anyFlags);
			_customLevelLoaderField = loaderType.GetField("_customLevelLoader", anyFlags);
			_getLevelPackForLevelIdMethod = typeof(BeatmapLevelsModel).GetMethod("GetLevelPackForLevelId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[1] { typeof(string) }, null);
			_customLevelLoaderSaveDataField = typeof(CustomLevelLoader).GetField("_loadedBeatmapSaveData", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		}
		catch
		{
		}
	}

	private static object GetCustomLevelLoaderObject()
	{
		if (!Available)
		{
			return null;
		}
		try
		{
			EnsureLoadingProbe();
			if (_customLevelLoaderProp != null)
			{
				MethodInfo getter = _customLevelLoaderProp.GetGetMethod(nonPublic: true);
				if (getter != null)
				{
					object target = (getter.IsStatic ? null : GetLoaderInstance());
					if (getter.IsStatic || target != null)
					{
						object val = _customLevelLoaderProp.GetValue(target);
						if (val != null)
						{
							return val;
						}
					}
				}
			}
			if (_customLevelLoaderField != null)
			{
				object target = (_customLevelLoaderField.IsStatic ? null : GetLoaderInstance());
				if (_customLevelLoaderField.IsStatic || target != null)
				{
					return _customLevelLoaderField.GetValue(target);
				}
			}
		}
		catch
		{
		}
		return null;
	}

	private static object GetBeatmapLevelsModelObject()
	{
		if (!Available)
		{
			return null;
		}
		try
		{
			EnsureLoadingProbe();
			if (_beatmapLevelsModelProp != null)
			{
				MethodInfo getter = _beatmapLevelsModelProp.GetGetMethod(nonPublic: true);
				if (getter != null)
				{
					object target = (getter.IsStatic ? null : GetLoaderInstance());
					if (getter.IsStatic || target != null)
					{
						object val = _beatmapLevelsModelProp.GetValue(target);
						if (val != null)
						{
							return val;
						}
					}
				}
			}
			if (_beatmapLevelsModelField != null)
			{
				object target = (_beatmapLevelsModelField.IsStatic ? null : GetLoaderInstance());
				if (_beatmapLevelsModelField.IsStatic || target != null)
				{
					object val = _beatmapLevelsModelField.GetValue(target);
					if (val != null)
					{
						return val;
					}
				}
			}
			SoloFreePlayFlowCoordinator soloFc = Resources.FindObjectsOfTypeAll<SoloFreePlayFlowCoordinator>().FirstOrDefault();
			if ((UnityEngine.Object)(object)soloFc != null)
			{
				for (Type t = soloFc.GetType(); t != null; t = t.BaseType)
				{
					foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
					{
						if (typeof(BeatmapLevelsModel).IsAssignableFrom(f.FieldType) && f.GetValue(soloFc) is BeatmapLevelsModel model && model != null)
						{
							return model;
						}
					}
				}
			}
		}
		catch
		{
		}
		return null;
	}

	private static bool ContainsKeyIgnoreCase(IDictionary dict, string key)
	{
		if (dict == null || string.IsNullOrEmpty(key))
		{
			return false;
		}
		if (dict.Contains(key))
		{
			return true;
		}
		foreach (object k in dict.Keys)
		{
			if (k is string ks && string.Equals(ks, key, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	public static object GetCustomLevelsRepositoryObject()
	{
		if (!Available)
		{
			return null;
		}
		try
		{
			EnsureLoadingProbe();
			if (_customLevelsRepoProp != null)
			{
				MethodInfo getter = _customLevelsRepoProp.GetGetMethod(nonPublic: true);
				if (getter != null)
				{
					object target = (getter.IsStatic ? null : GetLoaderInstance());
					if (getter.IsStatic || target != null)
					{
						return _customLevelsRepoProp.GetValue(target);
					}
				}
			}
			if (_customLevelsRepoField != null)
			{
				object target = (_customLevelsRepoField.IsStatic ? null : GetLoaderInstance());
				if (_customLevelsRepoField.IsStatic || target != null)
				{
					return _customLevelsRepoField.GetValue(target);
				}
			}
		}
		catch
		{
		}
		return null;
	}

	private static object GetLoaderInstance()
	{
		EnsureLoadingProbe();
		if (_instanceProp != null)
		{
			return _instanceProp.GetValue(null);
		}
		return _instanceField?.GetValue(null);
	}

	private static object GetCustomLevelsObject()
	{
		if (!Available)
		{
			return null;
		}
		EnsureLoadingProbe();
		if (_customLevelsProp != null)
		{
			MethodInfo getter = _customLevelsProp.GetGetMethod(nonPublic: true);
			if (getter != null)
			{
				object target = (getter.IsStatic ? null : GetLoaderInstance());
				if (getter.IsStatic || target != null)
				{
					return _customLevelsProp.GetValue(target);
				}
			}
		}
		if (_customLevelsField != null)
		{
			object target = (_customLevelsField.IsStatic ? null : GetLoaderInstance());
			if (_customLevelsField.IsStatic || target != null)
			{
				return _customLevelsField.GetValue(target);
			}
		}
		return null;
	}

	private static IEnumerable<BeatmapLevel> EnumerateCustomLevels(object customLevels)
	{
		if (customLevels == null)
		{
			yield break;
		}
		if (customLevels is IDictionary dict)
		{
			foreach (object value in dict.Values)
			{
				if (value is BeatmapLevel level && level != null)
				{
					yield return level;
				}
			}
			yield break;
		}
		PropertyInfo valuesProp = customLevels.GetType().GetProperty("Values", BindingFlags.Instance | BindingFlags.Public);
		if (valuesProp?.GetValue(customLevels) is IEnumerable valuesEnum)
		{
			foreach (object value in valuesEnum)
			{
				if (value is BeatmapLevel level && level != null)
				{
					yield return level;
				}
			}
			yield break;
		}
		if (customLevels is IEnumerable enumerable)
		{
			foreach (object item in enumerable)
			{
				if (item is BeatmapLevel directLevel && directLevel != null)
				{
					yield return directLevel;
				}
				else if (item != null)
				{
					PropertyInfo valProp = item.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
					if (valProp?.GetValue(item) is BeatmapLevel kvLevel && kvLevel != null)
					{
						yield return kvLevel;
					}
				}
			}
		}
	}

	public static bool TryGetCustomLevel(string levelId, out BeatmapLevel level)
	{
		level = null;
		try
		{
			if (string.IsNullOrEmpty(levelId))
			{
				return false;
			}
			EnsureLoadingProbe();
			if (_customLevelsByIdField != null && _customLevelsByIdField.GetValue(null) is IDictionary byIdDict)
			{
				if (byIdDict.Contains(levelId) && byIdDict[levelId] is BeatmapLevel direct && direct != null)
				{
					level = direct;
					return true;
				}
				foreach (DictionaryEntry entry in byIdDict)
				{
					if (entry.Key is string key && string.Equals(key, levelId, StringComparison.OrdinalIgnoreCase) && entry.Value is BeatmapLevel matched && matched != null)
					{
						level = matched;
						return true;
					}
				}
			}
			object customLevels = GetCustomLevelsObject();
			if (customLevels == null)
			{
				return false;
			}
			foreach (BeatmapLevel item in EnumerateCustomLevels(customLevels))
			{
				if (item != null && string.Equals(item.levelID, levelId, StringComparison.OrdinalIgnoreCase))
				{
					level = item;
					return true;
				}
			}
			return false;
		}
		catch (Exception ex)
		{
			Plugin.Log?.Warn("[BeatWanko] SongCore level lookup failed: " + ex.Message);
			return false;
		}
	}

	public static bool IsLevelReadyToPlay(string levelId, out BeatmapLevel level)
	{
		level = null;
		try
		{
			if (string.IsNullOrEmpty(levelId) || !Available || IsLoading())
			{
				return false;
			}
			if (!TryGetCustomLevel(levelId, out level) || level == null)
			{
				level = null;
				return false;
			}
			string actualId = level.levelID ?? levelId;
			if (_loadedSaveDataPendingField != null && _loadedSaveDataPendingField.GetValue(null) is IDictionary pendingDict && ContainsKeyIgnoreCase(pendingDict, actualId))
			{
				return false;
			}
			object customLevelLoader = GetCustomLevelLoaderObject();
			if (customLevelLoader != null && _customLevelLoaderSaveDataField != null && _customLevelLoaderSaveDataField.GetValue(customLevelLoader) is IDictionary loadedDict && !ContainsKeyIgnoreCase(loadedDict, actualId))
			{
				return false;
			}
			object model = GetBeatmapLevelsModelObject();
			if (model != null && _getLevelPackForLevelIdMethod != null)
			{
				object pack = _getLevelPackForLevelIdMethod.Invoke(model, new object[1] { actualId });
				if (pack == null)
				{
					return false;
				}
			}
			return true;
		}
		catch (Exception ex)
		{
			Plugin.Log?.Warn("[BeatWanko] IsLevelReadyToPlay check failed: " + ex.Message);
			level = null;
			return false;
		}
	}

	public static int CustomLevelCount()
	{
		try
		{
			object customLevels = GetCustomLevelsObject();
			if (customLevels == null)
			{
				return -1;
			}
			if (customLevels is ICollection col)
			{
				return col.Count;
			}
			PropertyInfo countProp = customLevels.GetType().GetProperty("Count", BindingFlags.Instance | BindingFlags.Public);
			if (countProp != null && countProp.GetValue(customLevels) is int c)
			{
				return c;
			}
			return -1;
		}
		catch
		{
			return -1;
		}
	}

	public static bool IsCustomLevelLoaded(string levelId)
	{
		return TryGetCustomLevel(levelId, out _);
	}

	public static bool IsLoading()
	{
		if (!Available)
		{
			return false;
		}
		try
		{
			return IsLoadingImpl();
		}
		catch
		{
			return false;
		}
	}

	private static bool IsLoadingImpl()
	{
		EnsureLoadingProbe();
		if (_areSongsLoadingProp != null && _areSongsLoadingProp.GetValue(null) is bool areLoading)
		{
			return areLoading;
		}
		if (_isLoadingProp != null)
		{
			MethodInfo getter = _isLoadingProp.GetGetMethod(nonPublic: true);
			if (getter != null)
			{
				object target = (getter.IsStatic ? null : GetLoaderInstance());
				if ((getter.IsStatic || target != null) && _isLoadingProp.GetValue(target) is bool isLoading)
				{
					return isLoading;
				}
			}
		}
		return false;
	}

	public static void RefreshSongs()
	{
		TryRefreshSongs();
	}

	public static bool TryRefreshSongs()
	{
		try
		{
			if (!Available || IsLoadingImpl())
			{
				return false;
			}
			Type loaderType = GetLoaderType();
			object instance = GetLoaderInstance();
			if (loaderType == null || instance == null)
			{
				return false;
			}
			MethodInfo[] candidates = loaderType
				.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
				.Where((MethodInfo m) => m.Name == "RefreshSongs")
				.OrderBy((MethodInfo m) => m.GetParameters().Length)
				.ToArray();
			foreach (MethodInfo method in candidates)
			{
				ParameterInfo[] ps = method.GetParameters();
				object[] args = new object[ps.Length];
				bool ok = true;
				for (int i = 0; i < ps.Length; i++)
				{
					ParameterInfo p = ps[i];
					if (p.ParameterType == typeof(bool) && (string.Equals(p.Name, "fullRefresh", StringComparison.OrdinalIgnoreCase) || i == 0))
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
						ok = false;
						break;
					}
				}
				if (ok)
				{
					method.Invoke(instance, args);
					return true;
				}
			}
			return false;
		}
		catch (Exception ex)
		{
			Plugin.Log?.Warn("[BeatWanko] SongCore refresh failed: " + ex.Message);
			return false;
		}
	}

	public static List<BeatmapLevel> AllCustomLevels()
	{
		try
		{
			object customLevels = GetCustomLevelsObject();
			if (customLevels == null)
			{
				return new List<BeatmapLevel>();
			}
			return EnumerateCustomLevels(customLevels).Where((BeatmapLevel l) => l != null).ToList();
		}
		catch (Exception ex)
		{
			Plugin.Log?.Warn("[BeatWanko] SongCore level enumeration failed: " + ex.Message);
			return new List<BeatmapLevel>();
		}
	}
}
