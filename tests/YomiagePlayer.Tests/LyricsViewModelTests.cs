using YomiagePlayer.Core.Models;
using YomiagePlayer.ViewModels;

namespace YomiagePlayer.Tests;

public class LyricsViewModelTests
{
    private static LyricsViewModel Create(double duration = 100)
    {
        var vm = new LyricsViewModel();
        vm.Reset(duration);
        return vm;
    }

    [Fact]
    public void Reset_ClearsStateToAnalyzing()
    {
        var vm = Create();
        Assert.Equal(LyricsState.Analyzing, vm.State);
        Assert.Empty(vm.Rows);
        Assert.Equal(0, vm.ProgressPercent);
    }

    [Fact]
    public void AddSegment_AppendsRowAndUpdatesProgress()
    {
        var vm = Create(duration: 100);
        vm.AddSegment(new TranscriptSegment(0, 25, "こんにちは"));
        Assert.Single(vm.Rows);
        Assert.Equal(25, vm.ProgressPercent);
    }

    [Fact]
    public void LoadAll_SetsReady()
    {
        var vm = Create();
        vm.LoadAll([new(0, 5, "a"), new(5, 10, "b")]);
        Assert.Equal(LyricsState.Ready, vm.State);
        Assert.Equal(2, vm.Rows.Count);
    }

    [Fact]
    public void UpdatePosition_SetsCurrentIndexAndRowFlag()
    {
        var vm = Create();
        vm.LoadAll([new(0, 5, "a"), new(5, 10, "b")]);
        vm.UpdatePosition(6.0);
        Assert.Equal(1, vm.CurrentIndex);
        Assert.True(vm.Rows[1].IsCurrent);
        Assert.False(vm.Rows[0].IsCurrent);
    }

    [Fact]
    public void UpdatePosition_SilenceGap_NoCurrentRow()
    {
        var vm = Create();
        vm.LoadAll([new(0, 5, "a"), new(20, 25, "b")]);
        vm.UpdatePosition(10.0);
        Assert.Equal(-1, vm.CurrentIndex);
        Assert.All(vm.Rows, r => Assert.False(r.IsCurrent));
    }

    [Fact]
    public void UpdatePosition_AheadOfAnalyzedRegion_ShowsAnalyzingBanner()
    {
        var vm = Create(duration: 100);
        vm.AddSegment(new TranscriptSegment(0, 10, "a"));
        vm.UpdatePosition(50.0); // 解析済み(〜10s)より先
        Assert.True(vm.IsPositionAheadOfAnalysis);

        vm.UpdatePosition(5.0);
        Assert.False(vm.IsPositionAheadOfAnalysis);
    }

    [Fact]
    public void UpdatePosition_Ready_NeverShowsBanner()
    {
        var vm = Create(duration: 100);
        vm.LoadAll([new(0, 10, "a")]);
        vm.UpdatePosition(50.0);
        Assert.False(vm.IsPositionAheadOfAnalysis);
    }

    [Fact]
    public void MarkFailed_SetsStateAndMessage()
    {
        var vm = Create();
        vm.MarkFailed("音声を抽出できませんでした");
        Assert.Equal(LyricsState.Failed, vm.State);
        Assert.Equal("音声を抽出できませんでした", vm.ErrorMessage);
    }

    [Fact]
    public void MarkReady_AfterStreaming_SetsReady()
    {
        var vm = Create();
        vm.AddSegment(new TranscriptSegment(0, 5, "a"));
        vm.MarkReady();
        Assert.Equal(LyricsState.Ready, vm.State);
        Assert.Equal(100, vm.ProgressPercent);
    }

    [Fact]
    public void NotifyUserScrolled_SuspendsAutoScroll_ResumeRestores()
    {
        var vm = Create();
        Assert.False(vm.IsAutoScrollSuspended);
        vm.NotifyUserScrolled();
        Assert.True(vm.IsAutoScrollSuspended);
        vm.ResumeAutoScroll();
        Assert.False(vm.IsAutoScrollSuspended);
    }

    [Fact]
    public void RequestSeek_RaisesEventWithSegmentStart()
    {
        var vm = Create();
        vm.LoadAll([new(3.5, 5, "a")]);
        double? seeked = null;
        vm.SeekRequested += s => seeked = s;
        vm.RequestSeek(vm.Rows[0]);
        Assert.Equal(3.5, seeked);
    }
}
