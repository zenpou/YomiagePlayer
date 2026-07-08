using System.IO;
using Serilog;
using YomiagePlayer.Core.Library;
using YomiagePlayer.Core.Transcription;

namespace YomiagePlayer.Services;

/// <summary>
/// アイドル時(解析キューが空のとき)に、登録ライブラリフォルダ内の
/// 未解析ファイルを1件ずつバックグラウンド解析する。
/// - ユーザーがファイルを開くとそのジョブがキュー先頭に入るため、常にユーザー操作が優先
/// - 1tickにつき1ファイルのみ処理し、次のtickで再びアイドルなら続きを処理
/// - 処理済み/失敗したパスはセッション内で記憶して再走査しない
/// </summary>
public sealed class IdleAnalysisService(
    TranscriptionCoordinator coordinator,
    TranscriptionQueue queue,
    Func<IEnumerable<string>> folderProvider) : IDisposable
{
    private readonly HashSet<string> _visited = new(StringComparer.OrdinalIgnoreCase);
    private Timer? _timer;
    private int _scanning;

    public void Start(TimeSpan? interval = null)
    {
        _timer = new Timer(
            _ => _ = ScanOnceAsync(),
            null,
            dueTime: TimeSpan.FromSeconds(15),
            period: interval ?? TimeSpan.FromSeconds(20));
    }

    /// <summary>ライブラリ登録が変わったら再走査できるように記憶をリセットする。</summary>
    public void ResetVisited()
    {
        lock (_visited) _visited.Clear();
    }

    /// <summary>1件だけ処理を試みる。解析を実行したらtrue。</summary>
    public async Task<bool> ScanOnceAsync()
    {
        // 多重走査防止
        if (Interlocked.Exchange(ref _scanning, 1) == 1) return false;
        try
        {
            if (!queue.IsIdle) return false;

            foreach (var folder in folderProvider())
            {
                if (!Directory.Exists(folder)) continue;
                foreach (var file in MediaFiles.Enumerate(folder))
                {
                    lock (_visited)
                    {
                        if (!_visited.Add(file)) continue;
                    }

                    var analyzed = await coordinator.EnsureAnalyzedAsync(file).ConfigureAwait(false);
                    if (analyzed)
                    {
                        Log.Information("アイドル解析完了: {File}", Path.GetFileName(file));
                        return true; // 1tick 1件。続きは次のアイドルtickで
                    }
                }
            }
            return false;
        }
        finally
        {
            Volatile.Write(ref _scanning, 0);
        }
    }

    public void Dispose() => _timer?.Dispose();
}
