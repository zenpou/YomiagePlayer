using System.Text.RegularExpressions;
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

    // (笑) （拍手） [音楽] 【咀嚼音】等、無音・非音声区間でWhisperが生成しがちな注釈
    private static readonly Regex BracketedAnnotation =
        new(@"[（(\[【][^）)\]】]*[）)\]】]", RegexOptions.Compiled);

    public bool ShouldDrop(TranscriptSegment segment, TranscriptSegment? previous)
    {
        // 括弧書き注釈を除いて何も残らないセグメントは非音声とみなす
        // (「そうなんだ(笑)」のような本文中の注釈は残る)
        var withoutAnnotations = BracketedAnnotation.Replace(segment.Text, "");
        var normalized = Normalize(withoutAnnotations);

        if (normalized.Length == 0)
            return true;

        if (KnownPhrases.Any(p => normalized.StartsWith(Normalize(p), StringComparison.Ordinal)))
            return true;

        // 短時間の同文反復 = 繰り返しループ。長い反復は歌のサビ等の可能性があるので残す
        if (previous is not null
            && segment.End - segment.Start < RepeatMaxDurationSec
            && normalized == Normalize(BracketedAnnotation.Replace(previous.Text, "")))
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
