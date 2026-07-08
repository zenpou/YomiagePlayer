using System.Text;

namespace YomiagePlayer.Core.Library;

/// <summary>
/// .m3u8(UTF-8)プレイリストの読み書き。
/// 保存はフルパス、読み込みは相対パスをプレイリストファイル基準で解決する。
/// </summary>
public static class M3u8Serializer
{
    public static void Save(string playlistPath, IEnumerable<string> mediaPaths)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#EXTM3U");
        foreach (var path in mediaPaths)
        {
            sb.AppendLine($"#EXTINF:-1,{Path.GetFileNameWithoutExtension(path)}");
            sb.AppendLine(path);
        }
        File.WriteAllText(playlistPath, sb.ToString(), new UTF8Encoding(false));
    }

    public static List<string> Load(string playlistPath)
    {
        var baseDir = Path.GetDirectoryName(Path.GetFullPath(playlistPath)) ?? ".";
        var result = new List<string>();
        foreach (var raw in File.ReadAllLines(playlistPath, Encoding.UTF8))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            result.Add(Path.IsPathRooted(line)
                ? line
                : Path.GetFullPath(Path.Combine(baseDir, line)));
        }
        return result;
    }
}
