using System.Collections;
using UnityEngine;

namespace BeatWanko;

internal class BeatWankoRunner : MonoBehaviour
{
	public Coroutine Run(IEnumerator routine)
	{
		return StartCoroutine(routine);
	}

	public void Stop(Coroutine routine)
	{
		if (routine != null)
		{
			StopCoroutine(routine);
		}
	}

	private void Update()
	{
		MainThread.Pump();
	}
}
