using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace YomiagePlayer.ViewModels;

public partial class LibraryFolder(string path) : ObservableObject
{
    public string Path { get; } = path;
    public string DisplayName { get; } = System.IO.Path.GetFileName(
        path.TrimEnd(System.IO.Path.DirectorySeparatorChar)) is { Length: > 0 } name ? name : path;
}

public partial class LibraryViewModel : ObservableObject
{
    public ObservableCollection<LibraryFolder> Folders { get; } = [];

    /// <summary>フォルダ内のメディアをプレイリストへ投入する要求(replace=置換/追加)。</summary>
    public event Action<IReadOnlyList<string>, bool>? FilesRequested;

    public event Action? FoldersChanged;

    public void SetFolders(IEnumerable<string> paths)
    {
        Folders.Clear();
        foreach (var p in paths.Where(Directory.Exists))
            Folders.Add(new LibraryFolder(p));
    }

    public IEnumerable<string> FolderPaths => Folders.Select(f => f.Path);

    public void AddFolder(string path)
    {
        if (Folders.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)))
            return;
        Folders.Add(new LibraryFolder(path));
        FoldersChanged?.Invoke();
    }

    [RelayCommand]
    private void RemoveFolder(LibraryFolder folder)
    {
        Folders.Remove(folder);
        FoldersChanged?.Invoke();
    }

    public void OpenFolder(LibraryFolder folder, bool append)
    {
        if (!Directory.Exists(folder.Path)) return;
        var files = MainWindow.EnumerateMediaFiles(folder.Path);
        if (files.Count > 0)
            FilesRequested?.Invoke(files, !append);
    }
}
