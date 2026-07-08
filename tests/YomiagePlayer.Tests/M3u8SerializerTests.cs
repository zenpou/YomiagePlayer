using System.IO;
using YomiagePlayer.Core.Library;

namespace YomiagePlayer.Tests;

public class M3u8SerializerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public M3u8SerializerTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        var paths = new[] { @"C:\music\夏の歌.mp3", @"C:\videos\動画.mp4" };
        var file = Path.Combine(_dir, "list.m3u8");
        M3u8Serializer.Save(file, paths);
        Assert.Equal(paths, M3u8Serializer.Load(file));
    }

    [Fact]
    public void Load_RelativePaths_ResolvedAgainstPlaylistDir()
    {
        var file = Path.Combine(_dir, "list.m3u8");
        File.WriteAllText(file, "#EXTM3U\nsongs\\track.mp3\n");
        var loaded = M3u8Serializer.Load(file);
        Assert.Equal(Path.Combine(_dir, "songs", "track.mp3"), loaded[0]);
    }

    [Fact]
    public void Load_SkipsCommentsAndBlankLines()
    {
        var file = Path.Combine(_dir, "list.m3u8");
        File.WriteAllText(file, "#EXTM3U\n\n#EXTINF:-1,title\nC:\\a.mp3\n\n");
        Assert.Single(M3u8Serializer.Load(file));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}
