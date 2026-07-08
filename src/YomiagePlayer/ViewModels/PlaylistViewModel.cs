using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YomiagePlayer.Core.Library;

namespace YomiagePlayer.ViewModels;

public enum RepeatMode
{
    None,
    All,
    One,
}

public partial class PlaylistItem(string filePath) : ObservableObject
{
    public string FilePath { get; } = filePath;
    public string DisplayName { get; } = Path.GetFileNameWithoutExtension(filePath);

    [ObservableProperty]
    private bool _isPlaying;
}

public partial class PlaylistViewModel : ObservableObject
{
    private readonly Random _random;

    public ObservableCollection<PlaylistItem> Items { get; } = [];

    [ObservableProperty]
    private PlaylistItem? _currentItem;

    [ObservableProperty]
    private RepeatMode _repeatMode = RepeatMode.None;

    [ObservableProperty]
    private bool _isShuffle;

    public event Action<PlaylistItem>? PlayRequested;

    public PlaylistViewModel() : this(new Random()) { }

    public PlaylistViewModel(Random random) => _random = random;

    partial void OnCurrentItemChanged(PlaylistItem? oldValue, PlaylistItem? newValue)
    {
        if (oldValue is not null) oldValue.IsPlaying = false;
        if (newValue is not null) newValue.IsPlaying = true;
    }

    public void ReplaceAll(IEnumerable<string> paths)
    {
        Items.Clear();
        foreach (var p in paths) Items.Add(new PlaylistItem(p));
    }

    public void Add(IEnumerable<string> paths)
    {
        foreach (var p in paths) Items.Add(new PlaylistItem(p));
    }

    public void PlayItem(PlaylistItem item)
    {
        CurrentItem = item;
        PlayRequested?.Invoke(item);
    }

    /// <summary>曲送り。manual=trueはユーザー操作(RepeatOneでも次へ進む)。</summary>
    public void PlayNext(bool manual = false)
    {
        var next = GetNext(manual);
        if (next is not null) PlayItem(next);
    }

    public void PlayPrev()
    {
        var prev = GetPrev();
        if (prev is not null) PlayItem(prev);
    }

    public PlaylistItem? GetNext(bool manual)
    {
        if (Items.Count == 0) return null;
        if (CurrentItem is null) return Items[0];

        if (RepeatMode == RepeatMode.One && !manual)
            return CurrentItem;

        if (IsShuffle)
        {
            if (Items.Count == 1)
                return RepeatMode == RepeatMode.None ? null : CurrentItem;
            PlaylistItem candidate;
            do { candidate = Items[_random.Next(Items.Count)]; }
            while (candidate == CurrentItem);
            return candidate;
        }

        var index = Items.IndexOf(CurrentItem);
        if (index + 1 < Items.Count) return Items[index + 1];
        return RepeatMode == RepeatMode.All ? Items[0] : null;
    }

    public PlaylistItem? GetPrev()
    {
        if (Items.Count == 0) return null;
        if (CurrentItem is null) return Items[0];
        var index = Items.IndexOf(CurrentItem);
        if (index - 1 >= 0) return Items[index - 1];
        return RepeatMode == RepeatMode.All ? Items[^1] : null;
    }

    public void Remove(PlaylistItem item)
    {
        if (item == CurrentItem) CurrentItem = null;
        Items.Remove(item);
    }

    public void SaveTo(string path) => M3u8Serializer.Save(path, Items.Select(i => i.FilePath));

    public void LoadFrom(string path) => ReplaceAll(M3u8Serializer.Load(path));

    [RelayCommand]
    private void CycleRepeatMode()
        => RepeatMode = RepeatMode switch
        {
            RepeatMode.None => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            _ => RepeatMode.None,
        };

    [RelayCommand]
    private void ToggleShuffle() => IsShuffle = !IsShuffle;
}
