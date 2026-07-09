using System.IO;
using System.Windows;
using Microsoft.Win32;
using YomiagePlayer.Core.Library;
using YomiagePlayer.Services;
using YomiagePlayer.ViewModels;

namespace YomiagePlayer;

public partial class MainWindow : Window
{
    private const string FileDialogFilter =
        "メディアファイル|*.mp3;*.wav;*.flac;*.m4a;*.ogg;*.opus;*.mp4;*.mkv;*.avi;*.webm|すべてのファイル|*.*";

    private readonly PlaybackService _playback;
    private readonly PlaybackViewModel _playbackVm;
    private readonly LyricsViewModel _lyricsVm;
    private readonly PlaylistViewModel _playlistVm;
    private readonly LibraryViewModel _libraryVm;
    private readonly SettingsStore _settingsStore;

    /// <summary>ファイルが開かれた(D&D/ダイアログ/引数)。</summary>
    public event Action<IReadOnlyList<string>>? FilesOpened;

    /// <summary>再生対象が切り替わった。TranscriptionCoordinatorが購読する。</summary>
    public event Action<string>? MediaChanged;

    /// <summary>再解析要求(プレイリスト右クリック/歌詞パネルの再解析ボタン)。</summary>
    public event Action<string>? ReanalyzeRequested;

    private string? _currentMediaPath;

    public MainWindow(
        PlaybackService playback,
        PlaybackViewModel playbackVm,
        LyricsViewModel lyricsVm,
        PlaylistViewModel playlistVm,
        LibraryViewModel libraryVm,
        SettingsStore settingsStore)
    {
        InitializeComponent();
        _playback = playback;
        _playbackVm = playbackVm;
        _lyricsVm = lyricsVm;
        _playlistVm = playlistVm;
        _libraryVm = libraryVm;
        _settingsStore = settingsStore;
        Controls.DataContext = playbackVm;
        Lyrics.DataContext = lyricsVm;
        Playlist.DataContext = playlistVm;
        Library.DataContext = libraryVm;

        _libraryVm.FilesRequested += (files, replace) =>
        {
            if (replace)
            {
                _playlistVm.ReplaceAll(files);
                if (_playlistVm.Items.Count > 0)
                    _playlistVm.PlayItem(_playlistVm.Items[0]);
            }
            else
            {
                _playlistVm.Add(files);
            }
        };
        _libraryVm.FoldersChanged += SaveSettings;

        RestoreSettings();
        Closing += (_, _) => SaveSettings();

        _lyricsVm.SeekRequested += s => _playback.SeekTo(TimeSpan.FromSeconds(s));
        _playback.PositionChanged += t => Dispatcher.BeginInvoke(
            () => _lyricsVm.UpdatePosition(t.TotalSeconds));
        _playback.LengthKnown += t => Dispatcher.BeginInvoke(
            () => _lyricsVm.SetDuration(t.TotalSeconds));

        _playlistVm.PlayRequested += item => PlayFile(item.FilePath);
        Playlist.ReanalyzeRequested += item => ReanalyzeRequested?.Invoke(item.FilePath);
        _lyricsVm.ReanalyzeRequested += () =>
        {
            if (_currentMediaPath is not null)
                ReanalyzeRequested?.Invoke(_currentMediaPath);
        };
        _playbackVm.NextRequested += () => _playlistVm.PlayNext(manual: true);
        _playbackVm.PrevRequested += () => _playlistVm.PlayPrev();
        _playback.MediaEnded += () => Dispatcher.BeginInvoke(
            () => _playlistVm.PlayNext(manual: false));

        Loaded += (_, _) =>
        {
            VideoView.MediaPlayer = _playback.Player;
            // コマンドライン引数(「プログラムから開く」やスパイク検証用)
            var args = Environment.GetCommandLineArgs().Skip(1)
                .Where(File.Exists).ToList();
            if (args.Count > 0)
                FilesOpened?.Invoke(args);
        };

        // 開いたファイルはプレイリストへ置換投入して先頭を再生
        FilesOpened += files =>
        {
            if (files.Count == 0) return;
            _playlistVm.ReplaceAll(files);
            _playlistVm.PlayItem(_playlistVm.Items[0]);
        };
    }

    public void PlayFile(string path)
    {
        _currentMediaPath = path;
        _playback.Play(path);
        _playbackVm.NowPlayingTitle = Path.GetFileNameWithoutExtension(path);
        NowPlayingText.Text = "";
        Title = $"{_playbackVm.NowPlayingTitle} - YomiagePlayer";
        UpdateArtwork(path);
        MediaChanged?.Invoke(path);
    }

