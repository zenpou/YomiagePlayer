namespace YomiagePlayer.Core.Library;

/// <summary>
/// 音声ファイルのアートワーク探索。メタデータ埋め込み画像を優先し、
/// なければ同ディレクトリの画像ファイルにフォールバックする。
/// </summary>
public static class ArtworkLocator
{
    private static readonly string[] ImageExtensions =
        [".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif"];

    // 同ディレクトリ画像のうちジャケットとして扱われがちな定番名(優先順)
    private static readonly string[] PreferredNames =
        ["cover", "folder", "front", "album", "jacket", "artwork"];

    /// <summary>埋め込み → ディレクトリ画像の順で探し、画像バイト列を返す。なければnull。</summary>
    public static byte[]? FindArtwork(string mediaPath)
    {
        var embedded = TryGetEmbedded(mediaPath);
        if (embedded is not null) return embedded;

        var imagePath = FindDirectoryImage(mediaPath);
        if (imagePath is null) return null;
        try { return File.ReadAllBytes(imagePath); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>メタデータ(ID3等)に埋め込まれた最初の画像を返す。なければ/読めなければnull。</summary>
    public static byte[]? TryGetEmbedded(string mediaPath)
    {
        try
        {
            using var file = TagLib.File.Create(mediaPath);
            var picture = file.Tag.Pictures.FirstOrDefault(p => p.Data is { Count: > 0 });
            return picture?.Data.Data;
        }
        catch
        {
            // 未対応形式・壊れたタグはアートワークなし扱い(再生自体には影響させない)
            return null;
        }
    }

    /// <summary>
    /// メディアと同じディレクトリから画像ファイルを探す。
    /// 優先順: メディアと同名 → 定番名(cover/folder/...) → 名前順で先頭。
    /// </summary>
    public static string? FindDirectoryImage(string mediaPath)
    {
        var dir = Path.GetDirectoryName(mediaPath);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;

        var images = Directory.EnumerateFiles(dir)
            .Where(f => ImageExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (images.Count == 0) return null;

        var mediaBase = Path.GetFileNameWithoutExtension(mediaPath);
        var sameName = images.FirstOrDefault(f => string.Equals(
            Path.GetFileNameWithoutExtension(f), mediaBase, StringComparison.OrdinalIgnoreCase));
        if (sameName is not null) return sameName;

        foreach (var name in PreferredNames)
        {
            var hit = images.FirstOrDefault(f => string.Equals(
                Path.GetFileNameWithoutExtension(f), name, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) return hit;
        }

        return images[0];
    }
}
