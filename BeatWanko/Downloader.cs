using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace BeatWanko;

internal static class Downloader
{
	private static readonly HttpClient Http = CreateClient();

	private static readonly object StateLock = new object();

	private static string _lastExtractedFolder;

	private static bool _lastExtractionCompleted;

	internal static volatile bool IsWaitingForSongCoreRegistration;

	private static HttpClient CreateClient()
	{
		try
		{
			ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
		}
		catch
		{
		}
		HttpClient httpClient = new HttpClient();
		httpClient.Timeout = Timeout.InfiniteTimeSpan;
		httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("BeatWanko/" + LeaderboardClient.ModVersion + " (+https://github.com/wacky-arch/BeatWanko)");
		return httpClient;
	}

	public static async Task<BeatmapLevel> DownloadAndLoadAsync(DrawCandidate draw, CancellationToken token)
	{
		IsWaitingForSongCoreRegistration = false;
		try
		{
			if (!(await DownloadAndExtractAsync(draw, token).ConfigureAwait(continueOnCapturedContext: false)))
			{
				return null;
			}
			IsWaitingForSongCoreRegistration = true;
			string levelId = draw.LevelId;

			async Task<BeatmapLevel> TryGetReadyLevelOnMainThreadAsync()
			{
				if (SongCoreBridge.IsLoading())
				{
					return null;
				}
				BeatmapLevel ready = null;
				await MainThread.Run(delegate
				{
					SongCoreBridge.IsLevelReadyToPlay(levelId, out ready);
				}).ConfigureAwait(continueOnCapturedContext: false);
				return ready;
			}

			BeatmapLevel fastReady = await TryGetReadyLevelOnMainThreadAsync().ConfigureAwait(continueOnCapturedContext: false);
			if (fastReady != null)
			{
				Plugin.Log?.Info("[BeatWanko] Downloaded song is loaded and ready: '" + draw.DisplayName + "' (" + (fastReady.levelID ?? levelId) + ").");
				return fastReady;
			}

			for (int attempt = 1; attempt <= 2; attempt++)
			{
				int pollSeconds = ((attempt == 1) ? 25 : 20);
				Stopwatch watch = Stopwatch.StartNew();
				if (SongCoreBridge.IsLoading())
				{
					Plugin.Log?.Info("[BeatWanko] SongCore is already scanning; waiting for current scan before refreshing for '" + draw.DisplayName + "'.");
					while (SongCoreBridge.IsLoading() && watch.Elapsed.TotalSeconds < (double)pollSeconds)
					{
						token.ThrowIfCancellationRequested();
						await Task.Delay(150, token).ConfigureAwait(continueOnCapturedContext: false);
					}
					BeatmapLevel afterWaitReady = await TryGetReadyLevelOnMainThreadAsync().ConfigureAwait(continueOnCapturedContext: false);
					if (afterWaitReady != null)
					{
						Plugin.Log?.Info("[BeatWanko] Downloaded song is loaded and ready: '" + draw.DisplayName + "' (" + (afterWaitReady.levelID ?? levelId) + ").");
						return afterWaitReady;
					}
				}
				int countBefore = SongCoreBridge.CustomLevelCount();
				bool refreshStarted = false;
				await MainThread.Run(delegate
				{
					refreshStarted = SongCoreBridge.TryRefreshSongs();
				}).ConfigureAwait(continueOnCapturedContext: false);
				bool sawLoading = refreshStarted && SongCoreBridge.IsLoading();
				Stopwatch postLoadingWatch = null;
				while (watch.Elapsed.TotalSeconds < (double)pollSeconds)
				{
					token.ThrowIfCancellationRequested();
					bool loading = SongCoreBridge.IsLoading();
					if (loading)
					{
						sawLoading = true;
						postLoadingWatch = null;
					}
					else
					{
						BeatmapLevel readyLevel = await TryGetReadyLevelOnMainThreadAsync().ConfigureAwait(continueOnCapturedContext: false);
						if (readyLevel != null)
						{
							Plugin.Log?.Info("[BeatWanko] Downloaded song is loaded and ready: '" + draw.DisplayName + "' (" + (readyLevel.levelID ?? levelId) + ").");
							return readyLevel;
						}
						if (postLoadingWatch == null)
						{
							postLoadingWatch = Stopwatch.StartNew();
						}
						bool inCustomLevels = SongCoreBridge.TryGetCustomLevel(levelId, out _);
						double postElapsed = postLoadingWatch.Elapsed.TotalSeconds;
						if (sawLoading)
						{
							if (postElapsed >= (inCustomLevels ? 3.0 : 0.8))
							{
								break;
							}
						}
						else if (postElapsed >= (inCustomLevels ? 3.0 : 1.5))
						{
							break;
						}
					}
					await Task.Delay(150, token).ConfigureAwait(continueOnCapturedContext: false);
				}
				int num2 = SongCoreBridge.CustomLevelCount();
				Plugin.Log?.Info($"[BeatWanko] SongCore refresh #{attempt} for '{draw.DisplayName}': custom levels {countBefore} -> {num2}.");
				if (attempt == 1)
				{
					Plugin.Log?.Warn($"[BeatWanko] '{draw.DisplayName}' not ready after refresh #1 (custom levels {countBefore} -> {num2}); retrying refresh.");
				}
			}
			Plugin.Log?.Warn("[BeatWanko] Song did not become ready in SongCore/BeatmapLevelsModel after download: " + levelId);
			return null;
		}
		catch (OperationCanceledException)
		{
			Plugin.Log?.Info("[BeatWanko] Download cancelled for '" + draw.DisplayName + "'.");
			return null;
		}
		catch (Exception ex2)
		{
			Plugin.Log?.Error("[BeatWanko] Download failed for '" + draw.DisplayName + "': " + ex2.Message);
			return null;
		}
		finally
		{
			IsWaitingForSongCoreRegistration = false;
		}
	}

