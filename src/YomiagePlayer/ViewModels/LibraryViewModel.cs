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

    /// <summary>登録フォルダ一覧を絞り込む検索文字列(部分一致)。空なら全件表示。</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

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

    public void AddFolder(string path) => AddFolders([path]);

    public void AddFolders(IEnumerable<string> paths)
    {
        var added = false;
        foreach (var path in paths)
        {
            if (Folders.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)))
                continue;
            Folders.Add(new LibraryFolder(path));
            added = true;
        }
        if (added)
            FoldersChanged?.Invoke();
    }

    [RelayCommand]
    private void RemoveFolder(LibraryFolder folder) => RemoveFolders([folder]);

    public void RemoveFolders(IEnumerable<LibraryFolder> folders)
    {
        var removed = false;
        foreach (var folder in folders.ToList())
            removed |= Folders.Remove(folder);
        if (removed)
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
