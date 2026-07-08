using YomiagePlayer.Core.Models;
using YomiagePlayer.Core.Transcription;

namespace YomiagePlayer.Tests;

public class TranscriptionQueueTests
{
    private static TranscriptionResult Result(string key) => new()
    { SourceFileName = "x", HashKey = key, Model = "m" };

    [Fact]
    public async Task RunsJobsOneAtATime()
    {
        var q = new TranscriptionQueue();
        int concurrent = 0, maxConcurrent = 0;

        async Task<TranscriptionResult> Job(string key, CancellationToken ct)
        {
            var now = Interlocked.Increment(ref concurrent);
            InterlockedMax(ref maxConcurrent, now);
            await Task.Delay(50, ct);
            Interlocked.Decrement(ref concurrent);
            return Result(key);
        }

        var tasks = new[] { "a", "b", "c" }.Select(k => q.Enqueue(k, ct => Job(k, ct))).ToArray();
        await Task.WhenAll(tasks);
        Assert.Equal(1, maxConcurrent);

        static void InterlockedMax(ref int target, int value)
        {
            int snapshot;
            while (value > (snapshot = Volatile.Read(ref target)))
                if (Interlocked.CompareExchange(ref target, value, snapshot) == snapshot) return;
        }
    }

    [Fact]
    public async Task DuplicateKey_ReturnsSameTask()
    {
        var q = new TranscriptionQueue();
        var gate = new TaskCompletionSource();
        var t1 = q.Enqueue("k", async ct => { await gate.Task; return Result("k"); });
        var t2 = q.Enqueue("k", ct => Task.FromResult(Result("other")));
        Assert.Same(t1, t2);
        gate.SetResult();
        Assert.Equal("k", (await t1).HashKey);
    }

    [Fact]
    public async Task NewestJob_RunsBeforeOlderQueuedJobs()
    {
        var q = new TranscriptionQueue();
        var order = new List<string>();
        var gate = new TaskCompletionSource();
        // 実行中ジョブでキューを塞ぐ
        var blocker = q.Enqueue("blocker", async ct => { await gate.Task; return Result("blocker"); });
        var tOld = q.Enqueue("old", ct => { lock (order) order.Add("old"); return Task.FromResult(Result("old")); });
        var tNew = q.Enqueue("new", ct => { lock (order) order.Add("new"); return Task.FromResult(Result("new")); });
        gate.SetResult();
        await Task.WhenAll(blocker, tOld, tNew);
        Assert.Equal(["new", "old"], order);
    }

    [Fact]
    public async Task QueueOverflow_DropsOldestWaiting()
    {
        var q = new TranscriptionQueue(maxWaiting: 2);
        var gate = new TaskCompletionSource();
        var blocker = q.Enqueue("blocker", async ct => { await gate.Task; return Result("blocker"); });
        var t1 = q.Enqueue("w1", ct => Task.FromResult(Result("w1")));
        var t2 = q.Enqueue("w2", ct => Task.FromResult(Result("w2")));
        var t3 = q.Enqueue("w3", ct => Task.FromResult(Result("w3"))); // w1が破棄される
        gate.SetResult();
        await Task.WhenAll(blocker, t2, t3);
        await Assert.ThrowsAsync<TaskCanceledException>(() => t1);
    }

    [Fact]
    public async Task Shutdown_CancelsRunningJob()
    {
        var q = new TranscriptionQueue();
        var started = new TaskCompletionSource();
        var job = q.Enqueue("k", async ct =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return Result("k");
        });
        await started.Task;
        await q.ShutdownAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => job);
    }

    [Fact]
    public async Task FailedJob_PropagatesException()
    {
        var q = new TranscriptionQueue();
        var job = q.Enqueue("k", ct => Task.FromException<TranscriptionResult>(new InvalidOperationException("boom")));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => job);
        Assert.Equal("boom", ex.Message);
    }

    [Fact]
    public async Task CompletedKey_CanBeEnqueuedAgain()
    {
        var q = new TranscriptionQueue();
        await q.Enqueue("k", ct => Task.FromResult(Result("k")));
        // 完了後の再Enqueue(再解析)は新しいジョブとして受け付ける
        var second = await q.Enqueue("k", ct => Task.FromResult(Result("k")));
        Assert.Equal("k", second.HashKey);
    }
}
