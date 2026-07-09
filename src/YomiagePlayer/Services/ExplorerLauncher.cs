using System.Diagnostics;
using Serilog;

namespace YomiagePlayer.Services;

/// <summary>エクスプローラーでファイル/フォルダを開く。失敗してもアプリを落とさない。</summary>
public static class ExplorerLauncher
{
    /// <summary>ファイルを選択した状態でエクスプローラーを開く。</summary>
    public static void ShowFile(string filePath)
    {
        try
        {
            Process.Start("explorer.exe", $"/select,\"{filePath}\"");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "エクスプローラーの起動に失敗しました: {Path}", filePath);
        }
    }

    /// <summary>フォルダをエクスプローラーで開く。</summary>
    public static void OpenFolder(string folderPath)
    {
        try
        {
            Process.Start(new ProcessStartInfo(folderPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "エクスプローラーの起動に失敗しました: {Path}", folderPath);
        }
    }
}
