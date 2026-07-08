using System.Globalization;
using System.Windows.Data;

namespace YomiagePlayer.UI;

/// <summary>double(秒) → "mm:ss" 表示。1時間超は "h:mm:ss"。</summary>
public class SecondsToTimeSpanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var t = TimeSpan.FromSeconds(value is double d && !double.IsNaN(d) ? d : 0);
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
