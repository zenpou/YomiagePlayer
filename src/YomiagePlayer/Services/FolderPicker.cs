using System.IO;
using Microsoft.Win32;

namespace YomiagePlayer.Services;

/// <summary>
/// Windows標準のMicrosoft.Win32.OpenFolderDialogは(FOS_PICKFOLDERS仕様上)
/// 選択中フォルダの中身のファイルを一切表示しない。mp3/wavが入っているか
/// 目視確認できず「間違ったフォルダを選んだのでは」と誤解されやすいため、
/// OpenFileDialogを流用しファイル一覧を見せながらフォルダを選ばせる。
/// </summary>
public static class FolderPicker
{
    private const string Filter =
        "メディアファイル|*.mp3;*.wav;*.flac;*.m4a;*.ogg;*.opus;*.mp4;*.mkv;*.avi;*.webm|すべてのファイル|*.*";

    /// <summary>フォルダを選ばせる。中のファイルが見える。キャンセル時はnull。</summary>
    public static string? PickFolder(string title = "フォルダを選択(中のファイルが確認できます)")
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = Filter,
            CheckFileExists = false,
            CheckPathExists = true,
            FileName = "このフォルダを選択",
        };
        return dialog.ShowDialog() == true ? Path.GetDirectoryName(dialog.FileName) : null;
    }
}
