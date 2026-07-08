using System.IO;
using YomiagePlayer.Core.Cache;
using YomiagePlayer.Core.Models;

namespace YomiagePlayer.Tests;

public class TranscriptionCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private TranscriptionCache Cache => new(_dir);

    private static TranscriptionResult Sample(string key = "abc123", string model = "medium") => new()
    {
        SourceFileName = "song.mp3",
        HashKey = key,
        Model = model,
        DurationSec = 245.3,
        Segments = [new(12.34, 15.10, "夏の終わりに聞こえた声")]
    };

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        Cache.Save(Sample());
        Assert.True(Cache.TryLoad("abc123", "medium", out var loaded));
        Assert.Equal("夏の終わりに聞こえた声", loaded!.Segments[0].Text);
        Assert.Equal(12.34, loaded.Segments[0].Start);
    }

    [Fact]
    public void TryLoad_Missing_ReturnsFalse()
        => Assert.False(Cache.TryLoad("nope", "medium", out _));

    [Fact]
    public void TryLoad_DifferentModel_ReturnsFalse()
    {
        Cache.Save(Sample(model: "medium"));
        Assert.False(Cache.TryLoad("abc123", "large-v3-turbo", out _));
    }

    [Fact]
    public void TryLoad_CorruptJson_ReturnsFalse()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "bad-medium.json"), "{ broken");
        Assert.False(Cache.TryLoad("bad", "medium", out _));
    }

    [Fact]
    public void Save_LeavesNoTmpFile()
    {
        Cache.Save(Sample());
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void Delete_RemovesCacheFile()
    {
        Cache.Save(Sample());
        Cache.Delete("abc123", "medium");
        Assert.False(Cache.TryLoad("abc123", "medium", out _));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}
