namespace YomiagePlayer.Core.Library;

/// <summary>
/// 音声ファイルのアートワーク探索。メタデータ埋め込み画像を優先し、
/// なければ同ディレクトリの画像ファイルにフォールバックする。
/// 候補が複数ある場合は全件返せる(切り替え表示用)。
/// </summary>
public static class ArtworkLocator
{
    private static readonly string[] ImageExtensions =
        [".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif"];

    // 同ディレクトリ画像のうちジャケットとして扱われがちな定番名(優先順)
    // "thumbnail"はfanza-downloaderが作品フォルダ直下に生成する表紙ファイル名
    private static readonly string[] PreferredNames =
        ["cover", "folder", "front", "album", "jacket", "artwork", "thumbnail"];

    // 音声を「MP3」「wav」等のサブフォルダに分け、画像は兄弟フォルダにまとめる
    // 頒布物(同人音声作品など)がよくある構成のため、これらの名前の兄弟/配下フォルダも探す。
    // 部分一致で判定するため(「①イメージ」のような連番接頭辞付きフォルダ名にも対応)、
    // 英語の汎用語(「art」等)は除外し、誤検出しにくい語のみを残す
    private static readonly string[] ImageFolderNames =
        ["photo", "photos", "image", "images", "img", "jacket", "jackets", "artwork", "scan", "scans", "cover",
         "イメージ", "画像", "ジャケット", "パッケージ", "表紙", "ポスター"];

    // 親フォルダを何階層まで遡って表紙画像を探すか(例: 作品/mp3/SEあり/曲.mp3 のような
    // 多段サブフォルダでも作品フォルダ直下のthumbnail.jpgに辿り着けるようにする)。
    // 際限なく遡ると無関係な共有フォルダの画像を拾う恐れがあるため、必要最小限に留める
    private const int MaxAncestorDepth = 2;

    // fanza-downloaderが作品フォルダ直下に生成するマーカーファイル。
    // これがあるフォルダは「作品フォルダ」なので、それより上(複数作品を束ねる
    // 共有ライブラリフォルダ)へは表紙画像を探しに行かない
    private const string WorkRootMarkerFileName = ".meta.json";

    /// <summary>埋め込み → ディレクトリ画像の順で探し、先頭の画像バイト列を返す。なければnull。</summary>
    /// <param name="libraryRoot">メディアが属するライブラリ登録フォルダ(分かれば)。指定時はこのフォルダ配下を再帰的に探す。</param>
    public static byte[]? FindArtwork(string mediaPath, string? libraryRoot = null) =>
        FindAllArtwork(mediaPath, libraryRoot).FirstOrDefault();