	private static async Task<bool> DownloadAndExtractAsync(DrawCandidate draw, CancellationToken token)
	{
		string hashLower = draw.HashUpper?.ToLowerInvariant();
		if (string.IsNullOrEmpty(hashLower))
		{
			return false;
		}
		string text;
		string text2;
		string text3;
		string text4;
		using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "https://api.beatsaver.com/maps/hash/" + hashLower))
		{
			using HttpResponseMessage response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(continueOnCapturedContext: false);
			response.EnsureSuccessStatusCode();
			JObject jObject = JObject.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(continueOnCapturedContext: false));
			text = (string?)jObject["id"];
			text2 = ((string?)jObject["metadata"]?["songName"]) ?? draw.DisplayName;
			text3 = ((string?)jObject["metadata"]?["levelAuthorName"]) ?? "";
			JArray jArray = (JArray)jObject["versions"];
			if (jArray == null || jArray.Count == 0)
			{
				return false;
			}
			text4 = null;
			foreach (JToken item in jArray)
			{
				string text5 = (string?)item?["hash"];
				string text6 = (string?)item?["downloadURL"];
				if (!string.IsNullOrEmpty(text5) && string.Equals(text5, hashLower, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(text6))
				{
					text4 = text6;
					break;
				}
			}
			if (string.IsNullOrEmpty(text4))
			{
				text4 = (string?)jArray[0]["downloadURL"];
				Plugin.Log?.Warn("[BeatWanko] Exact hash " + hashLower + " not found in BeatSaver versions[] for '" + draw.DisplayName + "'; falling back to versions[0].");
			}
			if (string.IsNullOrEmpty(text4))
			{
				return false;
			}
		}
		string customLevelsDirectory = SongCoreBridge.CustomLevelsDirectory;
		if (string.IsNullOrEmpty(customLevelsDirectory))
		{
			return false;
		}
		Directory.CreateDirectory(customLevelsDirectory);
		string folderName = SanitizeFolderName(text + " (" + text2 + " - " + text3 + ")");
		string destination = Path.Combine(customLevelsDirectory, folderName);
		lock (StateLock)
		{
			_lastExtractedFolder = destination;
			_lastExtractionCompleted = false;
		}
		if (Directory.Exists(destination) && IsCompleteBeatmapFolder(destination))
		{
			lock (StateLock)
			{
				if (string.Equals(_lastExtractedFolder, destination, StringComparison.OrdinalIgnoreCase))
				{
					_lastExtractionCompleted = true;
				}
			}
			Plugin.Log?.Info("[BeatWanko] '" + draw.DisplayName + "' is already downloaded on disk ('" + folderName + "'); skipping the download, refreshing only.");
			return true;
		}
		byte[] buffer;
		using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, text4))
		{
			using HttpResponseMessage response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(continueOnCapturedContext: false);
			response.EnsureSuccessStatusCode();
			using Stream stream2 = await response.Content.ReadAsStreamAsync().ConfigureAwait(continueOnCapturedContext: false);
			using MemoryStream memory = new MemoryStream();
			await stream2.CopyToAsync(memory, 81920, token).ConfigureAwait(continueOnCapturedContext: false);
			buffer = memory.ToArray();
		}
		token.ThrowIfCancellationRequested();
		bool createdDestination = false;
		bool extractionCompleted = false;
		try
		{
			if (Directory.Exists(destination))
			{
				Directory.Delete(destination, recursive: true);
			}
			Directory.CreateDirectory(destination);
			createdDestination = true;
			string destinationFull = Path.GetFullPath(destination);
			using (ZipArchive archive = new ZipArchive(new MemoryStream(buffer), ZipArchiveMode.Read))
			{
				foreach (ZipArchiveEntry entry in archive.Entries)
				{
					token.ThrowIfCancellationRequested();
					if (string.IsNullOrEmpty(entry.Name))
					{
						continue;
					}
					string fullPath = Path.GetFullPath(Path.Combine(destinationFull, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
					char directorySeparatorChar = Path.DirectorySeparatorChar;
					if (!fullPath.StartsWith(destinationFull + directorySeparatorChar, StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}
					Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
					using Stream stream2 = entry.Open();
					using FileStream fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write);
					await stream2.CopyToAsync(fileStream, 81920, token).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
			extractionCompleted = true;
			lock (StateLock)
			{
				if (string.Equals(_lastExtractedFolder, destination, StringComparison.OrdinalIgnoreCase))
				{
					_lastExtractionCompleted = true;
				}
			}
			Plugin.Log?.Info("[BeatWanko] Downloaded '" + draw.DisplayName + "' into '" + folderName + "'.");
			return true;
		}
		finally
		{
			if (createdDestination && !extractionCompleted)
			{
				try
				{
					if (Directory.Exists(destination))
					{
						Directory.Delete(destination, recursive: true);
					}
				}
				catch
				{
				}
			}
		}
	}

	private static bool IsCompleteBeatmapFolder(string folder)
	{
		try
		{
			string text = Path.Combine(folder, "info.dat");
			if (!File.Exists(text))
			{
				text = Path.Combine(folder, "Info.dat");
			}
			if (!File.Exists(text) || new FileInfo(text).Length <= 0)
			{
				return false;
			}
			JObject jObject = JObject.Parse(File.ReadAllText(text));
			string fullPath = Path.GetFullPath(folder);
			if (jObject["_difficultyBeatmapSets"] is JArray jArray)
			{
				string text2 = (string?)jObject["_songFilename"];
				if (string.IsNullOrEmpty(text2) || !IsNonEmptyChildFile(fullPath, text2))
				{
					return false;
				}
				int num = 0;
				foreach (JToken item in jArray)
				{
					if (!(item?["_difficultyBeatmaps"] is JArray jArray2))
					{
						continue;
					}
					foreach (JToken item2 in jArray2)
					{
						string text3 = (string?)item2?["_beatmapFilename"];
						if (string.IsNullOrEmpty(text3) || !IsNonEmptyChildFile(fullPath, text3))
						{
							return false;
						}
						num++;
					}
				}
				return num > 0;
			}
			if (jObject["difficultyBeatmaps"] is JArray jArray3)
			{
				string text4 = (string?)jObject["audio"]?["songFilename"];
				if (!string.IsNullOrEmpty(text4))
				{
					if (!IsNonEmptyChildFile(fullPath, text4))
					{
						return false;
					}
				}
				else if (!HasAnyNonEmptyAudioFile(fullPath))
				{
					return false;
				}
				int num2 = 0;
				foreach (JToken item3 in jArray3)
				{
					string text5 = (string?)item3?["beatmapDataFilename"];
					if (string.IsNullOrEmpty(text5) || !IsNonEmptyChildFile(fullPath, text5))
					{
						return false;
					}
					num2++;
				}
				return num2 > 0;
			}
			bool flag = false;
			foreach (string item4 in Directory.EnumerateFiles(fullPath, "*.dat", SearchOption.TopDirectoryOnly))
			{
				string fileName = Path.GetFileName(item4);
				if (!string.Equals(fileName, "info.dat", StringComparison.OrdinalIgnoreCase) && !string.Equals(fileName, "BPMInfo.dat", StringComparison.OrdinalIgnoreCase) && new FileInfo(item4).Length > 0)
				{
					flag = true;
					break;
				}
			}
			return flag && HasAnyNonEmptyAudioFile(fullPath);
		}
		catch
		{
			return false;
		}
	}

	private static bool IsNonEmptyChildFile(string folderFull, string relativePath)
	{
		string fullPath = Path.GetFullPath(Path.Combine(folderFull, relativePath.Replace('/', Path.DirectorySeparatorChar)));
		char directorySeparatorChar = Path.DirectorySeparatorChar;
		if (!fullPath.StartsWith(folderFull + directorySeparatorChar, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		if (File.Exists(fullPath))
		{
			return new FileInfo(fullPath).Length > 0;
		}
		return false;
	}

	private static bool HasAnyNonEmptyAudioFile(string folderFull)
	{
		foreach (string item in Directory.EnumerateFiles(folderFull, "*.*", SearchOption.TopDirectoryOnly))
		{
			string extension = Path.GetExtension(item);
			if ((string.Equals(extension, ".egg", StringComparison.OrdinalIgnoreCase) || string.Equals(extension, ".ogg", StringComparison.OrdinalIgnoreCase) || string.Equals(extension, ".wav", StringComparison.OrdinalIgnoreCase)) && new FileInfo(item).Length > 0)
			{
				return true;
			}
		}
		return false;
	}

	public static void CleanupIncomplete()
	{
		string lastExtractedFolder;
		bool lastExtractionCompleted;
		lock (StateLock)
		{
			lastExtractedFolder = _lastExtractedFolder;
			lastExtractionCompleted = _lastExtractionCompleted;
			_lastExtractedFolder = null;
			_lastExtractionCompleted = false;
		}
		try
		{
			if (!string.IsNullOrEmpty(lastExtractedFolder))
			{
				if (lastExtractionCompleted)
				{
					Plugin.Log?.Info("[BeatWanko] Keeping downloaded folder '" + lastExtractedFolder + "' (extraction completed); a later SongCore refresh can pick it up.");
				}
				else if (Directory.Exists(lastExtractedFolder))
				{
					Directory.Delete(lastExtractedFolder, recursive: true);
					Plugin.Log?.Info("[BeatWanko] Cleaned up incomplete download folder: " + lastExtractedFolder);
				}
			}
		}
		catch (Exception ex)
		{
			Plugin.Log?.Warn("[BeatWanko] Cleanup of download folder failed: " + ex.Message);
		}
	}

	private static string SanitizeFolderName(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			name = "BeatWankoSong";
		}
		char[] invalidFileNameChars = Path.GetInvalidFileNameChars();
		foreach (char oldChar in invalidFileNameChars)
		{
			name = name.Replace(oldChar, '_');
		}
		name = name.Trim();
		if (name.Length > 120)
		{
			name = name.Substring(0, 120);
		}
		return name;
	}
}
