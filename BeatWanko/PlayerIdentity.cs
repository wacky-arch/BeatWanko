using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HMUI;
using UnityEngine;

namespace BeatWanko;

internal sealed class ResolvedPlayerIdentity
{
	public string PlayerId { get; set; }

	public string PlatformUserName { get; set; }

	public string Platform { get; set; }

	public bool IsValid => !string.IsNullOrEmpty(PlayerId) && PlayerId != "0";
}

internal static class PlayerIdentity
{
	private static readonly object Sync = new object();

	private static object _cachedPlatformUserModel;

	private static Task<ResolvedPlayerIdentity> _pendingPlatformTask;

	private static ResolvedPlayerIdentity _cached;

	public static ResolvedPlayerIdentity Cached
	{
		get
		{
			lock (Sync)
			{
				return _cached;
			}
		}
	}

	public static void CaptureOnMainThread()
	{
		try
		{
			if (_cachedPlatformUserModel == null)
			{
				PlatformLeaderboardsModel[] models = Resources.FindObjectsOfTypeAll<PlatformLeaderboardsModel>();
				foreach (PlatformLeaderboardsModel model in models)
				{
					if ((UnityEngine.Object)(object)model == null)
					{
						continue;
					}
					object userModel = FindFieldValueInHierarchy(model, "_platformUserModel");
					if (userModel != null)
					{
						_cachedPlatformUserModel = userModel;
						break;
					}
				}
			}
			if (_cachedPlatformUserModel == null)
			{
				_cachedPlatformUserModel = TryResolveFromZenject("IPlatformUserModel");
			}
			StartPlatformUserTaskIfPossible();
			if (Cached == null || !Cached.IsValid)
			{
				TryCaptureOculusStudiosPlatformOnMainThread();
			}
			if (Cached == null || !Cached.IsValid)
			{
				TryCaptureSteamSync();
			}
		}
		catch (Exception ex)
		{
			Plugin.Log?.Debug("[BeatWanko] PlayerIdentity.CaptureOnMainThread warning: " + ex.Message);
		}
	}

