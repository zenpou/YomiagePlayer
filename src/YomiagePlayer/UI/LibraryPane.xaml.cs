using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using YomiagePlayer.Services;
using YomiagePlayer.ViewModels;

namespace YomiagePlayer.UI;

public partial class LibraryPane : UserControl
{
    private LibraryViewModel? Vm => DataContext as LibraryViewModel;

    public LibraryPane()
    {
        InitializeComponent();
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = FolderPicker.PickFolder();
        if (folder is not null)
            Vm?.AddFolder(folder);
    }

    private void List_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // ダブルクリック=置換投入、Shift+ダブルクリック=追加投入
        if (List.SelectedItem is LibraryFolder folder)
            Vm?.OpenFolder(folder, append: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
    }

    private void OpenReplace_Click(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is LibraryFolder folder)
            Vm?.OpenFolder(folder, append: false);
    }

    private void OpenAppend_Click(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is LibraryFolder folder)
            Vm?.OpenFolder(folder, append: true);
    }

    private void RemoveFolder_Click(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is LibraryFolder folder)
            Vm?.RemoveFolderCommand.Execute(folder);
    }

    private void OpenInExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is LibraryFolder folder)
            ExplorerLauncher.OpenFolder(folder.Path);
    }
}
