using YomiagePlayer.ViewModels;

namespace YomiagePlayer.Tests;

public class LibraryViewModelTests
{
    [Fact]
    public void RemoveFolders_RemovesAllGivenFolders()
    {
        var vm = new LibraryViewModel();
        vm.AddFolders([@"C:\a", @"C:\b", @"C:\c"]);

        vm.RemoveFolders([vm.Folders[0], vm.Folders[2]]);

        Assert.Equal([@"C:\b"], vm.FolderPaths);
    }

    [Fact]
    public void RemoveFolders_RaisesFoldersChangedOnce()
    {
        var vm = new LibraryViewModel();
        vm.AddFolders([@"C:\a", @"C:\b"]);
        var raised = 0;
        vm.FoldersChanged += () => raised++;

        vm.RemoveFolders([vm.Folders[0], vm.Folders[1]]);

        Assert.Equal(1, raised);
    }

    [Fact]
    public void RemoveFolders_EmptySelection_DoesNotRaiseFoldersChanged()
    {
        var vm = new LibraryViewModel();
        vm.AddFolders([@"C:\a"]);
        var raised = 0;
        vm.FoldersChanged += () => raised++;

        vm.RemoveFolders([]);

        Assert.Equal(0, raised);
        Assert.Single(vm.Folders);
    }
}
