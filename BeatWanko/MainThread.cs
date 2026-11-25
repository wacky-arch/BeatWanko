using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BeatWanko;

internal static class MainThread
{
	private static readonly Queue<Action> Queue = new Queue<Action>();

	public static void Post(Action action)
	{
		if (action == null)
		{
			return;
		}
		lock (Queue)
		{
			Queue.Enqueue(action);
		}
	}

	public static Task Run(Action action)
	{
		TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
		Post(delegate
		{
			try
			{
				action();
				tcs.TrySetResult(result: true);
			}
			catch (Exception exception)
			{
				tcs.TrySetException(exception);
			}
		});
		return tcs.Task;
	}

	public static void Pump()
	{
		while (true)
		{
			Action action = null;
			lock (Queue)
			{
				if (Queue.Count > 0)
				{
					action = Queue.Dequeue();
				}
			}
			if (action == null)
			{
				break;
			}
			try
			{
				action();
			}
			catch (Exception arg)
			{
				Plugin.Log?.Error($"[BeatWanko] Main-thread action failed: {arg}");
			}
		}
	}
}
