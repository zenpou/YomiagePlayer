using System.IO;
using System.Runtime.CompilerServices;
using YomiagePlayer.Core.Cache;
using YomiagePlayer.Core.Library;
using YomiagePlayer.Core.Models;
using YomiagePlayer.Core.Transcription;
using YomiagePlayer.Services;
using YomiagePlayer.ViewModels;

namespace YomiagePlayer.Tests;

public class IdleAnalysisServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly string _libDir;
    private readonly TranscriptionCache _cache;
    private readonly TranscriptionQueue _queue = new();
    private readonly LyricsViewModel _lyricsVm = new();
    private readonly TranscriptionCoordinator _coordinator;

    public IdleAnalysisServiceTests()
    {
        _libDir = Path.Combine(_dir, "library");
        Directory.CreateDirectory(_libDir);
        _cache = new TranscriptionCache(Path.Combine(_dir, "cache"));

        var modelsDir = Path.Combine(_dir, "models");
        Directory.CreateDirectory(modelsDir);
        var downloader = new ModelDownloader(new System.Net.Http.HttpClient(), modelsDir);
        File.WriteAllText(downloader.PathFor(WhisperModel.Medium), "dummy");

        _coordinator = new TranscriptionCoordinator(
            _cache,
            _queue,
            new FakeExtractor(_dir),
            new FakeTranscriberFactory(),
            downloader,
            new SettingsStore(Path.Combine(_dir, "settings.json")),
            _lyricsVm,
            uiInvoke: a => a());
    }

    private sealed class FakeExtractor(string dir) : IAudioExtractorService
    {
        public Task<string> ExtractWavAsync(string mediaPath, CancellationToken ct)
        {
            var wav = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".wav");
            File.WriteAllBytes(wav, new byte[32000 * 5]);
            return Task.FromResult(wav);
        }
    }

    private sealed class FakeTranscriber : ITranscriber
    {
        public async IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
            string wavPath, [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return new TranscriptSegment(0, 2, "テスト");
        }

        public void Dispose() { }
    }

    private sealed class FakeTranscriberFactory : ITranscriberFactory
    {
        public ITranscriber Create(WhisperModel model) => new FakeTranscriber();
    }

    private string AddLibraryFile(string name)
    {
        var path = Path.Combine(_libDir, name);
        File.WriteAllBytes(path, Guid.NewGuid().ToByteArray());
        return path;
    }

    private IdleAnalysisService CreateService()
        => new(_coordinator, _queue, () => [_libDir]);

    [Fact]
    public async Task ScanOnce_AnalyzesOneUnanalyzedFile()
    {
        var f1 = AddLibraryFile("a.mp3");
        var svc = CreateService();

        Assert.True(await svc.ScanOnceAsync());
        Assert.True(_cache.TryLoad(ContentHasher.ComputeKey(f1), "medium", out _));
        // UIには触れない
        Assert.Equal(LyricsState.Idle, _lyricsVm.State);
        Assert.Empty(_lyricsVm.Rows);
    }

    [Fact]
    public async Task ScanOnce_ProcessesOneFilePerTick()
    {
        AddLibraryFile("a.mp3");
        AddLibraryFile("b.mp3");
        var svc = CreateService();

        Assert.True(await svc.ScanOnceAsync());  // 1件目
        Assert.True(await svc.ScanOnceAsync());  // 2件目
        Assert.False(await svc.ScanOnceAsync()); // もう残っていない
        Assert.Equal(2, Directory.GetFiles(Path.Combine(_dir, "cache"), "*.json").Length);
    }

    [Fact]
    public async Task ScanOnce_SkipsAlreadyCachedFiles()
    {
        var f1 = AddLibraryFile("cached.mp3");
        _cache.Save(new TranscriptionResult
        {
            SourceFileName = "cached.mp3",
            HashKey = ContentHasher.ComputeKey(f1),
            Model = "medium",
            Segments = [new(0, 1, "既存")],
        });
        var svc = CreateService();

        Assert.False(await svc.ScanOnceAsync());
    }

    [Fact]
    public async Task ScanOnce_QueueBusy_DoesNothing()
    {
        AddLibraryFile("a.mp3");
        var gate = new TaskCompletionSource();
        var blocker = _queue.Enqueue("busy", async ct =>
        {
            await gate.Task;
            return new TranscriptionResult { SourceFileName = "x", HashKey = "busy", Model = "m" };
        });

        var svc = CreateService();
        Assert.False(await svc.ScanOnceAsync());

        gate.SetResult();
        await blocker;
    }

    [Fact]
    public async Task ResetVisited_AllowsRescan()
    {
        var f1 = AddLibraryFile("a.mp3");
        var svc = CreateService();
        Assert.True(await svc.ScanOnceAsync());
        Assert.False(await svc.ScanOnceAsync());

        // キャッシュを消して再走査を許可(再解析シナリオ)
        _cache.Delete(ContentHasher.ComputeKey(f1), "medium");
        svc.ResetVisited();
        Assert.True(await svc.ScanOnceAsync());
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}
