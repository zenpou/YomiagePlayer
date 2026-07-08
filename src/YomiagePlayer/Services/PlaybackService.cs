using LibVLCSharp.Shared;

namespace YomiagePlayer.Services;

/// <summary>
/// LibVLCのラッパー。イベントはVLCのバックグラウンドスレッドから発火するため、
/// UIへ反映する購読側でDispatcherへマーシャリングすること。
/// </summary>
public sealed class PlaybackService : IDisposable
{
    private readonly LibVLC _libVlc;

    /// <summary>停止/終了状態からのシーク要求。再生開始(Playing)後に適用する。</summary>
    private TimeSpan? _pendingSeek;

    public MediaPlayer Player { get; }

    public event Action<TimeSpan>? PositionChanged;
    public event Action<TimeSpan>? LengthKnown;
    public event Action? MediaEnded;
    public event Action<bool>? PlayingChanged;

    public PlaybackService()
    {
        LibVLCSharp.Shared.Core.Initialize();
        _libVlc = new LibVLC();
        Player = new MediaPlayer(_libVlc);

        Player.TimeChanged += (_, e) => PositionChanged?.Invoke(TimeSpan.FromMilliseconds(e.Time));
        Player.LengthChanged += (_, e) => LengthKnown?.Invoke(TimeSpan.FromMilliseconds(e.Length));
        Player.EndReached += (_, _) => MediaEnded?.Invoke();
        Player.Playing += (_, _) =>
        {
            PlayingChanged?.Invoke(true);
            if (_pendingSeek is { } pending)
            {
                _pendingSeek = null;
                Player.Time = (long)pending.TotalMilliseconds;
            }
        };
        Player.Paused += (_, _) => PlayingChanged?.Invoke(false);
        Player.Stopped += (_, _) => PlayingChanged?.Invoke(false);
    }

    public bool IsPlaying => Player.IsPlaying;

    /// <summary>音量%。100が原音量、100超はVLCのソフトウェア増幅(最大150)。</summary>
    public int Volume
    {
        get => Player.Volume;
        set => Player.Volume = Math.Clamp(value, 0, 150);
    }

    public void Play(string path)
    {
        using var media = new Media(_libVlc, path);
        Player.Play(media);
    }

    public void TogglePause()
    {
        if (Player.IsPlaying)
        {
            Player.SetPause(true);
        }
        else if (Player.Media is not null)
        {
            if (Player.State is VLCState.Ended or VLCState.Stopped)
                Restart();
            else
                Player.SetPause(false);
        }
    }

    /// <summary>EndReachedハンドラ内などVLCコールバック中のStopはデッドロックするためThreadPoolで実行。</summary>
    public void Stop() => ThreadPool.QueueUserWorkItem(_ => Player.Stop());

    public void SeekTo(TimeSpan position)
    {
        if (Player.Media is null) return;

        if (Player.State is VLCState.Ended or VLCState.Stopped)
        {
            // 停止/終了状態ではTime設定が効かないため、再生を再開してから適用する
            _pendingSeek = position;
            Restart();
        }
        else if (Player.IsSeekable)
        {
            Player.Time = (long)position.TotalMilliseconds;
        }
        else
        {
            // メディアオープン直後などまだシーク不可の場合はPlayingで適用
            _pendingSeek = position;
        }
    }

    /// <summary>Ended/Stoppedから同じメディアを再生し直す。VLCコールバックとのデッドロック回避のためThreadPoolで実行。</summary>
    private void Restart()
        => ThreadPool.QueueUserWorkItem(_ =>
        {
            Player.Stop();
            Player.Play();
        });

    public void Dispose()
    {
        Player.Dispose();
        _libVlc.Dispose();
    }
}
