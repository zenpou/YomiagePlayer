using System.Windows;
using YomiagePlayer.ViewModels;

namespace YomiagePlayer.UI;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
