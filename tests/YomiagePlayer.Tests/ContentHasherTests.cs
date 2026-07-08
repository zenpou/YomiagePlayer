using System.IO;
using YomiagePlayer.Core.Cache;

namespace YomiagePlayer.Tests;

public class ContentHasherTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    private string WriteTemp(byte[] data)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".bin");
        File.WriteAllBytes(path, data);
        _tempFiles.Add(path);
        return path;
    }

    [Fact]
    public void SameContent_DifferentPath_SameKey()
    {
        var data = new byte[3 * 1024 * 1024];
        new Random(1).NextBytes(data);
        var p1 = WriteTemp(data);
        var p2 = WriteTemp(data);
        Assert.Equal(ContentHasher.ComputeKey(p1), ContentHasher.ComputeKey(p2));
    }

    [Fact]
    public void DifferentHead_DifferentKey()
    {
        var data = new byte[3 * 1024 * 1024];
        var p1 = WriteTemp(data);
        data[0] ^= 0xFF;
        var p2 = WriteTemp(data);
        Assert.NotEqual(ContentHasher.ComputeKey(p1), ContentHasher.ComputeKey(p2));
    }

    [Fact]
    public void DifferentTail_DifferentKey()
    {
        var data = new byte[3 * 1024 * 1024];
        var p1 = WriteTemp(data);
        data[^1] ^= 0xFF;
        var p2 = WriteTemp(data);
        Assert.NotEqual(ContentHasher.ComputeKey(p1), ContentHasher.ComputeKey(p2));
    }

    [Fact]
    public void DifferentSize_SamePrefix_DifferentKey()
    {
        var small = new byte[512 * 1024];
        new Random(2).NextBytes(small);
        var big = small.Concat(new byte[1]).ToArray();
        Assert.NotEqual(
            ContentHasher.ComputeKey(WriteTemp(small)),
            ContentHasher.ComputeKey(WriteTemp(big)));
    }

    [Fact]
    public void SmallFile_UnderOneMegabyte_Works()
    {
        var data = new byte[100];
        var key = ContentHasher.ComputeKey(WriteTemp(data));
        Assert.Equal(64, key.Length); // sha256 hex
    }

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            try { File.Delete(f); } catch { }
    }
}
