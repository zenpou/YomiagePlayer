using YomiagePlayer.Core.Models;

namespace YomiagePlayer.Core.Transcription;

/// <summary>
/// 文字起こしジョブの直列実行キュー。
/// - 同時実行は常に1件(Whisperのメモリ/GPU負荷を抑える)
/// - 新規ジョブはキュー先頭に割り込み(ユーザーが最後に開いた曲を最優先)
/// - 同一キーの実行中/待機中ジョブがあれば同じTaskを返す(重複解析の抑止)
/// - 待機数が上限を超えたら最も古い待機ジョブをキャンセル破棄
/// </summary>
public class TranscriptionQueue(int maxWaiting = 5)
{
    private sealed class QueueItem
    {
        public required string HashKey { get; init; }
        public required Func<CancellationToken, Task<TranscriptionResult>> Job { get; init; }
        public TaskCompletionSource<TranscriptionResult> Tcs { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private readonly object _lock = new();
    private readonly List<QueueItem> _waiting = [];
    private readonly CancellationTokenSource _shutdownCts = new();
    private QueueItem? _running;
    private Task _pump = Task.CompletedTask;

    /// <summary>実行中ジョブも待機ジョブもない状態。アイドル時バックグラウンド解析の判定に使う。</summary>
    public bool IsIdle
    {
        get { lock (_lock) return _running is null && _waiting.Count == 0; }
    }

    public Task<TranscriptionResult> Enqueue(
        string hashKey, Func<CancellationToken, Task<TranscriptionResult>> job)
    {
        lock (_lock)
        {
            if (_running?.HashKey == hashKey)
                return _running.Tcs.Task;
            var existing = _waiting.FirstOrDefault(i => i.HashKey == hashKey);
            if (existing is not null)
                return existing.Tcs.Task;

            var item = new QueueItem { HashKey = hashKey, Job = job };
            _waiting.Insert(0, item);

            while (_waiting.Count > maxWaiting)
            {
                var dropped = _waiting[^1];
                _waiting.RemoveAt(_waiting.Count - 1);
                dropped.Tcs.TrySetCanceled();
            }

            if (_running is null)
                _pump = PumpAsync();

            return item.Tcs.Task;
        }
    }

    private async Task PumpAsync()
    {
        while (true)
        {
            QueueItem item;
            lock (_lock)
            {
                if (_waiting.Count == 0)
                {
                    _running = null;
                    return;
                }
                item = _waiting[0];
                _waiting.RemoveAt(0);
                _running = item;
            }

            try
            {
                var result = await item.Job(_shutdownCts.Token).ConfigureAwait(false);
                item.Tcs.TrySetResult(result);
            }
            catch (OperationCanceledException)
            {
                item.Tcs.TrySetCanceled();
            }
            catch (Exception ex)
            {
                item.Tcs.TrySetException(ex);
            }
        }
    }

    public async Task ShutdownAsync()
    {
        Task pump;
        lock (_lock)
        {
            foreach (var item in _waiting)
                item.Tcs.TrySetCanceled();
            _waiting.Clear();
            pump = _pump;
        }
        _shutdownCts.Cancel();
        try { await pump.ConfigureAwait(false); } catch { }
    }
}
