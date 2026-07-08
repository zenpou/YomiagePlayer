using System.IO;
using System.Runtime.CompilerServices;
using YomiagePlayer.Core.Cache;
using YomiagePlayer.Core.Library;
using YomiagePlayer.Core.Models;
using YomiagePlayer.Core.Transcription;
using YomiagePlayer.Services;
using YomiagePlayer.ViewModels;

namespace YomiagePlayer.Tests;

public class TranscriptionCoordinatorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly string _mediaFile;
    private readonly TranscriptionCache _cache;
    private readonly LyricsViewModel _lyricsVm = new();

    public TranscriptionCoordinatorTests()
    {
        Directory.CreateDirectory(_dir);
        _mediaFile = Path.Combine(_dir, "test.mp3");
        File.WriteAllBytes(_mediaFile, new byte[2048]);
        _cache = new TranscriptionCache(Path.Combine(_dir, "cache"));
    }

    private sealed class FakeExtractor(string dir, bool fail = false) : IAudioExtractorService
    {
        public Task<string> ExtractWavAsync(string mediaPath, CancellationToken ct)
        {
            if (fail) throw new AudioExtractionException("no audio");
            var wav = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".wav");
            File.WriteAllBytes(wav, new byte[32000 * 10]); // 10秒相当
            return Task.FromResult(wav);
        }
    }

    private sealed class FakeTranscriber(IReadOnlyList<TranscriptSegment> segments) : ITranscriber
    {
        public async IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
            string wavPath, [EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var s in segments)
            {
                await Task.Yield();
                yield return s;
            }
        }

        public void Dispose() { }
    }

    private sealed class FakeTranscriberFactory(IReadOnlyList<TranscriptSegment> segments) : ITranscriberFactory
    {
        public int CreateCount;

        public ITranscriber Create(WhisperModel model)
        {
            CreateCount++;
            return new FakeTranscriber(segments);
        }
    }

    private TranscriptionCoordinator Create(
        IReadOnlyList<TranscriptSegment>? segments = null,
        bool extractFails = false,
        bool modelDownloaded = true)
    {
        segments ??= [new(0, 3, "こんにちは"), new(3, 6, "テストです")];
        var settingsFile = Path.Combine(_dir, "settings.json");
        var settings = new SettingsStore(settingsFile);
        // IsDownloadedはファイル存在で判定するため、DL済みを装うにはダミーファイルを置く
        var modelsDir = Path.Combine(_dir, "models");
        var downloader = new ModelDownloader(new System.Net.Http.HttpClient(), modelsDir);
        if (modelDownloaded)
        {
            Directory.CreateDirectory(modelsDir);
            File.WriteAllText(downloader.PathFor(WhisperModel.Medium), "dummy");
        }
        return new TranscriptionCoordinator(
            _cache,
            new TranscriptionQueue(),
            new FakeExtractor(_dir, extractFails),
            new FakeTranscriberFactory(segments),
            downloader,
            settings,
            _lyricsVm,
            uiInvoke: a => a());
    }

    [Fact]
    public async Task CacheMiss_StreamsSegmentsAndSavesCache()
    {
        var c = Create();
        await c.OnMediaChangedAsync(_mediaFile);

        Assert.Equal(LyricsState.Ready, _lyricsVm.State);
        Assert.Equal(2, _lyricsVm.Rows.Count);
        var key = ContentHasher.ComputeKey(_mediaFile);
        Assert.True(_cache.TryLoad(key, "medium", out var saved));
        Assert.Equal(2, saved!.Segments.Count);
        Assert.Equal(10.0, saved.DurationSec, precision: 1);
    }

    [Fact]
    public async Task CacheHit_LoadsWithoutTranscribing()
    {
        var key = ContentHasher.ComputeKey(_mediaFile);
        _cache.Save(new TranscriptionResult
        {
            SourceFileName = "test.mp3",
            HashKey = key,
            Model = "medium",
            Segments = [new(0, 1, "キャッシュ済み")],
        });

        var c = Create();
        await c.OnMediaChangedAsync(_mediaFile);

        Assert.Equal(LyricsState.Ready, _lyricsVm.State);
        Assert.Single(_lyricsVm.Rows);
        Assert.Equal("キャッシュ済み", _lyricsVm.Rows[0].Text);
    }

    [Fact]
    public async Task ExtractionFailure_MarksFailed()
    {
        var c = Create(extractFails: true);
        await c.OnMediaChangedAsync(_mediaFile);
        Assert.Equal(LyricsState.Failed, _lyricsVm.State);
        Assert.Contains("音声を抽出できません", _lyricsVm.ErrorMessage);
    }

    [Fact]
    public async Task ModelNotDownloaded_MarksFailedWithGuidance()
    {
        var c = Create(modelDownloaded: false);
        await c.OnMediaChangedAsync(_mediaFile);
        Assert.Equal(LyricsState.Failed, _lyricsVm.State);
        Assert.Contains("未ダウンロード", _lyricsVm.ErrorMessage);
    }

    [Fact]
    public async Task Reanalyze_IgnoresExistingCache()
    {
        var key = ContentHasher.ComputeKey(_mediaFile);
        _cache.Save(new TranscriptionResult
        {
            SourceFileName = "test.mp3",
            HashKey = key,
            Model = "medium",
            Segments = [new(0, 1, "古い結果")],
        });

        var c = Create(segments: [new(0, 2, "新しい結果")]);
        await c.ReanalyzeAsync(_mediaFile);

        Assert.Single(_lyricsVm.Rows);
        Assert.Equal("新しい結果", _lyricsVm.Rows[0].Text);
        Assert.True(_cache.TryLoad(key, "medium", out var saved));
        Assert.Equal("新しい結果", saved!.Segments[0].Text);
    }

    [Fact]
    public async Task MediaSwitchDuringAnalysis_OldJobCachesButDoesNotTouchUi()
    {
        var media2 = Path.Combine(_dir, "second.mp3");
        File.WriteAllBytes(media2, new byte[4096]);

        var c = Create();
        var t1 = c.OnMediaChangedAsync(_mediaFile);
        var t2 = c.OnMediaChangedAsync(media2); // 即座に切替
        await Task.WhenAll(t1, t2);

        // 両方ともキャッシュには保存される
        Assert.True(_cache.TryLoad(ContentHasher.ComputeKey(_mediaFile), "medium", out _));
        Assert.True(_cache.TryLoad(ContentHasher.ComputeKey(media2), "medium", out _));
        // UIは最後に開いた曲の分のみ(2セグメント。二重反映されていない)
        Assert.Equal(2, _lyricsVm.Rows.Count);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}
