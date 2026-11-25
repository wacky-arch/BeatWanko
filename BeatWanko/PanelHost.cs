using BeatSaberMarkupLanguage.Attributes;
using TMPro;

namespace BeatWanko;

internal sealed class PanelHost
{
	[UIComponent("mode-title")]
	public TextMeshProUGUI ModeTitleText;

	[UIComponent("status-text")]
	public TextMeshProUGUI StatusText;

	[UIComponent("song-text")]
	public TextMeshProUGUI SongText;

	[UIComponent("count-text")]
	public TextMeshProUGUI CountText;

	[UIAction("close-lid")]
	public void OnCloseLid()
	{
		Session.RequestCloseLid();
	}
}
