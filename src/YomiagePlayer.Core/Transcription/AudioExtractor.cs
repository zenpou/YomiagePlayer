using FFMpegCore;

namespace YomiagePlayer.Core.Transcription;

public class AudioExtractionException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// ffmpeg(LGPLビルド)でメディアファイルからWhisper入力用の
/// 16kHz mono 16bit PCM WAVを抽出する。最初の音声トラック固定。
/// </summary>
public class AudioExtractor(string tempDir) : IAudioExtractorService
{
    public AudioExtractor() : this(AppPaths.Temp) { }

    /// <summary>tempDir内の残骸WAVを削除する。起動時に呼ぶ。</summary>
    public void CleanupTemp()
    {
        if (!Directory.Exists(tempDir)) return;
        foreach (var f in Directory.GetFiles(tempDir, "*.wav"))
            try { File.Delete(f); } catch { }
    }

    public async Task<string> ExtractWavAsync(string mediaPath, CancellationToken ct)
    {
        Directory.CreateDirectory(tempDir);
        var outPath = Path.Combine(tempDir, Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            var ok = await FFMpegArguments
                .FromFileInput(mediaPath)
                .OutputToFile(outPath, overwrite: true, options => options
                    .WithCustomArgument("-vn")      // 映像を無視
                    .WithCustomArgument("-ac 1")    // mono
                    .WithCustomArgument("-ar 16000")
                    .WithCustomArgument("-c:a pcm_s16le")
                    .WithCustomArgument("-map 0:a:0")) // 最初の音声トラック固定
                .CancellableThrough(ct)
                .ProcessAsynchronously();
            if (!ok || !File.Exists(outPath) || new FileInfo(outPath).Length == 0)
                throw new AudioExtractionException($"音声を抽出できませんでした: {mediaPath}");
            return outPath;
        }
        catch (OperationCanceledException)
        {
            TryDelete(outPath);
            throw;
        }
        catch (Exception ex) when (ex is not AudioExtractionException)
        {
            TryDelete(outPath);
            throw new AudioExtractionException($"音声を抽出できませんでした: {mediaPath}", ex);
        }
    }

    public static void DeleteWav(string wavPath) => TryDelete(wavPath);

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
