using YomiagePlayer.Core.Models;
using YomiagePlayer.Core.Transcription;

namespace YomiagePlayer.Tests;

public class HallucinationFilterTests
{
    private readonly HallucinationFilter _f = new();

    [Theory]
    [InlineData("ご視聴ありがとうございました")]
    [InlineData("ご視聴ありがとうございました。")]
    [InlineData(" チャンネル登録お願いします ")]
    [InlineData("ご清聴ありがとうございました")]
    public void KnownPhrases_Dropped(string text)
        => Assert.True(_f.ShouldDrop(new(0, 2, text), null));

    [Theory]
    [InlineData("今日はいい天気ですね")]
    [InlineData("ありがとうって言ってくれた")] // 定型句を含むが前方一致しない
    public void NormalText_Kept(string text)
        => Assert.False(_f.ShouldDrop(new(0, 2, text), null));

    [Fact]
    public void ShortRepeat_Dropped()
    {
        var prev = new TranscriptSegment(0, 1.5, "そうそう");
        var cur = new TranscriptSegment(1.5, 3.0, "そうそう");
        Assert.True(_f.ShouldDrop(cur, prev));
    }

    [Fact]
    public void LongRepeat_Kept() // 歌のサビ等、長い正当な繰り返しは残す
    {
        var prev = new TranscriptSegment(0, 5, "ラララ君と歩いた夏の日");
        var cur = new TranscriptSegment(5, 10, "ラララ君と歩いた夏の日");
        Assert.False(_f.ShouldDrop(cur, prev));
    }

    [Fact]
    public void DifferentText_AfterPrevious_Kept()
    {
        var prev = new TranscriptSegment(0, 1.5, "こんにちは");
        var cur = new TranscriptSegment(1.5, 3.0, "こんばんは");
        Assert.False(_f.ShouldDrop(cur, prev));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("…")]
    [InlineData("。。。")]
    public void EmptyOrSymbolOnly_Dropped(string text)
        => Assert.True(_f.ShouldDrop(new(0, 1, text), null));

    [Theory]
    [InlineData("(笑)")]
    [InlineData("（笑）")]
    [InlineData("(笑)(笑)")]
    [InlineData("（拍手）")]
    [InlineData("[音楽]")]
    [InlineData("【咀嚼音】")]
    [InlineData("♪〜")]
    [InlineData(" (笑) ")]
    public void AnnotationOnlySegment_Dropped(string text)
        => Assert.True(_f.ShouldDrop(new(0, 1, text), null));

    [Theory]
    [InlineData("そうなんだ(笑)")]
    [InlineData("面白いですね（笑）本当に")]
    public void TextWithInlineAnnotation_Kept(string text)
        => Assert.False(_f.ShouldDrop(new(0, 2, text), null));
}