    private List<System.Windows.Media.Imaging.BitmapImage> _artworkImages = [];
    private int _artworkIndex;

    /// <summary>
    /// 音声ファイルならアートワーク(メタデータ埋め込み → 同フォルダ画像、複数あれば全件)を
    /// 映像エリアに表示する。動画・画像なしの場合は非表示。複数枚あれば矢印で切り替え可能。
    /// </summary>
    private void UpdateArtwork(string path)
    {
        ArtworkImage.Source = null;
        ArtworkImage.Visibility = Visibility.Collapsed;
        ArtworkNav.Visibility = Visibility.Collapsed;
        _artworkImages = [];
        _artworkIndex = 0;
        if (!MediaFiles.IsAudio(path)) return;

        var libraryRoot = FindLibraryRootFor(path);
        _ = Task.Run(() =>
        {
            var images = new List<System.Windows.Media.Imaging.BitmapImage>();
            foreach (var bytes in ArtworkLocator.FindAllArtwork(path, libraryRoot))
            {
                try
                {
                    using var ms = new MemoryStream(bytes);
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    bmp.Freeze(); // バックグラウンドスレッド生成のためUIスレッドへ渡す前に必須
                    images.Add(bmp);
                }
                catch
                {
                    // 壊れた画像データはスキップ(他の候補があれば使う)
                }
            }
            if (images.Count == 0) return;
            Dispatcher.BeginInvoke(() =>
            {
                if (_currentMediaPath != path) return; // 既に別トラックへ切替済み
                _artworkImages = images;
                _artworkIndex = 0;
                ShowCurrentArtwork();
            });
        });
    }

    private void ShowCurrentArtwork()
    {
        if (_artworkImages.Count == 0) return;
        ArtworkImage.Source = _artworkImages[_artworkIndex];
        ArtworkImage.Visibility = Visibility.Visible;
        ArtworkNav.Visibility = _artworkImages.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        ArtworkCounter.Text = $"{_artworkIndex + 1} / {_artworkImages.Count}";
    }

    private void ArtworkPrev_Click(object sender, RoutedEventArgs e)
    {
        if (_artworkImages.Count == 0) return;
        _artworkIndex = (_artworkIndex - 1 + _artworkImages.Count) % _artworkImages.Count;
        ShowCurrentArtwork();
    }

    private void ArtworkNext_Click(object sender, RoutedEventArgs e)
    {
        if (_artworkImages.Count == 0) return;
        _artworkIndex = (_artworkIndex + 1) % _artworkImages.Count;
        ShowCurrentArtwork();
    }

    /// <summary>mediaPathを含むライブラリ登録フォルダを返す(複数一致する場合は最も深いもの)。なければnull。</summary>
    private string? FindLibraryRootFor(string mediaPath)
    {
        string? best = null;
        foreach (var folder in _libraryVm.FolderPaths)
        {
            var normalized = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var prefix = normalized + Path.DirectorySeparatorChar;
            if (mediaPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && (best is null || normalized.Length > best.Length))
                best = normalized;
        }
        return best;
    }

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = FileDialogFilter, Multiselect = true };
        if (dialog.ShowDialog() == true)
            FilesOpened?.Invoke(dialog.FileNames);
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = FolderPicker.PickFolder();
        if (folder is not null)
            FilesOpened?.Invoke(EnumerateMediaFiles(folder));
    }

    private void RestoreSettings()
    {
        var s = _settingsStore.Load();
        _playbackVm.Volume = s.Volume;
        Width = s.WindowWidth;
        Height = s.WindowHeight;
        _libraryVm.SetFolders(s.RegisteredFolders);
    }

    private void SaveSettings()
    {
        var current = _settingsStore.Load();
        _settingsStore.Save(current with
        {
            RegisteredFolders = _libraryVm.FolderPaths.ToList(),
            Volume = _playbackVm.Volume,
            WindowWidth = Width,
            WindowHeight = Height,
        });
    }

    public static List<string> EnumerateMediaFiles(string folder)
        => MediaFiles.Enumerate(folder);

    /// <summary>設定画面を開く要求。App側でSettingsWindowを生成して表示する。</summary>
    public event Action? SettingsRequested;

    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_PreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        var files = new List<string>();
        foreach (var p in paths)
        {
            if (Directory.Exists(p)) files.AddRange(EnumerateMediaFiles(p));
            else if (MediaFiles.IsSupported(p)) files.Add(p);
        }
        if (files.Count > 0)
            FilesOpened?.Invoke(files);
    }
}
