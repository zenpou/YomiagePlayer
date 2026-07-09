using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace YomiagePlayer.UI;

/// <summary>プレイリストのフォルダ見出し表示用: フルパスから末尾のフォルダ名だけを取り出す。</summary>
public class FolderPathToNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrEmpty(path)) return string.Empty;
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
        return string.IsNullOrEmpty(name) ? path : name;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
