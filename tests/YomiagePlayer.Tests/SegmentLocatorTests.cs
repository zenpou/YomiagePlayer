using YomiagePlayer.Core.Models;
using YomiagePlayer.Core.Transcription;

namespace YomiagePlayer.Tests;

public class SegmentLocatorTests
{
    private static readonly TranscriptSegment[] Segs =
    [
        new(10.0, 12.0, "a"),
        new(12.0, 15.0, "b"),
        new(40.0, 42.0, "c"), // 15〜40秒は無音
    ];

    [Theory]
    [InlineData(10.0, 0)]
    [InlineData(11.9, 0)]
    [InlineData(12.0, 1)]
    [InlineData(41.0, 2)]
    public void InsideSegment_ReturnsIndex(double t, int expected)
        => Assert.Equal(expected, SegmentLocator.FindIndex(Segs, t));

    [Theory]
    [InlineData(0.0)]    // 先頭より前
    [InlineData(20.0)]   // 無音区間
    [InlineData(15.0)]   // bのend丁度(半開区間なので外)
    [InlineData(99.0)]   // 末尾より後
    public void OutsideSegments_ReturnsMinusOne(double t)
        => Assert.Equal(-1, SegmentLocator.FindIndex(Segs, t));

    [Fact]
    public void EmptyList_ReturnsMinusOne()
        => Assert.Equal(-1, SegmentLocator.FindIndex([], 5.0));
}
