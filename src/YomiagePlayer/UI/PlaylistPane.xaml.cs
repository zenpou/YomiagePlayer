using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using YomiagePlayer.ViewModels;

namespace YomiagePlayer.UI;

public partial class PlaylistPane : UserControl
{
    private const string M3u8Filter = "プレイリスト (*.m3u8)|*.m3u8";

    private PlaylistViewModel? Vm => DataContext as PlaylistViewModel;

    /// <summary>右クリック「再解析」。処理はTranscriptionCoordinator側で行う。</summary>
    public event Action<PlaylistItem>? ReanalyzeRequested;

    public PlaylistPane()
    {
        InitializeComponent();
    }

    private void List_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (List.SelectedItem is PlaylistItem item)
            Vm?.PlayItem(item);
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is PlaylistItem item)
            Vm?.Remove(item);
    }

    private void Reanalyze_Click(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is PlaylistItem item)
            ReanalyzeRequested?.Invoke(item);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = M3u8Filter, DefaultExt = ".m3u8" };
        if (dialog.ShowDialog() == true)
            Vm?.SaveTo(dialog.FileName);
    }

    private void Load_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = M3u8Filter };
        if (dialog.ShowDialog() == true)
            Vm?.LoadFrom(dialog.FileName);
    }
}
