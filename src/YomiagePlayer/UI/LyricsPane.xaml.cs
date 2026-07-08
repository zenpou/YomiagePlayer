using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using YomiagePlayer.ViewModels;

namespace YomiagePlayer.UI;

public partial class LyricsPane : UserControl
{
    private readonly DispatcherTimer _resumeTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool _autoScrolling;

    private LyricsViewModel? Vm => DataContext as LyricsViewModel;

    public LyricsPane()
    {
        InitializeComponent();

        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is LyricsViewModel oldVm)
                oldVm.CurrentRowChanged -= ScrollToRow;
            if (e.NewValue is LyricsViewModel newVm)
                newVm.CurrentRowChanged += ScrollToRow;
        };

        // 行クリックでシーク
        LyricsList.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource is System.Windows.DependencyObject src
                && ItemsControl.ContainerFromElement(LyricsList, src) is ListBoxItem { Content: SegmentRow row })
                Vm?.RequestSeek(row);
        };

        // ユーザー起因のスクロールで自動スクロールを一時停止(5秒無操作で復帰)
        LyricsList.AddHandler(MouseWheelEvent, new MouseWheelEventHandler((_, _) => SuspendAutoScroll()), true);
        LyricsList.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, e) =>
        {
            if (!_autoScrolling && e.VerticalChange != 0 && Mouse.LeftButton == MouseButtonState.Pressed)
                SuspendAutoScroll();
        }), true);

        _resumeTimer.Tick += (_, _) =>
        {
            _resumeTimer.Stop();
            Vm?.ResumeAutoScroll();
        };
    }

    private void SuspendAutoScroll()
    {
        Vm?.NotifyUserScrolled();
        _resumeTimer.Stop();
        _resumeTimer.Start();
    }

    private void ScrollToRow(int index)
    {
        if (Vm is null || Vm.IsAutoScrollSuspended) return;
        if (index < 0 || index >= LyricsList.Items.Count) return;
        _autoScrolling = true;
        LyricsList.ScrollIntoView(LyricsList.Items[index]);
        _autoScrolling = false;
    }
}