	public static void KickResolve()
	{
		CaptureOnMainThread();
		Task.Run(async delegate
		{
			try
			{
				await ResolveAsync(CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch
			{
			}
		});
	}

	public static async Task<ResolvedPlayerIdentity> ResolveAsync(CancellationToken token)
	{
		ResolvedPlayerIdentity existing = Cached;
		if (existing != null && existing.IsValid && !string.IsNullOrEmpty(existing.PlatformUserName))
		{
			return existing;
		}
		Task<ResolvedPlayerIdentity> pending;
		lock (Sync)
		{
			pending = _pendingPlatformTask;
		}
		if (pending == null)
		{
			try
			{
				await MainThread.Run(CaptureOnMainThread).ConfigureAwait(continueOnCapturedContext: false);
				lock (Sync)
				{
					pending = _pendingPlatformTask;
				}
			}
			catch
			{
			}
		}
		if (pending != null)
		{
			try
			{
				Task completed = await Task.WhenAny(pending, Task.Delay(4000, token)).ConfigureAwait(continueOnCapturedContext: false);
				if (completed == pending && !pending.IsFaulted && !pending.IsCanceled)
				{
					ResolvedPlayerIdentity fromModel = pending.Result;
					if (fromModel != null && fromModel.IsValid)
					{
						SetCached(fromModel);
						return fromModel;
					}
				}
			}
			catch (Exception ex)
			{
				Plugin.Log?.Debug("[BeatWanko] PlatformUserModel await failed: " + ex.Message);
			}
		}
		ResolvedPlayerIdentity current = Cached;
		if (current != null && current.IsValid)
		{
			return current;
		}
		ResolvedPlayerIdentity fromSteam = TryCaptureSteamSync();
		if (fromSteam != null && fromSteam.IsValid)
		{
			return fromSteam;
		}
		return Cached;
	}

	private static void StartPlatformUserTaskIfPossible()
	{
		object userModel = _cachedPlatformUserModel;
		if (userModel == null)
		{
			return;
		}
		lock (Sync)
		{
			if (_pendingPlatformTask != null && !_pendingPlatformTask.IsFaulted && !_pendingPlatformTask.IsCanceled)
			{
				return;
			}
		}
		try
		{
			Task<ResolvedPlayerIdentity> task = StartPlatformUserInfoTask(userModel);
			if (task != null)
			{
				lock (Sync)
				{
					_pendingPlatformTask = task;
				}
			}
		}
		catch (Exception ex)
		{
			Plugin.Log?.Debug("[BeatWanko] GetUserInfo start failed: " + ex.Message);
		}
	}

	private static async Task<ResolvedPlayerIdentity> StartPlatformUserInfoTask(object userModel)
	{
		MethodInfo method = userModel.GetType()
			.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
			.Where((MethodInfo m) => m.Name == "GetUserInfo")
			.OrderBy((MethodInfo m) => m.GetParameters().Length)
			.FirstOrDefault();
		if (method == null)
		{
			return null;
		}
		ParameterInfo[] ps = method.GetParameters();
		object[] args = new object[ps.Length];
		for (int i = 0; i < ps.Length; i++)
		{
			if (ps[i].ParameterType == typeof(CancellationToken))
			{
				args[i] = CancellationToken.None;
			}
			else if (ps[i].HasDefaultValue)
			{
				args[i] = ps[i].DefaultValue;
			}
		}
		object taskObj = method.Invoke(userModel, args);
		if (taskObj is not Task rawTask)
		{
			return null;
		}
		await rawTask.ConfigureAwait(continueOnCapturedContext: false);
		object userInfo = rawTask.GetType().GetProperty("Result", BindingFlags.Instance | BindingFlags.Public)?.GetValue(rawTask);
		if (userInfo == null)
		{
			return null;
		}
		Type uiType = userInfo.GetType();
		string platformUserId = (uiType.GetField("platformUserId")?.GetValue(userInfo) ?? uiType.GetProperty("platformUserId")?.GetValue(userInfo))?.ToString();
		string userName = (uiType.GetField("userName")?.GetValue(userInfo) ?? uiType.GetProperty("userName")?.GetValue(userInfo))?.ToString();
		string platform = (uiType.GetField("platform")?.GetValue(userInfo) ?? uiType.GetProperty("platform")?.GetValue(userInfo))?.ToString();
		if (string.IsNullOrEmpty(platformUserId) || platformUserId == "0")
		{
			return null;
		}
		return new ResolvedPlayerIdentity
		{
			PlayerId = platformUserId.Trim(),
			PlatformUserName = (userName ?? string.Empty).Trim(),
			Platform = (platform ?? "Unknown")
		};
	}

	private static void SetCached(ResolvedPlayerIdentity id)
	{
		if (id == null || !id.IsValid)
		{
			return;
		}
		lock (Sync)
		{
			bool changed = _cached == null || _cached.PlayerId != id.PlayerId || (!string.IsNullOrEmpty(id.PlatformUserName) && string.IsNullOrEmpty(_cached.PlatformUserName));
			_cached = id;
			if (changed)
			{
				Plugin.Log?.Info($"[BeatWanko] PlayerIdentity resolved: id={id.PlayerId}, name='{id.PlatformUserName}', platform={id.Platform}");
			}
		}
	}

	private static object FindFieldValueInHierarchy(object instance, string fieldName)
	{
		if (instance == null)
		{
			return null;
		}
		for (Type t = instance.GetType(); t != null; t = t.BaseType)
		{
			FieldInfo field = t.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
			if (field != null)
			{
				return field.GetValue(instance);
			}
		}
		return null;
	}

	internal static Type FindSteamUserType()
	{
		try
		{
			string[] preferredAsmNames = new string[2] { "com.rlabrecque.steamworks.net", "Steamworks.NET" };
			Assembly[] loaded = AppDomain.CurrentDomain.GetAssemblies();
			foreach (string asmName in preferredAsmNames)
			{
				Assembly asm = loaded.FirstOrDefault((Assembly a) => string.Equals(a.GetName().Name, asmName, StringComparison.OrdinalIgnoreCase));
				Type t = asm?.GetType("Steamworks.SteamUser", throwOnError: false);
				if (t != null)
				{
					return t;
				}
			}
			foreach (Assembly asm in loaded)
			{
				try
				{
					Type t = asm.GetType("Steamworks.SteamUser", throwOnError: false);
					if (t != null)
					{
						return t;
					}
				}
				catch
				{
				}
			}
			string managedDir = null;
			try
			{
				if (!string.IsNullOrEmpty(Application.dataPath))
				{
					managedDir = Path.Combine(Application.dataPath, "Managed");
				}
			}
			catch
			{
			}
			if (!string.IsNullOrEmpty(managedDir) && Directory.Exists(managedDir))
			{
				string[] candidateDlls = new string[2] { "com.rlabrecque.steamworks.net.dll", "Steamworks.NET.dll" };
				foreach (string dllName in candidateDlls)
				{
					string fullPath = Path.Combine(managedDir, dllName);
					if (File.Exists(fullPath))
					{
						Assembly loadedAsm = Assembly.LoadFrom(fullPath);
						Type t = loadedAsm?.GetType("Steamworks.SteamUser", throwOnError: false);
						if (t != null)
						{
							return t;
						}
					}
				}
			}
		}
		catch (Exception ex)
		{
			Plugin.Log?.Debug("[BeatWanko] FindSteamUserType warning: " + ex.Message);
		}
		return null;
	}

	private static object TryResolveFromZenject(string typeSimpleOrFullName)
	{
		if (string.IsNullOrEmpty(typeSimpleOrFullName))
		{
			return null;
		}
		try
		{
			Type targetType = null;
			foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
			{
				try
				{
					targetType = asm.GetType(typeSimpleOrFullName, throwOnError: false);
					if (targetType == null && typeSimpleOrFullName.IndexOf('.') < 0)
					{
						targetType = asm.GetTypes().FirstOrDefault((Type t) => string.Equals(t.Name, typeSimpleOrFullName, StringComparison.Ordinal));
					}
					if (targetType != null)
					{
						break;
					}
				}
				catch
				{
				}
			}
			if (targetType == null)
			{
				return null;
			}
			Type pcType = AppDomain.CurrentDomain.GetAssemblies()
				.Select((Assembly a) => a.GetType("Zenject.ProjectContext", throwOnError: false))
				.FirstOrDefault((Type t) => t != null);
			if (pcType != null)
			{
				bool hasInstance = (bool)(pcType.GetProperty("HasInstance", BindingFlags.Static | BindingFlags.Public)?.GetValue(null) ?? false);
				if (hasInstance)
				{
					object pc = pcType.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
					object container = pc?.GetType().GetProperty("Container", BindingFlags.Instance | BindingFlags.Public)?.GetValue(pc);
					object resolved = InvokeContainerTryResolve(container, targetType);
					if (resolved != null)
					{
						return resolved;
					}
				}
			}
			Type scType = AppDomain.CurrentDomain.GetAssemblies()
				.Select((Assembly a) => a.GetType("Zenject.SceneContext", throwOnError: false))
				.FirstOrDefault((Type t) => t != null);
			if (scType != null)
			{
				UnityEngine.Object[] contexts = Resources.FindObjectsOfTypeAll(scType);
				foreach (UnityEngine.Object ctx in contexts)
				{
					if (ctx == null)
					{
						continue;
					}
					object container = scType.GetProperty("Container", BindingFlags.Instance | BindingFlags.Public)?.GetValue(ctx);
					object resolved = InvokeContainerTryResolve(container, targetType);
					if (resolved != null)
					{
						return resolved;
					}
				}
			}
		}
		catch (Exception ex)
		{
			Plugin.Log?.Debug("[BeatWanko] TryResolveFromZenject warning: " + ex.Message);
		}
		return null;
	}

	private static object InvokeContainerTryResolve(object container, Type targetType)
	{
		if (container == null || targetType == null)
		{
			return null;
		}
		MethodInfo tryResolve = container.GetType()
			.GetMethods(BindingFlags.Instance | BindingFlags.Public)
			.FirstOrDefault((MethodInfo m) => m.Name == "TryResolve" && !m.IsGenericMethod && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(Type));
		return tryResolve?.Invoke(container, new object[1] { targetType });
	}

	private static ResolvedPlayerIdentity TryCaptureOculusStudiosPlatformOnMainThread()
	{
		try
		{
			object zenjectPlatform = TryResolveFromZenject("OculusStudios.Platform.Core.IPlatform");
			ResolvedPlayerIdentity zenjectId = TryExtractFromOculusPlatform(zenjectPlatform);
			if (zenjectId != null && zenjectId.IsValid)
			{
				SetCached(zenjectId);
				return zenjectId;
			}
			FlowCoordinator[] coordinators = Resources.FindObjectsOfTypeAll<FlowCoordinator>();
			foreach (FlowCoordinator fc in coordinators)
			{
				if ((UnityEngine.Object)(object)fc == null)
				{
					continue;
				}
				object platformObj = FindFieldValueInHierarchy(fc, "_platform") ?? FindFieldValueByTypeNameInHierarchy(fc, "OculusStudios.Platform.Core.IPlatform");
				ResolvedPlayerIdentity extracted = TryExtractFromOculusPlatform(platformObj);
				if (extracted != null && extracted.IsValid)
				{
					SetCached(extracted);
					return extracted;
				}
			}
			PlatformLeaderboardsModel[] models = Resources.FindObjectsOfTypeAll<PlatformLeaderboardsModel>();
			foreach (PlatformLeaderboardsModel model in models)
			{
				if ((UnityEngine.Object)(object)model == null)
				{
					continue;
				}
				object platformObj = FindFieldValueInHierarchy(model, "_platform") ?? FindFieldValueByTypeNameInHierarchy(model, "OculusStudios.Platform.Core.IPlatform");
				ResolvedPlayerIdentity extracted = TryExtractFromOculusPlatform(platformObj);
				if (extracted != null && extracted.IsValid)
				{
					SetCached(extracted);
					return extracted;
				}
			}
			foreach (MonoBehaviour mb in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
			{
				if ((UnityEngine.Object)(object)mb == null)
				{
					continue;
				}
				string fullName = mb.GetType().FullName ?? string.Empty;
				if (fullName.IndexOf("Platform", StringComparison.OrdinalIgnoreCase) < 0 && fullName.IndexOf("ProjectContext", StringComparison.OrdinalIgnoreCase) < 0)
				{
					continue;
				}
				object platformObj = FindFieldValueByTypeNameInHierarchy(mb, "OculusStudios.Platform.Core.IPlatform");
				ResolvedPlayerIdentity extracted = TryExtractFromOculusPlatform(platformObj);
				if (extracted != null && extracted.IsValid)
				{
					SetCached(extracted);
					return extracted;
				}
			}
		}
		catch (Exception ex)
		{
			Plugin.Log?.Debug("[BeatWanko] TryCaptureOculusStudiosPlatformOnMainThread warning: " + ex.Message);
		}
		return null;
	}

	private static object FindFieldValueByTypeNameInHierarchy(object instance, string targetTypeFullName)
	{
		if (instance == null || string.IsNullOrEmpty(targetTypeFullName))
		{
			return null;
		}
		for (Type t = instance.GetType(); t != null; t = t.BaseType)
		{
			foreach (FieldInfo field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
			{
				try
				{
					string fType = field.FieldType?.FullName;
					if (string.Equals(fType, targetTypeFullName, StringComparison.Ordinal) || (fType != null && fType.IndexOf("IPlatform", StringComparison.Ordinal) >= 0 && fType.IndexOf("OculusStudios.Platform", StringComparison.Ordinal) >= 0))
					{
						object val = field.GetValue(instance);
						if (val != null)
						{
							return val;
						}
					}
				}
				catch
				{
				}
			}
		}
		return null;
	}

	private static ResolvedPlayerIdentity TryExtractFromOculusPlatform(object platformObj)
	{
		if (platformObj == null)
		{
			return null;
		}
		try
		{
			Type pType = platformObj.GetType();
			PropertyInfo userProp = pType.GetProperty("user", BindingFlags.Instance | BindingFlags.Public);
			object userObj = userProp?.GetValue(platformObj);
			if (userObj == null)
			{
				return null;
			}
			Type uType = userObj.GetType();
			object idVal = uType.GetProperty("userId", BindingFlags.Instance | BindingFlags.Public)?.GetValue(userObj)
				?? uType.GetField("userId", BindingFlags.Instance | BindingFlags.Public)?.GetValue(userObj);
			string idStr = idVal?.ToString()?.Trim();
			if (string.IsNullOrEmpty(idStr) || idStr == "0")
			{
				return null;
			}
			string displayName = (uType.GetProperty("displayName", BindingFlags.Instance | BindingFlags.Public)?.GetValue(userObj)
				?? uType.GetField("displayName", BindingFlags.Instance | BindingFlags.Public)?.GetValue(userObj))?.ToString()?.Trim() ?? string.Empty;
			string vendor = pType.GetProperty("vendor", BindingFlags.Instance | BindingFlags.Public)?.GetValue(platformObj)?.ToString()?.Trim();
			string platformName = string.IsNullOrEmpty(vendor) ? "OculusStudiosPlatform" : ("OculusStudiosPlatform/" + vendor);
			return new ResolvedPlayerIdentity
			{
				PlayerId = idStr,
				PlatformUserName = displayName,
				Platform = platformName
			};
		}
		catch
		{
			return null;
		}
	}

	private static ResolvedPlayerIdentity TryCaptureSteamSync()
	{
		try
		{
			Type steamUserType = FindSteamUserType();
			if (steamUserType == null)
			{
				return null;
			}
			MethodInfo getSteamId = steamUserType.GetMethod("GetSteamID", BindingFlags.Static | BindingFlags.Public);
			if (getSteamId == null)
			{
				return null;
			}
			object steamIdObj = getSteamId.Invoke(null, null);
			string idStr = steamIdObj?.ToString()?.Trim();
			if (string.IsNullOrEmpty(idStr) || idStr == "0")
			{
				return null;
			}
			string personaName = string.Empty;
			try
			{
				Type steamFriendsType = steamUserType.Assembly.GetType("Steamworks.SteamFriends");
				MethodInfo getName = steamFriendsType?.GetMethod("GetPersonaName", BindingFlags.Static | BindingFlags.Public);
				personaName = getName?.Invoke(null, null)?.ToString()?.Trim() ?? string.Empty;
			}
			catch
			{
			}
			ResolvedPlayerIdentity identity = new ResolvedPlayerIdentity
			{
				PlayerId = idStr,
				PlatformUserName = personaName,
				Platform = "Steam"
			};
			SetCached(identity);
			return identity;
		}
		catch
		{
			return null;
		}
	}
}
