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
                // TagLibがPictureType.NotAPictureに分類するAPICフレームは、壊れた/空の
                // タグ付けツールが残したゴミであることが多い(実例: 12バイトの無効データ)。
                // これを表紙として採用すると、実際にはフォルダ内にある正しい画像への
                // フォールバックが一切走らなくなってしまうため除外する
                .Where(p => p.Type != TagLib.PictureType.NotAPicture && p.Data is { Count: > 0 })
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
    /// libraryRootが不明なら、直近の祖先から順に(MaxAncestorDepthまで)、
    /// .meta.json(作品フォルダの目印)があればそこを境界として配下を再帰的に探索し、
    /// なければそのフォルダ直下(非再帰)の画像のみを候補にする。祖先を無制限に、かつ
    /// 目印なしで再帰探索すると、共有フォルダ(ダウンロードフォルダ直下など)を丸ごと
    /// 舐めてしまう恐れがあるため、境界が確認できないフォルダでは深追いしない。
    /// 見つかったフォルダの画像を全件、優先順(メディアと同名 → 定番名 → 名前順)で先頭から返す。
    /// </summary>
    public static List<string> FindDirectoryImages(string mediaPath, string? libraryRoot = null)
    {
        var dir = Path.GetDirectoryName(mediaPath);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return [];

        if (libraryRoot is not null && Directory.Exists(libraryRoot) && IsAncestorOf(libraryRoot, dir))
        {
            // ライブラリ登録フォルダはユーザーが明示的に指定した境界なので、同ディレクトリに
            // 画像があるかどうかに関わらず配下全体を対象にする(フォルダ名・ファイル名も問わず
            // 全て表紙候補として採用)。同ディレクトリの画像だけを優先して他を無視すると、
            // 例えば作品フォルダ直下のthumbnail.jpgに隠れてサブフォルダの差分イラスト等が
            // 一切候補に上がらなくなってしまうため
            var libraryImages = ImagesUnder(libraryRoot);
            return libraryImages.Count > 0 ? OrderBestFirst(libraryImages, mediaPath) : [];
        }

        var direct = ImagesIn(dir);
        if (direct.Count > 0) return OrderBestFirst(direct, mediaPath);

        // libraryRootが不明な場合(ライブラリ経由で開かれていないファイル等)のフォールバック
        var ancestor = Directory.GetParent(dir);
        for (var depth = 0; ancestor is not null && depth < MaxAncestorDepth; depth++, ancestor = ancestor.Parent)
        {
            if (File.Exists(Path.Combine(ancestor.FullName, WorkRootMarkerFileName)))
            {
                // 目印(.meta.json)がある=作品フォルダの境界が確定するので、配下を再帰的に探索してよい
                var images = ImagesUnder(ancestor.FullName);
                return images.Count > 0 ? OrderBestFirst(images, mediaPath) : [];
            }

            // 目印がない祖先は無関係な共有フォルダの可能性があるため、直下の画像のみ(非再帰)を対象にし、
            // かつメディアと同名/定番名(cover/thumbnail等)に一致する場合のみ採用する
            // (「名前順で先頭」は適用しない=無関係な孤立ファイルを拾わないため)
            var ancestorImages = ImagesIn(ancestor.FullName);
            if (ancestorImages.Count > 0 && FindBestMatch(ancestorImages, mediaPath, requireMatch: true) is not null)
                return OrderBestFirst(ancestorImages, mediaPath);
        }

        return [];
    }

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
