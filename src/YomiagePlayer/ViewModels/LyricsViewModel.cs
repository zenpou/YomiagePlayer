using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YomiagePlayer.Core.Models;
using YomiagePlayer.Core.Transcription;

namespace YomiagePlayer.ViewModels;

public enum LyricsState
{
    Idle,
    Analyzing,
    Ready,
    Failed,
}

public partial class SegmentRow(TranscriptSegment segment) : ObservableObject
{
    public TranscriptSegment Segment { get; } = segment;
    public string Text => Segment.Text;
    public string TimeLabel { get; } = TimeSpan.FromSeconds(segment.Start).ToString(
        segment.Start >= 3600 ? @"h\:mm\:ss" : @"mm\:ss");

    [ObservableProperty]
    private bool _isCurrent;
}

/// <summary>
/// 歌詞パネルのVM。解析中はAddSegmentで逐次行が増え、キャッシュ命中時はLoadAllで一括表示。
/// Dispatcherへの依存はない(呼び出し側がUIスレッドで呼ぶこと)。
/// </summary>
public partial class LyricsViewModel : ObservableObject
{
    public ObservableCollection<SegmentRow> Rows { get; } = [];

    [ObservableProperty]
    private LyricsState _state = LyricsState.Idle;

    [ObservableProperty]
    private int _progressPercent;

    [ObservableProperty]
    private int _currentIndex = -1;

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private bool _isAutoScrollSuspended;

    /// <summary>解析中に、解析済み区間より先の位置を再生している</summary>
    [ObservableProperty]
    private bool _isPositionAheadOfAnalysis;

    private double _durationSec;
    private double _analyzedUntilSec;
    private readonly List<TranscriptSegment> _segments = [];

    public event Action<double>? SeekRequested;
    public event Action? ReanalyzeRequested;
    public event Action<int>? CurrentRowChanged;

    /// <summary>新しいメディアの解析を開始するときに呼ぶ。</summary>
    public void Reset(double durationSec)
    {
        _durationSec = durationSec;
        _analyzedUntilSec = 0;
        _segments.Clear();
        Rows.Clear();
        CurrentIndex = -1;
        ProgressPercent = 0;
        ErrorMessage = "";
        IsPositionAheadOfAnalysis = false;
        State = LyricsState.Analyzing;
    }

    public void SetDuration(double durationSec) => _durationSec = durationSec;

    public void AddSegment(TranscriptSegment segment)
    {
        _segments.Add(segment);
        Rows.Add(new SegmentRow(segment));
        _analyzedUntilSec = Math.Max(_analyzedUntilSec, segment.End);
        if (_durationSec > 0)
            ProgressPercent = Math.Min(100, (int)(_analyzedUntilSec / _durationSec * 100));
    }

    public void LoadAll(IEnumerable<TranscriptSegment> segments)
    {
        _segments.Clear();
        Rows.Clear();
        foreach (var s in segments)
        {
            _segments.Add(s);
            Rows.Add(new SegmentRow(s));
        }
        _analyzedUntilSec = _segments.Count > 0 ? _segments[^1].End : 0;
        ProgressPercent = 100;
        IsPositionAheadOfAnalysis = false;
        State = LyricsState.Ready;
    }

    public void MarkReady()
    {
        ProgressPercent = 100;
        IsPositionAheadOfAnalysis = false;
        State = LyricsState.Ready;
    }

    public void MarkFailed(string message)
    {
        ErrorMessage = message;
        State = LyricsState.Failed;
    }

    public void MarkIdle() => State = LyricsState.Idle;

    /// <summary>再生位置の変化に応じて現在行を更新する。</summary>
    public void UpdatePosition(double seconds)
    {
        IsPositionAheadOfAnalysis =
            State == LyricsState.Analyzing && seconds > _analyzedUntilSec;

        var index = SegmentLocator.FindIndex(_segments, seconds);
        if (index == CurrentIndex) return;

        if (CurrentIndex >= 0 && CurrentIndex < Rows.Count)
            Rows[CurrentIndex].IsCurrent = false;
        if (index >= 0)
            Rows[index].IsCurrent = true;
        CurrentIndex = index;

        if (index >= 0)
            CurrentRowChanged?.Invoke(index);
    }

    public void RequestSeek(SegmentRow row) => SeekRequested?.Invoke(row.Segment.Start);

    public void NotifyUserScrolled() => IsAutoScrollSuspended = true;

    [RelayCommand]
    public void ResumeAutoScroll()
    {
        IsAutoScrollSuspended = false;
        if (CurrentIndex >= 0)
            CurrentRowChanged?.Invoke(CurrentIndex);
    }

    [RelayCommand]
    private void Reanalyze() => ReanalyzeRequested?.Invoke();
}
