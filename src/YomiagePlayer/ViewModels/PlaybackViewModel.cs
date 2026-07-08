using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YomiagePlayer.Services;

namespace YomiagePlayer.ViewModels;

public partial class PlaybackViewModel : ObservableObject
{
    private readonly PlaybackService _playback;
    private bool _updatingFromPlayer;

    [ObservableProperty]
    private double _positionSeconds;

    [ObservableProperty]
    private double _durationSeconds;

    [ObservableProperty]
    private int _volume = 80;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private string _nowPlayingTitle = "";

    /// <summary>前/次ボタン。曲送りのロジックはプレイリスト側が持つ。</summary>
    public event Action? NextRequested;
    public event Action? PrevRequested;

    public PlaybackViewModel(PlaybackService playback)
    {
        _playback = playback;
        _playback.Volume = Volume;

        _playback.PositionChanged += t => OnUi(() =>
        {
            _updatingFromPlayer = true;
            PositionSeconds = t.TotalSeconds;
            _updatingFromPlayer = false;
        });
        _playback.LengthKnown += t => OnUi(() => DurationSeconds = t.TotalSeconds);
        _playback.PlayingChanged += playing => OnUi(() => IsPlaying = playing);
    }

    partial void OnPositionSecondsChanged(double value)
    {
        // プレイヤー由来の更新はシークしない。スライダー操作(ユーザー由来)のみシーク
        if (!_updatingFromPlayer)
            _playback.SeekTo(TimeSpan.FromSeconds(value));
    }

    partial void OnVolumeChanged(int value) => _playback.Volume = value;

    [RelayCommand]
    private void PlayPause() => _playback.TogglePause();

    [RelayCommand]
    private void Next() => NextRequested?.Invoke();

    [RelayCommand]
    private void Prev() => PrevRequested?.Invoke();

    public void SeekTo(double seconds) => _playback.SeekTo(TimeSpan.FromSeconds(seconds));

    private static void OnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }
}
