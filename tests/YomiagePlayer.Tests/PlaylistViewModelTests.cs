using YomiagePlayer.ViewModels;

namespace YomiagePlayer.Tests;

public class PlaylistViewModelTests
{
    private static PlaylistViewModel Create(int count = 3, int seed = 1)
    {
        var vm = new PlaylistViewModel(new Random(seed));
        vm.ReplaceAll(Enumerable.Range(1, count).Select(i => $@"C:\music\track{i}.mp3"));
        return vm;
    }

    [Fact]
    public void GetNext_Sequential_ReturnsNextItem()
    {
        var vm = Create();
        vm.PlayItem(vm.Items[0]);
        Assert.Same(vm.Items[1], vm.GetNext(manual: false));
    }

    [Fact]
    public void UpcomingFiles_NothingPlaying_ReturnsEmpty()
    {
        var vm = Create();
        Assert.Empty(vm.UpcomingFiles);
    }

    [Fact]
    public void UpcomingFiles_ReturnsFilesAfterCurrentInOrder()
    {
        var vm = Create(count: 4);
        vm.PlayItem(vm.Items[1]);
        Assert.Equal([vm.Items[2].FilePath, vm.Items[3].FilePath], vm.UpcomingFiles);
    }

    [Fact]
    public void UpcomingFiles_AtEnd_ReturnsEmpty()
    {
        var vm = Create();
        vm.PlayItem(vm.Items[^1]);
        Assert.Empty(vm.UpcomingFiles);
    }

    [Fact]
    public void GetNext_AtEnd_RepeatNone_ReturnsNull()
    {
        var vm = Create();
        vm.PlayItem(vm.Items[^1]);
        Assert.Null(vm.GetNext(manual: false));
    }

    [Fact]
    public void GetNext_AtEnd_RepeatAll_WrapsToFirst()
    {
        var vm = Create();
        vm.RepeatMode = RepeatMode.All;
        vm.PlayItem(vm.Items[^1]);
        Assert.Same(vm.Items[0], vm.GetNext(manual: false));
    }

    [Fact]
    public void GetNext_RepeatOne_Auto_ReturnsSame()
    {
        var vm = Create();
        vm.RepeatMode = RepeatMode.One;
        vm.PlayItem(vm.Items[1]);
        Assert.Same(vm.Items[1], vm.GetNext(manual: false));
    }

    [Fact]
    public void GetNext_RepeatOne_Manual_Advances()
    {
        var vm = Create();
        vm.RepeatMode = RepeatMode.One;
        vm.PlayItem(vm.Items[1]);
        Assert.Same(vm.Items[2], vm.GetNext(manual: true));
    }

    [Fact]
    public void GetNext_Shuffle_ReturnsDifferentItem()
    {
        var vm = Create(count: 10);
        vm.IsShuffle = true;
        vm.PlayItem(vm.Items[0]);
        for (int i = 0; i < 20; i++)
            Assert.NotSame(vm.Items[0], vm.GetNext(manual: false));
    }

    [Fact]
    public void GetPrev_AtStart_RepeatAll_WrapsToLast()
    {
        var vm = Create();
        vm.RepeatMode = RepeatMode.All;
        vm.PlayItem(vm.Items[0]);
        Assert.Same(vm.Items[^1], vm.GetPrev());
    }

    [Fact]
    public void PlayItem_SetsIsPlayingFlagExclusively()
    {
        var vm = Create();
        vm.PlayItem(vm.Items[0]);
        vm.PlayItem(vm.Items[1]);
        Assert.False(vm.Items[0].IsPlaying);
        Assert.True(vm.Items[1].IsPlaying);
    }

    [Fact]
    public void Remove_CurrentItem_ClearsCurrent()
    {
        var vm = Create();
        vm.PlayItem(vm.Items[0]);
        vm.Remove(vm.Items[0]);
        Assert.Null(vm.CurrentItem);
        Assert.Equal(2, vm.Items.Count);
    }

    [Fact]
    public void CycleRepeatMode_None_All_One_None()
    {
        var vm = Create();
        Assert.Equal(RepeatMode.None, vm.RepeatMode);
        vm.CycleRepeatModeCommand.Execute(null);
        Assert.Equal(RepeatMode.All, vm.RepeatMode);
        vm.CycleRepeatModeCommand.Execute(null);
        Assert.Equal(RepeatMode.One, vm.RepeatMode);
        vm.CycleRepeatModeCommand.Execute(null);
        Assert.Equal(RepeatMode.None, vm.RepeatMode);
    }
}
