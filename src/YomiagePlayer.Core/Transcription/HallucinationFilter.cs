using YomiagePlayer.Core.Models;

namespace YomiagePlayer.Core.Transcription;

/// <summary>
/// Whisperの無音ハルシネーション対策のテキストベースフィルタ。
/// 無音・非音声区間で生成されがちな定型句、短時間の同文反復、
/// 空・記号のみのセグメントを破棄する。
/// no_speech_prob等の確率ベースの対策はWhisper呼び出し側パラメータで行う。
/// </summary>
public class HallucinationFilter
{
    // 実サンプルでの検証結果に応じて追加していく
    private static readonly string[] KnownPhrases =
    [
        "ご視聴ありがとうございました",
        "ご清聴ありがとうございました",
        "チャンネル登録",
        "最後までご視聴",
        "おやすみなさい。おやすみなさい。",
        "字幕視聴ありがとうございました",
    ];

    private const double RepeatMaxDurationSec = 2.0;

    public bool ShouldDrop(TranscriptSegment segment, TranscriptSegment? previous)
    {
        var normalized = Normalize(segment.Text);

        if (normalized.Length == 0)
            return true;

        if (KnownPhrases.Any(p => normalized.StartsWith(Normalize(p), StringComparison.Ordinal)))
            return true;

        // 短時間の同文反復 = 繰り返しループ。長い反復は歌のサビ等の可能性があるので残す
        if (previous is not null
            && segment.End - segment.Start < RepeatMaxDurationSec
            && normalized == Normalize(previous.Text))
            return true;

        return false;
    }

    private static string Normalize(string text)
    {
        var chars = text.Where(c => !char.IsWhiteSpace(c) && (char.IsLetterOrDigit(c) || c > 0x3040))
            .Where(c => !char.IsPunctuation(c) && !char.IsSymbol(c));
        return new string(chars.ToArray());
    }
}
