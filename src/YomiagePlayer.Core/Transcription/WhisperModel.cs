namespace YomiagePlayer.Core.Transcription;

public enum WhisperModel
{
    Small,
    Medium,
    LargeV3Turbo,
}

public static class WhisperModelInfo
{
    /// <summary>ggml変換モデルのファイル名(Hugging Face ggerganov/whisper.cpp のファイル名と一致)</summary>
    public static string FileName(this WhisperModel model) => model switch
    {
        WhisperModel.Small => "ggml-small.bin",
        WhisperModel.Medium => "ggml-medium.bin",
        WhisperModel.LargeV3Turbo => "ggml-large-v3-turbo.bin",
        _ => throw new ArgumentOutOfRangeException(nameof(model)),
    };

    /// <summary>キャッシュファイル名や設定に使う識別子</summary>
    public static string Id(this WhisperModel model) => model switch
    {
        WhisperModel.Small => "small",
        WhisperModel.Medium => "medium",
        WhisperModel.LargeV3Turbo => "large-v3-turbo",
        _ => throw new ArgumentOutOfRangeException(nameof(model)),
    };

    public static string DisplayName(this WhisperModel model) => model switch
    {
        WhisperModel.Small => "Small (軽量・約500MB)",
        WhisperModel.Medium => "Medium (バランス・約1.5GB)",
        WhisperModel.LargeV3Turbo => "Large v3 Turbo (高精度・約1.6GB, GPU推奨)",
        _ => throw new ArgumentOutOfRangeException(nameof(model)),
    };

    public static bool TryParse(string id, out WhisperModel model)
    {
        foreach (var m in Enum.GetValues<WhisperModel>())
        {
            if (m.Id() == id) { model = m; return true; }
        }
        model = default;
        return false;
    }
}
