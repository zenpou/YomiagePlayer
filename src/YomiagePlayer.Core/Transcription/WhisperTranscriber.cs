using System.Runtime.CompilerServices;
using Whisper.net;
using Whisper.net.LibraryLoader;
using YomiagePlayer.Core.Models;

namespace YomiagePlayer.Core.Transcription;

/// <summary>
/// Whisper.netによる文字起こし。セグメントを逐次yieldするので、
/// 呼び出し側は解析完了を待たずにUIへ反映できる。
/// ハルシネーション対策: no_speechしきい値・エントロピーしきい値(Whisperパラメータ)
/// + NoContext(繰り返し連鎖の抑制) + HallucinationFilter(テキストベース後処理)。
/// ※Whisper.net 1.9.1にはSilero VADのビルダーAPIが無いため、VADなし構成。
/// </summary>
public sealed class WhisperTranscriber(string modelPath, HallucinationFilter filter) : IDisposable
{
    private WhisperFactory? _factory;

    /// <summary>GPU優先の自動フォールバック順を設定する。アプリ起動時に1回呼ぶ。</summary>
    public static void ConfigureRuntimeOrder()
        => RuntimeOptions.RuntimeLibraryOrder =
            [RuntimeLibrary.Cuda, RuntimeLibrary.Vulkan, RuntimeLibrary.Cpu];

    /// <summary>実際にロードされたランタイム名(設定画面の表示用)。未ロードならnull。</summary>
    public static string? LoadedRuntime => RuntimeOptions.LoadedLibrary?.ToString();

    public async IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
        string wavPath, [EnumeratorCancellation] CancellationToken ct = default)
    {
        _factory ??= WhisperFactory.FromPath(modelPath);
        await using var processor = _factory.CreateBuilder()
            .WithLanguage("ja")
            .WithNoContext()
            .WithNoSpeechThreshold(0.6f)
            .WithEntropyThreshold(2.4f)
            .WithProbabilities()
            .Build();

        using var fs = File.OpenRead(wavPath);
        TranscriptSegment? prev = null;
        await foreach (var seg in processor.ProcessAsync(fs, ct))
        {
            var s = new TranscriptSegment(
                seg.Start.TotalSeconds, seg.End.TotalSeconds, seg.Text.Trim());
            if (filter.ShouldDrop(s, prev)) continue;
            prev = s;
            yield return s;
        }
    }

    public void Dispose() => _factory?.Dispose();
}
