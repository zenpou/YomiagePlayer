using YomiagePlayer.Core.Models;

namespace YomiagePlayer.Core.Transcription;

/// <summary>
/// 再生位置から現在のセグメントを探す。segmentsはStart昇順であること。
/// 区間は [Start, End) の半開区間。該当なし(無音区間・範囲外)は -1。
/// </summary>
public static class SegmentLocator
{
    public static int FindIndex(IReadOnlyList<TranscriptSegment> segments, double timeSec)
    {
        int lo = 0, hi = segments.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (segments[mid].Start > timeSec)
                hi = mid - 1;
            else
                lo = mid + 1;
        }
        // hi = Start <= timeSec を満たす最後のインデックス
        if (hi >= 0 && timeSec < segments[hi].End)
            return hi;
        return -1;
    }
}
