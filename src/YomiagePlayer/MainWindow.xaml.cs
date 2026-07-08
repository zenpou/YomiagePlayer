using System.IO;
using System.Windows;
using Microsoft.Win32;
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

    /// <summary>ファイルが開かれた(D&D/ダイアログ)。後続タスクでプレイリスト/解析へ接続する。</summary>
    public event Action<IReadOnlyList<string>>? FilesOpened;

    public MainWindow(PlaybackService playback, PlaybackViewModel playbackVm, LyricsViewModel lyricsVm)
    {
        InitializeComponent();
        _playback = playback;
        _playbackVm = playbackVm;
        _lyricsVm = lyricsVm;
        Controls.DataContext = playbackVm;
        Lyrics.DataContext = lyricsVm;

        _lyricsVm.SeekRequested += s => _playback.SeekTo(TimeSpan.FromSeconds(s));
        _playback.PositionChanged += t => Dispatcher.BeginInvoke(
            () => _lyricsVm.UpdatePosition(t.TotalSeconds));
        _playback.LengthKnown += t => Dispatcher.BeginInvoke(
            () => _lyricsVm.SetDuration(t.TotalSeconds));

        Loaded += (_, _) =>
        {
            VideoView.MediaPlayer = _playback.Player;
            // コマンドライン引数(「プログラムから開く」やスパイク検証用)
            var args = Environment.GetCommandLineArgs().Skip(1)
                .Where(File.Exists).ToList();
            if (args.Count > 0)
                FilesOpened?.Invoke(args);
        };

        // Task 18でTranscriptionCoordinator/Playlistに置き換えるまでの仮接続: 開いたら即再生
        FilesOpened += files =>
        {
            if (files.Count == 0) return;
            PlayFile(files[0]);
        };
    }

    public void PlayFile(string path)
    {
        _playback.Play(path);
        _playbackVm.NowPlayingTitle = Path.GetFileNameWithoutExtension(path);
        NowPlayingText.Text = "";
        Title = $"{_playbackVm.NowPlayingTitle} - YomiagePlayer";
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
