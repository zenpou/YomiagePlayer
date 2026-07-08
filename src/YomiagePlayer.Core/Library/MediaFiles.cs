namespace YomiagePlayer.Core.Library;

public static class MediaFiles
{
    public static readonly string[] SupportedExtensions =
        [".mp3", ".wav", ".flac", ".m4a", ".ogg", ".opus", ".mp4", ".mkv", ".avi", ".webm"];

    public static bool IsSupported(string path)
        => SupportedExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public static List<string> Enumerate(string folder)
        => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(IsSupported)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
