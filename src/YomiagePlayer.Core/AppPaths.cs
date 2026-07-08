namespace YomiagePlayer.Core;

/// <summary>%AppData%\YomiagePlayer 配下の各ディレクトリ。</summary>
public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "YomiagePlayer");

    public static string Cache => Path.Combine(Root, "cache");
    public static string Models => Path.Combine(Root, "models");
    public static string Temp => Path.Combine(Root, "temp");
    public static string Logs => Path.Combine(Root, "logs");
    public static string SettingsFile => Path.Combine(Root, "settings.json");
}
