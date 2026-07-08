using LibVLCSharp.Shared;

namespace YomiagePlayer.Services;

/// <summary>
/// LibVLCのラッパー。イベントはVLCのバックグラウンドスレッドから発火するため、
/// UIへ反映する購読側でDispatcherへマーシャリングすること。
/// </summary>
public sealed class PlaybackService : IDisposable
{
    private readonly LibVLC _libVlc;

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
        Player.Playing += (_, _) => PlayingChanged?.Invoke(true);
        Player.Paused += (_, _) => PlayingChanged?.Invoke(false);
        Player.Stopped += (_, _) => PlayingChanged?.Invoke(false);
    }

    public bool IsPlaying => Player.IsPlaying;

    public int Volume
    {
        get => Player.Volume;
        set => Player.Volume = Math.Clamp(value, 0, 100);
    }

    public void Play(string path)
    {
        using var media = new Media(_libVlc, path);
        Player.Play(media);
    }

    public void TogglePause()
    {
        if (Player.IsPlaying) Player.SetPause(true);
        else if (Player.Media is not null) Player.SetPause(false);
    }

    /// <summary>EndReachedハンドラ内などVLCコールバック中のStopはデッドロックするためThreadPoolで実行。</summary>
    public void Stop() => ThreadPool.QueueUserWorkItem(_ => Player.Stop());

    public void SeekTo(TimeSpan position)
    {
        if (Player.Media is not null && Player.IsSeekable)
            Player.Time = (long)position.TotalMilliseconds;
    }

    public void Dispose()
    {
        Player.Dispose();
        _libVlc.Dispose();
    }
}