    /// <summary>
    /// 埋め込み画像(複数可)があれば全て、なければディレクトリ画像(複数可)を全て返す。
    /// 切り替え表示用。埋め込みとディレクトリ画像は混在させない。
    /// </summary>
    public static List<byte[]> FindAllArtwork(string mediaPath, string? libraryRoot = null)
    {
        var embedded = TryGetAllEmbedded(mediaPath);
        if (embedded.Count > 0) return embedded;

        var result = new List<byte[]>();
        foreach (var imagePath in FindDirectoryImages(mediaPath, libraryRoot))
        {
            try { result.Add(File.ReadAllBytes(imagePath)); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return result;
    }

    /// <summary>メタデータ(ID3等)に埋め込まれた最初の画像を返す。なければ/読めなければnull。</summary>
    public static byte[]? TryGetEmbedded(string mediaPath) => TryGetAllEmbedded(mediaPath).FirstOrDefault();

    /// <summary>メタデータ(ID3等)に埋め込まれた画像を全て返す。なければ/読めなければ空リスト。</summary>
    public static List<byte[]> TryGetAllEmbedded(string mediaPath)
    {
        try
        {
            using var file = TagLib.File.Create(mediaPath);
            return file.Tag.Pictures
                .Where(p => p.Data is { Count: > 0 })
                .Select(p => p.Data.Data)
                .ToList();
        }
        catch
        {
            // 未対応形式・壊れたタグはアートワークなし扱い(再生自体には影響させない)
            return [];
        }
    }

    /// <summary>メディアと同じディレクトリ(またはライブラリフォルダ配下/祖先)から画像ファイルを1件探す。なければnull。</summary>
    public static string? FindDirectoryImage(string mediaPath, string? libraryRoot = null) =>
        FindDirectoryImages(mediaPath, libraryRoot).FirstOrDefault();

    /// <summary>
    /// メディアと同じディレクトリから画像ファイルを探す。見つからなければ、
    /// libraryRoot(ライブラリに登録されたフォルダ)が分かればその配下の画像を
    /// フォルダ名・ファイル名を問わず全て候補として返す(例: 作品フォルダをライブラリ登録した場合、
    /// 作品フォルダ/mp3/曲.mp3 の表紙が作品フォルダ/thumbnail.jpg やどのサブフォルダにあっても見つかる)。
    /// libraryRootが不明なら、音声を「作品フォルダ/mp3/」やさらに「作品フォルダ/mp3/SEあり/」
    /// のように多段のサブフォルダに分けた構成を想定し祖先フォルダを辿るヒューリスティックと、
    /// 音声/画像をサブフォルダで分けた構成(例: 作品/MP3/曲.mp3 と 作品/photo/表紙.jpg)を
    /// 想定した兄弟フォルダ探索にフォールバックする(こちらは無関係な画像を拾わないよう、
    /// 同名/定番名/画像フォルダ名の一致がある場合のみ採用する)。
    /// 見つかったフォルダの画像を全件、優先順(メディアと同名 → 定番名 → 名前順)で先頭から返す。
    /// </summary>
    public static List<string> FindDirectoryImages(string mediaPath, string? libraryRoot = null)
    {
        var dir = Path.GetDirectoryName(mediaPath);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return [];

        var direct = ImagesIn(dir);
        if (direct.Count > 0) return OrderBestFirst(direct, mediaPath);

        if (libraryRoot is not null && Directory.Exists(libraryRoot) && IsAncestorOf(libraryRoot, dir))
        {
            // ライブラリ登録フォルダはユーザーが明示的に指定した境界なので、配下にある
            // 画像はフォルダ名・ファイル名を問わず全て表紙候補として採用する(切り替え表示用)
            var libraryImages = ImagesUnder(libraryRoot);
            return libraryImages.Count > 0 ? OrderBestFirst(libraryImages, mediaPath) : [];
        }

        // libraryRootが不明な場合(ライブラリ経由で開かれていないファイル等)のヒューリスティック。
        // 祖先フォルダは無関係なファイルも同居しうる共有ディレクトリのことがあるため、
        // 同名/定番名の一致がある場合のみ採用する(「名前順で先頭」は適用しない)。
        // .meta.json(作品フォルダの目印)に達したらそこで打ち切り、それより上の
        // 共有ライブラリフォルダへは探しに行かない
        var ancestor = Directory.GetParent(dir);
        for (var depth = 0; ancestor is not null && depth < MaxAncestorDepth; depth++, ancestor = ancestor.Parent)
        {
            var ancestorImages = ImagesIn(ancestor.FullName);
            if (ancestorImages.Count > 0 && FindBestMatch(ancestorImages, mediaPath, requireMatch: true) is not null)
                return OrderBestFirst(ancestorImages, mediaPath);

            if (File.Exists(Path.Combine(ancestor.FullName, WorkRootMarkerFileName))) break;
        }

        var parent = Directory.GetParent(dir);
        if (parent is null) return [];

        foreach (var sibling in parent.GetDirectories())
        {
            if (string.Equals(sibling.FullName, dir, StringComparison.OrdinalIgnoreCase)) continue;
            if (!IsImageFolderName(sibling.Name)) continue;
            var siblingImages = ImagesIn(sibling.FullName);
            if (siblingImages.Count > 0) return OrderBestFirst(siblingImages, mediaPath);
        }

        return [];
    }

    /// <summary>フォルダ名が画像専用フォルダの命名慣習(部分一致)に合致するか。</summary>
    private static bool IsImageFolderName(string folderName) =>
        ImageFolderNames.Any(name => folderName.Contains(name, StringComparison.OrdinalIgnoreCase));

    private static bool IsAncestorOf(string ancestorDir, string dir)
    {
        var normalized = ancestorDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(dir, normalized, StringComparison.OrdinalIgnoreCase)
            || dir.StartsWith(normalized + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> ImagesIn(string dir) =>
        Directory.EnumerateFiles(dir)
            .Where(f => ImageExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static List<string> ImagesUnder(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => ImageExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>一番手にすべき画像を先頭にし、残りを名前順で続ける。</summary>
    private static List<string> OrderBestFirst(List<string> images, string mediaPath)
    {
        var best = FindBestMatch(images, mediaPath, requireMatch: false);
        if (best is null) return images;
        return [best, .. images.Where(f => f != best)];
    }

    /// <summary>メディアと同名 → 定番名(cover/folder/...)の順で一致を探す。requireMatch=falseなら先頭にフォールバック。</summary>
    private static string? FindBestMatch(List<string> images, string mediaPath, bool requireMatch)
    {
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

        return requireMatch ? null : images.FirstOrDefault();
    }
}
