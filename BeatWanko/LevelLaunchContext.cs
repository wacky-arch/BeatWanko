using System;
using System.Reflection;

namespace BeatWanko;

internal sealed class LevelLaunchContext
{
	public MenuTransitionsHelper Helper;

	public MethodInfo Method;

	public object[] Args;

	public int BeatmapKeyIndex;

	public int BeatmapLevelIndex;

	public int PracticeSettingsIndex;

	public int LevelFinishedIndex;

	public Action<StandardLevelScenesTransitionSetupDataSO, LevelCompletionResults> OriginalLevelFinished;

	public BeatmapKey CurrentKey;

	public BeatmapLevel CurrentLevel;

	public string GameMode;
}
