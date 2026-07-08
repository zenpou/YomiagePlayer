using System.IO;
using System.Windows;
using Microsoft.Win32;
using YomiagePlayer.Core.Library;
using YomiagePlayer.Services;
using YomiagePlayer.ViewModels;

namespace YomiagePlayer;

public partial class MainWindow : Window
{
    public static readonly string[] SupportedExtensions =
        [".mp3", ".wav", ".flac", ".m4a", ".ogg", ".opus", ".mp4", ".mkv", ".avi", ".webm"];

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
        _playback.Play(path);
        _playbackVm.NowPlayingTitle = Path.GetFileNameWithoutExtension(path);
        NowPlayingText.Text = "";
        Title = $"{_playbackVm.NowPlayingTitle} - YomiagePlayer";
        MediaChanged?.Invoke(path);
    }

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = FileDialogFilter, Multiselect = true };
        if (dialog.ShowDialog() == true)
            FilesOpened?.Invoke(dialog.FileNames);
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog() == true)
            FilesOpened?.Invoke(EnumerateMediaFiles(dialog.FolderName));
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
        => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        // Task 17で実装
        MessageBox.Show("設定画面は未実装です", "YomiagePlayer");
    }

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
            else if (SupportedExtensions.Contains(Path.GetExtension(p).ToLowerInvariant())) files.Add(p);
        }
        if (files.Count > 0)
            FilesOpened?.Invoke(files);
    }
}
