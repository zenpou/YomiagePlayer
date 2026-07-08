using FFMpegCore;

namespace YomiagePlayer.Core.Transcription;

/// <summary>FFMpegCoreが使うffmpegバイナリの場所を設定する。</summary>
public static class FfmpegConfig
{
    /// <summary>
    /// 指定フォルダのffmpeg.exeを使う。見つからなければPATH探索(FFMpegCoreデフォルト)のまま。
    /// アプリ起動時とテストセットアップから呼ぶ。
    /// </summary>
    public static bool Configure(string binaryFolder)
    {
        if (!File.Exists(Path.Combine(binaryFolder, "ffmpeg.exe")))
            return false;
        GlobalFFOptions.Configure(o => o.BinaryFolder = binaryFolder);
        return true;
    }

    /// <summary>startDirから上に辿って tools/ffmpeg を探して設定する(開発環境用)。</summary>
    public static bool ConfigureFromRepoTools(string startDir)
    {
        for (var dir = new DirectoryInfo(startDir); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "tools", "ffmpeg");
            if (Configure(candidate)) return true;
        }
        return false;
    }
}
