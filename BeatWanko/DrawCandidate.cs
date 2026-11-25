namespace BeatWanko;

internal sealed class DrawCandidate
{
	public string HashUpper;

	public string LevelId;

	public string DisplayName;

	public int DifficultyRank;

	public string CharacteristicName;

	public float Stars;

	public int StepBand;

	public bool StrictDifficulty;

	public BeatmapLevel OwnedLevel;

	public BeatmapKey? PrebuiltKey;
}
