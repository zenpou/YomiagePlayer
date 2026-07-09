using System.IO;
using YomiagePlayer.Core.Library;

namespace YomiagePlayer.Tests;

public class ArtworkLocatorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public ArtworkLocatorTests() => Directory.CreateDirectory(_dir);

    // 1x1ピクセルの最小PNG(実画像である必要はなくバイト列比較にのみ使う)
    private static readonly byte[] FakeImageBytes = [0x89, 0x50, 0x4E, 0x47, 0x01, 0x02, 0x03];

    private string CreateFile(string name, byte[]? bytes = null)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, bytes ?? FakeImageBytes);
        return path;
    }

    private string CopyFixtureMp3(string name = "audio.mp3")
    {
        var src = FindFixture("tone-1s.mp3");
        var dst = Path.Combine(_dir, name);
        File.Copy(src, dst);
        return dst;
    }

    private static string FindFixture(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent!)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "fixtures", name);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException(name);
    }

    private static void EmbedPicture(string mp3Path, byte[] imageBytes) => EmbedPictures(mp3Path, imageBytes);

    private static void EmbedPictures(string mp3Path, params byte[][] imagesBytes)
    {
        using var tf = TagLib.File.Create(mp3Path);
        tf.Tag.Pictures = imagesBytes.Select(b => new TagLib.Picture(new TagLib.ByteVector(b))
        {
            Type = TagLib.PictureType.FrontCover,
            MimeType = "image/png",
        }).ToArray();
        tf.Save();
    }

    // ---- ディレクトリ画像の探索 ----

    [Fact]
    public void FindDirectoryImage_NoImages_ReturnsNull()
    {
        var media = CreateFile("song.mp3");
        CreateFile("readme.txt");
        Assert.Null(ArtworkLocator.FindDirectoryImage(media));
    }

    [Fact]
    public void FindDirectoryImage_PrefersSameBaseName()
    {
        var media = CreateFile("song.mp3");
        CreateFile("cover.jpg");
        var expected = CreateFile("song.png");
        Assert.Equal(expected, ArtworkLocator.FindDirectoryImage(media));
    }

    [Fact]
    public void FindDirectoryImage_PrefersKnownCoverNames()
    {
        var media = CreateFile("song.mp3");
        CreateFile("aaa.jpg");
        var expected = CreateFile("Cover.JPG"); // 大文字小文字は無視
        Assert.Equal(expected, ArtworkLocator.FindDirectoryImage(media));
    }

    [Fact]
    public void FindDirectoryImage_FallsBackToFirstImageAlphabetically()
    {
        var media = CreateFile("song.mp3");
        CreateFile("zzz.png");
        var expected = CreateFile("bbb.jpg");
        Assert.Equal(expected, ArtworkLocator.FindDirectoryImage(media));
    }

    [Fact]
    public void FindDirectoryImage_FallsBackToSiblingPhotoFolder()
    {
        // 実際の頒布構成: 作品フォルダ/MP3/曲.mp3 と 作品フォルダ/photo/表紙.jpg
        var mp3Dir = Path.Combine(_dir, "MP3");
        var photoDir = Path.Combine(_dir, "photo");
        Directory.CreateDirectory(mp3Dir);
        Directory.CreateDirectory(photoDir);
        var media = Path.Combine(mp3Dir, "01_song.mp3");
        File.WriteAllBytes(media, FakeImageBytes);
        var expected = Path.Combine(photoDir, "20211129.jpg");
        File.WriteAllBytes(expected, FakeImageBytes);

        Assert.Equal(expected, ArtworkLocator.FindDirectoryImage(media));
    }

    [Fact]
    public void FindDirectoryImage_FindsThumbnailDirectlyInParentFolder()
    {
        // fanza-downloaderの出力構成: 作品フォルダ/mp3/曲.mp3 と 作品フォルダ/thumbnail.jpg
        // (thumbnail.jpgはphoto等の名前を持つサブフォルダではなく親フォルダ直下に置かれる)
        var mp3Dir = Path.Combine(_dir, "mp3");
        Directory.CreateDirectory(mp3Dir);
        var media = Path.Combine(mp3Dir, "01_song.mp3");
        File.WriteAllBytes(media, FakeImageBytes);
        var expected = Path.Combine(_dir, "thumbnail.jpg");
        File.WriteAllBytes(expected, FakeImageBytes);

        Assert.Equal(expected, ArtworkLocator.FindDirectoryImage(media));
    }

    [Fact]
    public void FindDirectoryImage_FindsThumbnailTwoLevelsUpThroughNestedSubfolder()
    {
        // 実際の構成: 作品フォルダ/mp3/SEあり/曲.mp3 と 作品フォルダ/thumbnail.jpg
        // (SEあり/なしでさらに一段サブフォルダが分かれるケース)
        var nestedDir = Path.Combine(_dir, "mp3", "SEあり");
        Directory.CreateDirectory(nestedDir);
        var media = Path.Combine(nestedDir, "track01.mp3");
        File.WriteAllBytes(media, FakeImageBytes);
        var expected = Path.Combine(_dir, "thumbnail.jpg");
        File.WriteAllBytes(expected, FakeImageBytes);

        Assert.Equal(expected, ArtworkLocator.FindDirectoryImage(media));
    }

    [Fact]
    public void FindDirectoryImage_LibraryRoot_FindsImageArbitrarilyDeepAndNamed()
    {
        // ライブラリ登録フォルダ配下なら、祖先探索の階層制限(MaxAncestorDepth)や
        // 兄弟フォルダ名の制約(ImageFolderNames)を超えて、配下のどこにあっても見つかる
        var nestedDir = Path.Combine(_dir, "audio", "deep", "nested");
        Directory.CreateDirectory(nestedDir);
        var media = Path.Combine(nestedDir, "track01.mp3");
        File.WriteAllBytes(media, FakeImageBytes);
        var otherDir = Path.Combine(_dir, "artwork_stuff"); // ImageFolderNamesに含まれない名前
        Directory.CreateDirectory(otherDir);
        var expected = Path.Combine(otherDir, "cover.jpg");
        File.WriteAllBytes(expected, FakeImageBytes);

        Assert.Equal(expected, ArtworkLocator.FindDirectoryImage(media, libraryRoot: _dir));
    }

    [Fact]
    public void FindDirectoryImage_WithoutLibraryRoot_TooDeepToFindViaHeuristic()
    {
        // libraryRootを渡さない場合は祖先探索の階層制限に阻まれ見つからない
        // (LibraryRoot_FindsImageArbitrarilyDeepAndNamedと同じ構成でlibraryRoot省略)
        var nestedDir = Path.Combine(_dir, "audio", "deep", "nested");
        Directory.CreateDirectory(nestedDir);
        var media = Path.Combine(nestedDir, "track01.mp3");
        File.WriteAllBytes(media, FakeImageBytes);
        var otherDir = Path.Combine(_dir, "artwork_stuff");
        Directory.CreateDirectory(otherDir);
        File.WriteAllBytes(Path.Combine(otherDir, "cover.jpg"), FakeImageBytes);

        Assert.Null(ArtworkLocator.FindDirectoryImage(media));
    }

    [Fact]
    public void FindDirectoryImage_LibraryRoot_IgnoresUnrecognizedNamesEvenWithinRoot()
    {
        // ライブラリフォルダ配下でも、同名/定番名に一致しなければ採用しない
        // (「名前順で先頭」を配下全体に適用すると無関係な画像を拾ってしまうため)
        var mp3Dir = Path.Combine(_dir, "mp3");
        Directory.CreateDirectory(mp3Dir);
        var media = Path.Combine(mp3Dir, "01_song.mp3");
        File.WriteAllBytes(media, FakeImageBytes);
        File.WriteAllBytes(Path.Combine(_dir, "unrelated.jpg"), FakeImageBytes);

        Assert.Null(ArtworkLocator.FindDirectoryImage(media, libraryRoot: _dir));
    }

    [Fact]
    public void FindDirectoryImage_ParentFolderLooseImage_IgnoresUnrecognizedNames()
    {
        // 親フォルダ直下の画像は無関係なファイルのこともあるため、
        // 同名/定番名に一致しない限り採用しない(名前順で先頭、は適用しない)
        var mp3Dir = Path.Combine(_dir, "mp3");
        Directory.CreateDirectory(mp3Dir);
        var media = Path.Combine(mp3Dir, "01_song.mp3");
        File.WriteAllBytes(media, FakeImageBytes);
        File.WriteAllBytes(Path.Combine(_dir, "unrelated.jpg"), FakeImageBytes);

        Assert.Null(ArtworkLocator.FindDirectoryImage(media));
    }

    [Fact]
    public void FindDirectoryImage_IgnoresSiblingFoldersWithUnrecognizedNames()
    {
        var mp3Dir = Path.Combine(_dir, "MP3");
        var otherDir = Path.Combine(_dir, "Scenario"); // photo等の定番名ではない
        Directory.CreateDirectory(mp3Dir);
        Directory.CreateDirectory(otherDir);
        var media = Path.Combine(mp3Dir, "01_song.mp3");
        File.WriteAllBytes(media, FakeImageBytes);
        File.WriteAllBytes(Path.Combine(otherDir, "art.jpg"), FakeImageBytes);

        Assert.Null(ArtworkLocator.FindDirectoryImage(media));
    }

    // ---- メタデータ埋め込み画像 ----

    [Fact]
    public void TryGetEmbedded_Mp3WithPicture_ReturnsImageBytes()
    {
        var mp3 = CopyFixtureMp3();
        EmbedPicture(mp3, FakeImageBytes);
        Assert.Equal(FakeImageBytes, ArtworkLocator.TryGetEmbedded(mp3));
    }

    [Fact]
    public void TryGetEmbedded_NoPicture_ReturnsNull()
    {
        var mp3 = CopyFixtureMp3();
        Assert.Null(ArtworkLocator.TryGetEmbedded(mp3));
    }

    [Fact]
    public void TryGetEmbedded_BrokenFile_ReturnsNull()
    {
        var broken = CreateFile("broken.mp3", [0x00, 0x01]);
        Assert.Null(ArtworkLocator.TryGetEmbedded(broken));
    }

    [Fact]
    public void TryGetAllEmbedded_MultiplePictures_ReturnsAllInOrder()
    {
        var mp3 = CopyFixtureMp3();
        byte[] first = [0x01, 0x02];
        byte[] second = [0x03, 0x04];
        EmbedPictures(mp3, first, second);
        Assert.Equal([first, second], ArtworkLocator.TryGetAllEmbedded(mp3));
    }

    // ---- 統合(埋め込み優先 → ディレクトリ画像) ----

    [Fact]
    public void FindArtwork_EmbeddedWinsOverDirectoryImage()
    {
        var mp3 = CopyFixtureMp3();
        EmbedPicture(mp3, FakeImageBytes);
        CreateFile("cover.jpg", [0xFF, 0xFE]);
        Assert.Equal(FakeImageBytes, ArtworkLocator.FindArtwork(mp3));
    }

    [Fact]
    public void FindArtwork_FallsBackToDirectoryImage()
    {
        var mp3 = CopyFixtureMp3();
        CreateFile("cover.jpg", [0xFF, 0xFE]);
        Assert.Equal(new byte[] { 0xFF, 0xFE }, ArtworkLocator.FindArtwork(mp3));
    }

    [Fact]
    public void FindArtwork_NothingFound_ReturnsNull()
    {
        var mp3 = CopyFixtureMp3();
        Assert.Null(ArtworkLocator.FindArtwork(mp3));
    }

    [Fact]
    public void FindAllArtwork_MultipleEmbedded_ReturnsAllAndIgnoresDirectoryImages()
    {
        var mp3 = CopyFixtureMp3();
        byte[] first = [0x01, 0x02];
        byte[] second = [0x03, 0x04];
        EmbedPictures(mp3, first, second);
        CreateFile("cover.jpg", [0xFF, 0xFE]);
        Assert.Equal([first, second], ArtworkLocator.FindAllArtwork(mp3));
    }

    [Fact]
    public void FindDirectoryImages_MultipleImages_ReturnsAllWithBestFirst()
    {
        var media = CreateFile("song.mp3");
        var zzz = CreateFile("zzz.png");
        var cover = CreateFile("cover.jpg");
        Assert.Equal([cover, zzz], ArtworkLocator.FindDirectoryImages(media));
    }

    // ---- MediaFiles.IsAudio ----

    [Theory]
    [InlineData(@"C:\a\song.mp3", true)]
    [InlineData(@"C:\a\song.WAV", true)]
    [InlineData(@"C:\a\song.flac", true)]
    [InlineData(@"C:\a\movie.mp4", false)]
    [InlineData(@"C:\a\movie.mkv", false)]
    [InlineData(@"C:\a\note.txt", false)]
    public void MediaFiles_IsAudio(string path, bool expected)
        => Assert.Equal(expected, MediaFiles.IsAudio(path));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}
