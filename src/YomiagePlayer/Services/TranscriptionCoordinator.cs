using System.IO;
using Serilog;
using YomiagePlayer.Core.Cache;
using YomiagePlayer.Core.Library;
using YomiagePlayer.Core.Models;
using YomiagePlayer.Core.Transcription;
using YomiagePlayer.ViewModels;

namespace YomiagePlayer.Services;

/// <summary>
/// 「メディアを開く → キャッシュ探索 → (未命中なら)抽出+文字起こし → 歌詞パネルへ反映」
/// を束ねる中核オーケストレータ。
/// - 解析はTranscriptionQueue(同時1件)経由。曲を切り替えても進行中ジョブは完走しキャッシュされる
/// - UI反映は「現在表示中の曲」のジョブのみ(キーで判定)
/// </summary>
public sealed class TranscriptionCoordinator : IDisposable
{
    private readonly TranscriptionCache _cache;
    private readonly TranscriptionQueue _queue;
    private readonly IAudioExtractorService _extractor;
    private readonly ITranscriberFactory _transcriberFactory;
    private readonly ModelDownloader _downloader;
    private readonly SettingsStore _settings;
    private readonly LyricsViewModel _lyricsVm;
    private readonly Action<Action> _uiInvoke;

    private readonly object _transcriberLock = new();
    private (WhisperModel Model, ITranscriber Instance)? _transcriber;
    private string? _currentKey;

    public TranscriptionCoordinator(
        TranscriptionCache cache,
        TranscriptionQueue queue,
        IAudioExtractorService extractor,
        ITranscriberFactory transcriberFactory,
        ModelDownloader downloader,
        SettingsStore settings,
        LyricsViewModel lyricsVm,
        Action<Action> uiInvoke)
    {
        _cache = cache;
        _queue = queue;
        _extractor = extractor;
        _transcriberFactory = transcriberFactory;
        _downloader = downloader;
        _settings = settings;
        _lyricsVm = lyricsVm;
        _uiInvoke = uiInvoke;
    }

    private WhisperModel CurrentModel
    {
        get
        {
            WhisperModelInfo.TryParse(_settings.Load().Model, out var model);
            return model;
        }
    }

    public Task OnMediaChangedAsync(string mediaPath) => HandleAsync(mediaPath, force: false);

    /// <summary>
    /// UIに触れないバックグラウンド解析(アイドル時のライブラリ事前解析用)。
    /// 解析を実行したらtrue、キャッシュ済み・モデル未DL・失敗ならfalse。
    /// </summary>
    public async Task<bool> EnsureAnalyzedAsync(string mediaPath)
    {
        var model = CurrentModel;
        if (!_downloader.IsDownloaded(model)) return false;

        string key;
        try
        {
            key = await Task.Run(() => ContentHasher.ComputeKey(mediaPath)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "バックグラウンド解析: ハッシュ計算失敗 {Path}", mediaPath);
            return false;
        }

        if (_cache.TryLoad(key, model.Id(), out _)) return false;

        try
        {
            await _queue.Enqueue(key, ct => RunJobAsync(mediaPath, key, model, ct))
                .ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "バックグラウンド解析失敗: {Path}", mediaPath);
            return false;
        }
    }

    /// <summary>右クリック「再解析」。キャッシュを消して解析し直す。</summary>
    public Task ReanalyzeAsync(string mediaPath) => HandleAsync(mediaPath, force: true);

    private async Task HandleAsync(string mediaPath, bool force)
    {
        var model = CurrentModel;
        _uiInvoke(() => _lyricsVm.Reset(0));

        string key;
        try
        {
            key = await Task.Run(() => ContentHasher.ComputeKey(mediaPath)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ハッシュ計算失敗: {Path}", mediaPath);
            _uiInvoke(() => _lyricsVm.MarkFailed("ファイルを読み取れませんでした"));
            return;
        }
        _currentKey = key;

        if (force)
            _cache.Delete(key, model.Id());

        if (_cache.TryLoad(key, model.Id(), out var cached))
        {
            _uiInvoke(() =>
            {
                if (_currentKey == key)
                    _lyricsVm.LoadAll(cached!.Segments);
            });
            return;
        }

        if (!_downloader.IsDownloaded(model))
        {
            _uiInvoke(() =>
            {
                if (_currentKey == key)
                    _lyricsVm.MarkFailed(
                        $"モデル({model.Id()})が未ダウンロードです。設定画面からダウンロードしてください。");
            });
            return;
        }

        try
        {
            await _queue.Enqueue(key, ct => RunJobAsync(mediaPath, key, model, ct))
                .ConfigureAwait(false);
            _uiInvoke(() =>
            {
                if (_currentKey == key)
                    _lyricsVm.MarkReady();
            });
        }
        catch (OperationCanceledException)
        {
            // アプリ終了・キュー破棄。UIはそのまま
        }
        catch (Exception ex)
        {
            Log.Error(ex, "文字起こし失敗: {Path}", mediaPath);
            _uiInvoke(() =>
            {
                if (_currentKey == key)
                    _lyricsVm.MarkFailed(ex is AudioExtractionException
                        ? "音声を抽出できませんでした"
                        : "文字起こしに失敗しました");
            });
        }
    }

    private async Task<TranscriptionResult> RunJobAsync(
        string mediaPath, string key, WhisperModel model, CancellationToken ct)
    {
        var wavPath = await _extractor.ExtractWavAsync(mediaPath, ct).ConfigureAwait(false);
        try
        {
            // 16kHz mono 16bit PCM: 32000 bytes/秒(ヘッダ44byteは誤差として無視)
            var durationSec = new FileInfo(wavPath).Length / 32000.0;
            _uiInvoke(() =>
            {
                if (_currentKey == key)
                    _lyricsVm.SetDuration(durationSec);
            });

            var transcriber = GetTranscriber(model);
            var segments = new List<TranscriptSegment>();
            await foreach (var seg in transcriber.TranscribeAsync(wavPath, ct).ConfigureAwait(false))
            {
                segments.Add(seg);
                _uiInvoke(() =>
                {
                    if (_currentKey == key)
                        _lyricsVm.AddSegment(seg);
                });
            }

            var result = new TranscriptionResult
            {
                SourceFileName = Path.GetFileName(mediaPath),
                HashKey = key,
                Model = model.Id(),
                DurationSec = durationSec,
                Segments = segments,
            };
            _cache.Save(result);
            Log.Information("解析完了: {File} ({Count}セグメント)", result.SourceFileName, segments.Count);
            return result;
        }
        finally
        {
            AudioExtractor.DeleteWav(wavPath);
        }
    }

    private ITranscriber GetTranscriber(WhisperModel model)
    {
        lock (_transcriberLock)
        {
            if (_transcriber is { } t && t.Model == model)
                return t.Instance;
            _transcriber?.Instance.Dispose();
            var created = _transcriberFactory.Create(model);
            _transcriber = (model, created);
            return created;
        }
    }

    public async Task ShutdownAsync()
    {
        await _queue.ShutdownAsync().ConfigureAwait(false);
        Dispose();
    }

    public void Dispose()
    {
        lock (_transcriberLock)
        {
            _transcriber?.Instance.Dispose();
            _transcriber = null;
        }
    }
}

public class WhisperTranscriberFactory(ModelDownloader downloader, HallucinationFilter filter)
    : ITranscriberFactory
{
    public ITranscriber Create(WhisperModel model)
        => new WhisperTranscriber(downloader.PathFor(model), filter);
}
