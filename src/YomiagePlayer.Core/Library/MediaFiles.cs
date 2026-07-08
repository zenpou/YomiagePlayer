namespace YomiagePlayer.Core.Library;

public static class MediaFiles
{
    public static readonly string[] AudioExtensions =
        [".mp3", ".wav", ".flac", ".m4a", ".ogg", ".opus"];

    public static readonly string[] SupportedExtensions =
        [.. AudioExtensions, ".mp4", ".mkv", ".avi", ".webm"];

    public static bool IsSupported(string path)
        => SupportedExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>映像を持たない音声ファイルか(アートワーク表示の対象判定)。</summary>
    public static bool IsAudio(string path)
        => AudioExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public static List<string> Enumerate(string folder)
        => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(IsSupported)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
