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

    private static void EmbedPicture(string mp3Path, byte[] imageBytes)
    {
        using var tf = TagLib.File.Create(mp3Path);
        tf.Tag.Pictures =
        [
            new TagLib.Picture(new TagLib.ByteVector(imageBytes))
            {
                Type = TagLib.PictureType.FrontCover,
                MimeType = "image/png",
            },
        ];
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
