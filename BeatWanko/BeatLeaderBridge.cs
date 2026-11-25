using System;
using System.Reflection;

namespace BeatWanko;

internal static class BeatLeaderBridge
{
	private static bool _probed;

	private static MethodInfo _onActionButtonWasPressed;

	private static FieldInfo _shouldRecordField;

	private static void EnsureProbed()
	{
		if (_probed)
		{
			return;
		}
		_probed = true;
		try
		{
			Type type = Type.GetType("BeatLeader.Utils.RecorderUtils, BeatLeader");
			if (type == null)
			{
				Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
				foreach (Assembly assembly in assemblies)
				{
					if (string.Equals(assembly.GetName().Name, "BeatLeader", StringComparison.OrdinalIgnoreCase))
					{
						type = assembly.GetType("BeatLeader.Utils.RecorderUtils");
						break;
					}
				}
			}
			if (!(type == null))
			{
				_onActionButtonWasPressed = type.GetMethod("OnActionButtonWasPressed", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
				_shouldRecordField = type.GetField("shouldRecord", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			}
		}
		catch (Exception ex)
		{
			Plugin.Log?.Warn("[BeatWanko] BeatLeader probe failed: " + ex.Message);
		}
	}

	public static void ArmRecorderForSoloLaunch()
	{
		try
		{
			EnsureProbed();
			if (_onActionButtonWasPressed != null)
			{
				_onActionButtonWasPressed.Invoke(null, null);
				Plugin.Log?.Info("[BeatWanko] Armed BeatLeader replay recorder (RecorderUtils.OnActionButtonWasPressed).");
			}
			else if (_shouldRecordField != null && _shouldRecordField.FieldType == typeof(bool))
			{
				_shouldRecordField.SetValue(null, true);
				Plugin.Log?.Info("[BeatWanko] Armed BeatLeader replay recorder (RecorderUtils.shouldRecord = true).");
			}
		}
		catch (Exception ex)
		{
			Plugin.Log?.Warn("[BeatWanko] Arming BeatLeader replay recorder failed: " + ex.Message);
		}
	}
}
