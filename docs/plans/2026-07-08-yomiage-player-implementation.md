# YomiagePlayer 実装プラン

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 音声/動画を日本語字幕付きで再生するWindowsメディアプレイヤーのMVPを構築する。

**Architecture:** WPF(MVVM) + Core層(再生/文字起こし/キャッシュ/ライブラリ)の3プロジェクト構成。LibVLCSharpで即時再生しつつ、Whisper.netによる文字起こしをキュー制御されたバックグラウンドジョブとして実行し、セグメントを歌詞パネルへストリーミング反映する。解析結果は内容ハッシュキーでJSONキャッシュする。

**Tech Stack:** .NET 10 (LTS) / WPF / LibVLCSharp / Whisper.net (+Cuda/Vulkanランタイム) / FFMpegCore (LGPL ffmpeg) / xUnit / Serilog / CommunityToolkit.Mvvm / GongSolutions.WPF.DragDrop

**設計書:** `docs/plans/2026-07-08-yomiage-player-design.md`(必ず先に読むこと)

**開発機情報:** GPU = NVIDIA RTX 3070 8GB(CUDA利用可)。既存SDKは .NET 7 のみ → Task 0 でインストール。

---

## Phase 0: 環境構築とスキャフォールド

### Task 0: .NET 10 SDK インストール

**Step 1: インストール**

```powershell
winget install Microsoft.DotNet.SDK.10 --accept-source-agreements --accept-package-agreements
```

**Step 2: 確認**(新しいシェルで)

```powershell
dotnet --list-sdks
```

Expected: `10.0.x` が表示される。

### Task 1: ソリューションとプロジェクト作成

**Files:**
- Create: `YomiagePlayer.sln`, `src/YomiagePlayer/`(WPF), `src/YomiagePlayer.Core/`(classlib), `tests/YomiagePlayer.Tests/`(xunit), `.gitignore`

**Step 1: スキャフォールド**

```powershell
cd c:\Users\takas\YomiagePlayer
dotnet new gitignore
dotnet new sln -n YomiagePlayer
dotnet new wpf -n YomiagePlayer -o src/YomiagePlayer -f net10.0
dotnet new classlib -n YomiagePlayer.Core -o src/YomiagePlayer.Core -f net10.0
dotnet new xunit -n YomiagePlayer.Tests -o tests/YomiagePlayer.Tests -f net10.0
dotnet sln add src/YomiagePlayer src/YomiagePlayer.Core tests/YomiagePlayer.Tests
dotnet add src/YomiagePlayer reference src/YomiagePlayer.Core
dotnet add tests/YomiagePlayer.Tests reference src/YomiagePlayer.Core
```

注: WPFプロジェクトのTFMは `net10.0-windows` に手動修正(`<TargetFramework>net10.0-windows</TargetFramework>`、`<UseWPF>true</UseWPF>` を確認)。

**Step 2: パッケージ導入**

```powershell
dotnet add src/YomiagePlayer package LibVLCSharp.WPF
dotnet add src/YomiagePlayer package VideoLAN.LibVLC.Windows
dotnet add src/YomiagePlayer package CommunityToolkit.Mvvm
dotnet add src/YomiagePlayer package Microsoft.Extensions.DependencyInjection
dotnet add src/YomiagePlayer package GongSolutions.WPF.DragDrop
dotnet add src/YomiagePlayer package Serilog.Sinks.File
dotnet add src/YomiagePlayer.Core package Whisper.net
dotnet add src/YomiagePlayer.Core package Whisper.net.Runtime
dotnet add src/YomiagePlayer.Core package Whisper.net.Runtime.Cuda
dotnet add src/YomiagePlayer.Core package Whisper.net.Runtime.Vulkan
dotnet add src/YomiagePlayer.Core package FFMpegCore
```

**Step 3: ビルド確認 → コミット**

```powershell
dotnet build
git add -A; git commit -m "chore: scaffold solution (WPF + Core + Tests)"
```

---

## Phase 1: Coreロジック(TDD)

各タスクは「失敗するテストを書く → 落ちることを確認 → 最小実装 → 通ることを確認 → コミット」の順で進める。テスト実行コマンドは共通:

```powershell
dotnet test tests/YomiagePlayer.Tests --filter "FullyQualifiedName~<TestClassName>"
```

### Task 2: Models

**Files:**
- Create: `src/YomiagePlayer.Core/Models/TranscriptSegment.cs`
- Create: `src/YomiagePlayer.Core/Models/TranscriptionResult.cs`

テスト不要(単純なrecord)。Task 3以降のテストで間接的に検証される。

```csharp
namespace YomiagePlayer.Core.Models;

public record TranscriptSegment(double Start, double End, string Text);
```

```csharp
namespace YomiagePlayer.Core.Models;

public record TranscriptionResult
{
    public int Version { get; init; } = 1;
    public required string SourceFileName { get; init; }
    public required string HashKey { get; init; }
    public required string Model { get; init; }
    public string Language { get; init; } = "ja";
    public double DurationSec { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
    public IReadOnlyList<TranscriptSegment> Segments { get; init; } = [];
}
```

コミット: `feat: add transcript models`

### Task 3: ハッシュキー算出 (ContentHasher)

**Files:**
- Create: `src/YomiagePlayer.Core/Cache/ContentHasher.cs`
- Test: `tests/YomiagePlayer.Tests/ContentHasherTests.cs`

**仕様:** `key = SHA256(fileSize(8byte LE) + 先頭1MB + 末尾1MB)` を小文字hex文字列で返す。1MB未満のファイルは全体を1回だけ読む(head=tail=全体だと二重に混ぜない。`head = 全体`, `tail = 空` とする)。ファイルは `FileShare.ReadWrite | FileShare.Delete` で開く(再生中でも読めるように)。

**Step 1: 失敗するテストを書く**

```csharp
using YomiagePlayer.Core.Cache;

public class ContentHasherTests
{
    private static string WriteTemp(byte[] data)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".bin");
        File.WriteAllBytes(path, data);
        return path;
    }

    [Fact]
    public void SameContent_DifferentPath_SameKey()
    {
        var data = new byte[3 * 1024 * 1024];
        new Random(1).NextBytes(data);
        var p1 = WriteTemp(data);
        var p2 = WriteTemp(data);
        Assert.Equal(ContentHasher.ComputeKey(p1), ContentHasher.ComputeKey(p2));
    }

    [Fact]
    public void DifferentHead_DifferentKey()
    {
        var data = new byte[3 * 1024 * 1024];
        var p1 = WriteTemp(data);
        data[0] ^= 0xFF;
        var p2 = WriteTemp(data);
        Assert.NotEqual(ContentHasher.ComputeKey(p1), ContentHasher.ComputeKey(p2));
    }

    [Fact]
    public void DifferentTail_DifferentKey()
    {
        var data = new byte[3 * 1024 * 1024];
        var p1 = WriteTemp(data);
        data[^1] ^= 0xFF;
        var p2 = WriteTemp(data);
        Assert.NotEqual(ContentHasher.ComputeKey(p1), ContentHasher.ComputeKey(p2));
    }

    [Fact]
    public void DifferentSize_SamePrefix_DifferentKey()
    {
        var small = new byte[512 * 1024];
        new Random(2).NextBytes(small);
        var big = small.Concat(new byte[1]).ToArray();
        Assert.NotEqual(ContentHasher.ComputeKey(WriteTemp(small)), ContentHasher.ComputeKey(WriteTemp(big)));
    }

    [Fact]
    public void SmallFile_UnderOneMegabyte_Works()
    {
        var data = new byte[100];
        var key = ContentHasher.ComputeKey(WriteTemp(data));
        Assert.Equal(64, key.Length); // sha256 hex
    }
}
```

**Step 2: 実行して失敗を確認**(`ContentHasher` 未定義でコンパイルエラー = 失敗でよい)

**Step 3: 実装**

```csharp
using System.Security.Cryptography;

namespace YomiagePlayer.Core.Cache;

public static class ContentHasher
{
    private const int ChunkSize = 1024 * 1024;

    public static string ComputeKey(string filePath)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var sha = SHA256.Create();

        var sizeBytes = BitConverter.GetBytes(fs.Length);
        sha.TransformBlock(sizeBytes, 0, sizeBytes.Length, null, 0);

        var buffer = new byte[ChunkSize];
        int headRead = ReadFully(fs, buffer);
        sha.TransformBlock(buffer, 0, headRead, null, 0);

        if (fs.Length > ChunkSize)
        {
            fs.Seek(-Math.Min(ChunkSize, fs.Length - ChunkSize), SeekOrigin.End);
            fs.Seek(Math.Max(fs.Length - ChunkSize, ChunkSize), SeekOrigin.Begin);
            int tailRead = ReadFully(fs, buffer);
            sha.TransformBlock(buffer, 0, tailRead, null, 0);
        }

        sha.TransformFinalBlock([], 0, 0);
        return Convert.ToHexStringLower(sha.Hash!);
    }

    private static int ReadFully(Stream s, byte[] buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int n = s.Read(buffer, total, buffer.Length - total);
            if (n == 0) break;
            total += n;
        }
        return total;
    }
}
```

注: tail読み出し位置は `max(fileLength - 1MB, 1MB)`(headと重複しない範囲で末尾1MB)。上記Seekの2行は意図を確認して1行に整理すること。

**Step 4: テストが通ることを確認 → Step 5: コミット** `feat: add content hash key computation`

### Task 4: キャッシュ読み書き (TranscriptionCache)

**Files:**
- Create: `src/YomiagePlayer.Core/Cache/TranscriptionCache.cs`
- Test: `tests/YomiagePlayer.Tests/TranscriptionCacheTests.cs`

**仕様:**
- コンストラクタでキャッシュディレクトリを受け取る(テスト容易性のため。本番は `%AppData%\YomiagePlayer\cache`)
- `Save(TranscriptionResult)`: `{hashKey}-{model}.json.tmp` へUTF-8で書き、`File.Move(tmp, final, overwrite: true)` で原子的に置換
- `TryLoad(string hashKey, string model, out TranscriptionResult?)`: 存在しない/JSON破損/version不一致なら false
- JSONは `System.Text.Json`、camelCase

**Step 1: 失敗するテストを書く**

```csharp
using YomiagePlayer.Core.Cache;
using YomiagePlayer.Core.Models;

public class TranscriptionCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private TranscriptionCache Cache => new(_dir);

    private static TranscriptionResult Sample(string key = "abc123", string model = "medium") => new()
    {
        SourceFileName = "song.mp3",
        HashKey = key,
        Model = model,
        DurationSec = 245.3,
        Segments = [new(12.34, 15.10, "夏の終わりに聞こえた声")]
    };

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        Cache.Save(Sample());
        Assert.True(Cache.TryLoad("abc123", "medium", out var loaded));
        Assert.Equal("夏の終わりに聞こえた声", loaded!.Segments[0].Text);
        Assert.Equal(12.34, loaded.Segments[0].Start);
    }

    [Fact]
    public void TryLoad_Missing_ReturnsFalse()
        => Assert.False(Cache.TryLoad("nope", "medium", out _));

    [Fact]
    public void TryLoad_DifferentModel_ReturnsFalse()
    {
        Cache.Save(Sample(model: "medium"));
        Assert.False(Cache.TryLoad("abc123", "large-v3-turbo", out _));
    }

    [Fact]
    public void TryLoad_CorruptJson_ReturnsFalse()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "bad-medium.json"), "{ broken");
        Assert.False(Cache.TryLoad("bad", "medium", out _));
    }

    [Fact]
    public void Save_LeavesNoTmpFile()
    {
        Cache.Save(Sample());
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }
}
```

**Step 2: 失敗確認 → Step 3: 実装**

```csharp
using System.Text.Json;
using YomiagePlayer.Core.Models;

namespace YomiagePlayer.Core.Cache;

public class TranscriptionCache(string cacheDir)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private string PathFor(string hashKey, string model)
        => Path.Combine(cacheDir, $"{hashKey}-{model}.json");

    public void Save(TranscriptionResult result)
    {
        Directory.CreateDirectory(cacheDir);
        var final = PathFor(result.HashKey, result.Model);
        var tmp = final + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(result, JsonOpts));
        File.Move(tmp, final, overwrite: true);
    }

    public bool TryLoad(string hashKey, string model, out TranscriptionResult? result)
    {
        result = null;
        var path = PathFor(hashKey, model);
        if (!File.Exists(path)) return false;
        try
        {
            result = JsonSerializer.Deserialize<TranscriptionResult>(File.ReadAllText(path), JsonOpts);
            if (result is null || result.Version != 1) { result = null; return false; }
            return true;
        }
        catch (JsonException) { return false; }
    }
}
```

**Step 4: テスト確認 → Step 5: コミット** `feat: add atomic transcription cache`

### Task 5: 再生位置→セグメント検索 (SegmentLocator)

**Files:**
- Create: `src/YomiagePlayer.Core/Transcription/SegmentLocator.cs`
- Test: `tests/YomiagePlayer.Tests/SegmentLocatorTests.cs`

**仕様:** 開始時刻でソート済みの `IReadOnlyList<TranscriptSegment>` と現在時刻(秒)を受け取り、`start <= t < end` を満たすセグメントのインデックスを二分探索で返す。無音区間(どのセグメントにも該当しない)は `-1`。逐次追加中でも呼ばれるため、リストが空なら `-1`。

**Step 1: テスト**

```csharp
using YomiagePlayer.Core.Models;
using YomiagePlayer.Core.Transcription;

public class SegmentLocatorTests
{
    private static readonly TranscriptSegment[] Segs =
    [
        new(10.0, 12.0, "a"),
        new(12.0, 15.0, "b"),
        new(40.0, 42.0, "c"), // 15〜40秒は無音
    ];

    [Theory]
    [InlineData(10.0, 0)]
    [InlineData(11.9, 0)]
    [InlineData(12.0, 1)]
    [InlineData(41.0, 2)]
    public void InsideSegment_ReturnsIndex(double t, int expected)
        => Assert.Equal(expected, SegmentLocator.FindIndex(Segs, t));

    [Theory]
    [InlineData(0.0)]    // 先頭より前
    [InlineData(20.0)]   // 無音区間
    [InlineData(15.0)]   // bのend丁度(半開区間なので外)
    [InlineData(99.0)]   // 末尾より後
    public void OutsideSegments_ReturnsMinusOne(double t)
        => Assert.Equal(-1, SegmentLocator.FindIndex(Segs, t));

    [Fact]
    public void EmptyList_ReturnsMinusOne()
        => Assert.Equal(-1, SegmentLocator.FindIndex([], 5.0));
}
```

**Step 2: 失敗確認 → Step 3: 実装**(`start` で二分探索し、見つかった候補の `end` を確認する。約15行)

**Step 4: 確認 → Step 5: コミット** `feat: add segment binary search locator`

### Task 6: ハルシネーションフィルタ (HallucinationFilter)

**Files:**
- Create: `src/YomiagePlayer.Core/Transcription/HallucinationFilter.cs`
- Test: `tests/YomiagePlayer.Tests/HallucinationFilterTests.cs`

**仕様:** セグメント単位の後処理フィルタ。`bool ShouldDrop(TranscriptSegment seg, TranscriptSegment? previous)`:
1. **定型句一致**: 正規化(空白除去)後、既知定型句リストと前方一致で照合。リスト初期値: 「ご視聴ありがとうございました」「チャンネル登録」「ご清聴ありがとうございました」「おやすみなさい。おやすみなさい。」等 + 実測で追加
2. **連続重複**: 直前セグメントと正規化テキストが完全一致し、かつ持続時間が2秒未満 → 破棄(繰り返しループ)
3. 空文字・記号のみ → 破棄

(no_speech_prob・圧縮率しきい値はWhisper.net呼び出し側パラメータで対応するため、このクラスはテキストベースの判定のみ)

**Step 1: テスト**

```csharp
using YomiagePlayer.Core.Models;
using YomiagePlayer.Core.Transcription;

public class HallucinationFilterTests
{
    private readonly HallucinationFilter _f = new();

    [Theory]
    [InlineData("ご視聴ありがとうございました")]
    [InlineData("ご視聴ありがとうございました。")]
    [InlineData(" チャンネル登録お願いします ")]
    public void KnownPhrases_Dropped(string text)
        => Assert.True(_f.ShouldDrop(new(0, 2, text), null));

    [Fact]
    public void NormalText_Kept()
        => Assert.False(_f.ShouldDrop(new(0, 2, "今日はいい天気ですね"), null));

    [Fact]
    public void ShortRepeat_Dropped()
    {
        var prev = new TranscriptSegment(0, 1.5, "そうそう");
        var cur = new TranscriptSegment(1.5, 3.0, "そうそう");
        Assert.True(_f.ShouldDrop(cur, prev));
    }

    [Fact]
    public void LongRepeat_Kept() // 歌のサビ等、長い正当な繰り返しは残す
    {
        var prev = new TranscriptSegment(0, 5, "ラララ君と歩いた夏の日");
        var cur = new TranscriptSegment(5, 10, "ラララ君と歩いた夏の日");
        Assert.False(_f.ShouldDrop(cur, prev));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("…")]
    public void EmptyOrSymbolOnly_Dropped(string text)
        => Assert.True(_f.ShouldDrop(new(0, 1, text), null));
}
```

**Step 2: 失敗確認 → Step 3: 実装 → Step 4: 確認 → Step 5: コミット** `feat: add hallucination text filter`

### Task 7: 解析キュー (TranscriptionQueue)

**Files:**
- Create: `src/YomiagePlayer.Core/Transcription/TranscriptionQueue.cs`
- Test: `tests/YomiagePlayer.Tests/TranscriptionQueueTests.cs`

**仕様:**
- `Task<TranscriptionResult> Enqueue(string hashKey, Func<CancellationToken, Task<TranscriptionResult>> job)`
- 同時実行は常に1件(直列)。新規Enqueueは**キュー先頭**に割り込み(LIFO優先。ユーザーが最後に開いた曲を最優先)
- 同一 `hashKey` が実行中/待機中なら新ジョブを積まず既存の `Task` を返す(重複抑止)
- 待機ジョブ数の上限は5。超えたら**最も古い待機ジョブ**をキャンセル扱いで破棄
- `ShutdownAsync()`: 実行中ジョブをキャンセルし完了を待つ

**Step 1: テスト**(`TaskCompletionSource` でジョブの開始/完了を手動制御して検証)

```csharp
using YomiagePlayer.Core.Models;
using YomiagePlayer.Core.Transcription;

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
}
```

**Step 2: 失敗確認 → Step 3: 実装**(`SemaphoreSlim(1,1)` + `List<QueueItem>` をlockで守る素直な実装。`maxWaiting` はコンストラクタ引数、デフォルト5)

**Step 4: 確認 → Step 5: コミット** `feat: add serialized transcription queue with LIFO priority`

---

## Phase 2: メディアパイプライン

### Task 8: 音声抽出 (AudioExtractor)

**Files:**
- Create: `src/YomiagePlayer.Core/Transcription/AudioExtractor.cs`
- Create: `src/YomiagePlayer.Core/AppPaths.cs`(AppData配下のパス定数: Cache/Models/Temp/Logs)
- Test: `tests/YomiagePlayer.Tests/AudioExtractorTests.cs`(ffmpeg必要な統合テストは `[Trait("Category","Integration")]`)

**仕様:**
- `Task<string> ExtractWavAsync(string mediaPath, CancellationToken ct)`: FFMpegCoreで16kHz mono 16bit PCM WAVを `AppPaths.Temp` に出力しパスを返す(`-vn -ac 1 -ar 16000`)。最初の音声トラック固定
- `CleanupTemp()`: temp内の全WAVを削除(起動時に呼ぶ)
- ffmpeg本体: `FFMpegCore` の `GlobalFFOptions` でバイナリパスを設定。**LGPLビルド**のffmpeg.exeを `tools/ffmpeg/` に配置(初回はREADMEに従い手動DL。入手元URLとLGPLであることを `tools/ffmpeg/README.md` に記録)
- 音声トラックなし/破損ファイルは `AudioExtractionException` を投げる

**Steps:** ユニットテスト(CleanupTemp、例外型)→ 実装 → 統合テストは実サンプルファイル(`tests/fixtures/` に短いwav/mp4を用意)で手動確認 → コミット `feat: add ffmpeg audio extraction`

### Task 9: Whisper文字起こし (WhisperTranscriber)

**Files:**
- Create: `src/YomiagePlayer.Core/Transcription/WhisperTranscriber.cs`
- Create: `src/YomiagePlayer.Core/Transcription/WhisperModel.cs`(enum: Small, Medium, LargeV3Turbo + ggmlファイル名/DL URL/SHA)

**仕様:**
- コンストラクタ: モデルファイルパス、`HallucinationFilter`
- `IAsyncEnumerable<TranscriptSegment> TranscribeAsync(string wavPath, CancellationToken ct)`:
  - `WhisperFactory.FromPath(modelPath)` → `CreateBuilder().WithLanguage("ja")`
  - `WithNoSpeechThreshold` / 温度・`condition_on_previous_text` 無効化など設計書のハルシネーション対策パラメータを設定(Whisper.netの現行APIで利用可能なものを確認して適用。Silero VAD統合(`WithVad...`)がAPIにあれば有効化、なければno_speech_probフィルタのみ)
  - セグメントごとに `HallucinationFilter.ShouldDrop` を通し、通過したものだけ yield
- ランタイム選択: `RuntimeOptions.RuntimeLibraryOrder = [Cuda, Vulkan, Cpu]`(アプリ起動時に1回設定)。実際にロードされたランタイム名を取得できるようにする(設定画面表示用)
- GPU実行はプロセス内で直列前提(TranscriptionQueueが保証)

**Steps:** ユニットテストは困難(ネイティブ依存)なので、`tests/fixtures/` の短い日本語音声で統合テスト(`[Trait("Category","Integration")]`)を書き、モデルは `small` でCI外実行。実装 → 手動確認 → コミット `feat: add whisper transcriber with streaming segments`

### Task 10: モデルダウンローダ (ModelDownloader)

**Files:**
- Create: `src/YomiagePlayer.Core/Transcription/ModelDownloader.cs`
- Test: `tests/YomiagePlayer.Tests/ModelDownloaderTests.cs`

**仕様:**
- `bool IsDownloaded(WhisperModel model)`: `AppPaths.Models` にファイルが存在しサイズ>0
- `Task DownloadAsync(WhisperModel model, IProgress<double> progress, CancellationToken ct)`: Hugging Face `ggerganov/whisper.cpp` リポジトリの ggml `.bin` をHttpClientでストリームDL(`.tmp`→rename)。SHA1/SHA256検証(URLとハッシュは `WhisperModel` 定義に持たせる)
- テスト: ローカルHTTPスタブ(小さなバイト列)でDL・進捗・ハッシュ不一致時の失敗を検証

**Steps:** テスト → 実装 → コミット `feat: add whisper model downloader with checksum verification`

---

## Phase 3: 再生とUI

### Task 11: スパイク — LibVLCSharp.WPF Airspace確認(コミット不要の使い捨て検証)

`src/YomiagePlayer/MainWindow` に仮の `VideoView` を置き、以下を実機確認して結果を `docs/plans/2026-07-08-spike-notes.md` に記録:
1. `VideoView.Content` にWPFコントロールを重ねて表示できるか
2. ウィンドウ全体への `AllowDrop` / Drop イベントが映像領域上でも発火するか
3. mp4再生・音声のみmp3再生の動作

**判断:** D&Dが映像上で効かない場合 → D&D受付はプレイリストペイン+メニューに限定(設計書に追記)。記録をコミット `docs: record airspace spike results`

### Task 12: PlaybackService

**Files:**
- Create: `src/YomiagePlayer/Services/PlaybackService.cs`

**仕様:** LibVLCSharpのラッパー。`Play(string path)` / `Pause()` / `Stop()` / `SeekTo(TimeSpan)` / `Volume` / イベント `PositionChanged(TimeSpan)`(200ms間隔で発火), `MediaEnded`, `LengthKnown(TimeSpan)`。`LibVLC` と `MediaPlayer` はシングルトンでDI登録。UIスレッドへのマーシャリングは購読側(VM)で行う。

**Steps:** 実装 → Task 13のUIから手動確認 → コミット `feat: add LibVLC playback service`

### Task 13: メインウィンドウ骨格 + 再生コントロール

**Files:**
- Modify: `src/YomiagePlayer/MainWindow.xaml`(3ペイン+コントロールバーのGrid)
- Create: `src/YomiagePlayer/ViewModels/PlaybackViewModel.cs`
- Create: `src/YomiagePlayer/UI/PlayerControls.xaml`(UserControl)
- Modify: `src/YomiagePlayer/App.xaml.cs`(DIコンテナ: Microsoft.Extensions.DependencyInjection。Serilog初期化。AppPaths.Temp掃除)

**仕様:**
- レイアウトは設計書のワイヤーフレーム通り(左: ライブラリ/プレイリスト縦分割、中央: VideoView+コントロール、右: 歌詞パネル)。右・左ペインは `GridSplitter` で幅調整可
- `PlaybackViewModel`(CommunityToolkit.Mvvm): PlayPauseCommand, NextCommand, PrevCommand, Position(双方向・シークバー), Duration, Volume, IsPlaying
- メニュー: ファイルを開く(OpenFileDialog, フィルタ: `*.mp3;*.wav;*.flac;*.m4a;*.ogg;*.mp4;*.mkv;*.avi;*.webm`)、フォルダを開く、設定、終了
- 「ファイルを開く→再生される」をここで手動確認

**Steps:** 実装 → `dotnet run` で手動確認(mp3とmp4を開いて再生・シーク・音量) → コミット `feat: main window layout and playback controls`

### Task 14: 歌詞パネル (LyricsPane)

**Files:**
- Create: `src/YomiagePlayer/UI/LyricsPane.xaml`
- Create: `src/YomiagePlayer/ViewModels/LyricsViewModel.cs`
- Test: `tests/YomiagePlayer.Tests/LyricsViewModelTests.cs`(VMロジックのみ)

**仕様:**
- `ObservableCollection<SegmentRowVM>` に逐次Add(UIスレッドへ `Dispatcher.Invoke`)
- `PositionChanged` 購読 → `SegmentLocator.FindIndex` → `CurrentIndex` 更新(無音は-1で全行非ハイライト)
- 自動スクロール: `CurrentIndex` 変更時に `ScrollIntoView`。ユーザーの手動スクロール(`ScrollChanged` でユーザー起因判定)後は5秒間 or「現在行に戻る」ボタン押下まで自動スクロール停止
- 行クリック → `SeekRequested(start)` イベント → PlaybackViewModelがシーク
- 状態表示: Idle(「未解析」)/ Analyzing(スピナー+「解析中 {progress}%」。progress = 最後のセグメントend ÷ duration)/ Ready / Failed(エラー+再解析ボタン)
- 解析済み区間より先へシーク中で該当行がない場合は「この区間は解析中です」バナー
- VMテスト: CurrentIndex更新、progress計算、手動スクロール中は自動スクロール要求が出ないこと

**Steps:** VMテスト → VM実装 → XAML → 手動確認(モックデータで) → コミット `feat: lyrics pane with streaming display and highlight`

### Task 15: プレイリスト (PlaylistPane)

**Files:**
- Create: `src/YomiagePlayer/UI/PlaylistPane.xaml`
- Create: `src/YomiagePlayer/ViewModels/PlaylistViewModel.cs`
- Create: `src/YomiagePlayer.Core/Library/M3u8Serializer.cs`
- Test: `tests/YomiagePlayer.Tests/M3u8SerializerTests.cs`, `PlaylistViewModelTests.cs`

**仕様:**
- `ObservableCollection<PlaylistItem>`(パス、表示名、長さ)。現在再生中の行をマーカー表示
- ダブルクリックで再生。GongSolutions.WPF.DragDropで並び替え。右クリック: 削除/プレイリスト保存/読み込み
- リピート(なし/全体/1曲)・シャッフルのモード管理と次曲選択ロジックはVMに実装(テスト対象)
- `M3u8Serializer`: `#EXTM3U`/`#EXTINF` 形式の読み書き(UTF-8、相対パスは保存先基準で解決)
- 曲終了(`MediaEnded`)→ 次曲自動再生

**Steps:** Serializer TDD → VM(次曲選択ロジック)TDD → XAML → 手動確認 → コミット `feat: playlist with reorder, repeat/shuffle, m3u8`

### Task 16: ライブラリ(フォルダ登録)+設定永続化

**Files:**
- Create: `src/YomiagePlayer/UI/LibraryPane.xaml`
- Create: `src/YomiagePlayer/ViewModels/LibraryViewModel.cs`
- Create: `src/YomiagePlayer.Core/Library/SettingsStore.cs`
- Test: `tests/YomiagePlayer.Tests/SettingsStoreTests.cs`

**仕様:**
- `SettingsStore`: `%AppData%\YomiagePlayer\settings.json` に登録フォルダ一覧・選択モデル・音量・ウィンドウサイズを保存(原子書き込みはTranscriptionCacheと同じ`.tmp`→rename方式。共通化できるならヘルパー抽出)
- LibraryPane: 登録フォルダをリスト表示、追加(FolderBrowserDialog)/削除。フォルダクリックで対応拡張子のファイルを列挙しプレイリストへ置換投入(Shift+クリックで追加投入)
- 起動時に前回設定を復元

**Steps:** SettingsStore TDD → VM/XAML → 手動確認 → コミット `feat: library folders and settings persistence`

### Task 17: 設定画面+ライセンス表記

**Files:**
- Create: `src/YomiagePlayer/UI/SettingsWindow.xaml`
- Create: `src/YomiagePlayer/ViewModels/SettingsViewModel.cs`
- Create: `docs/licenses/THIRD-PARTY-NOTICES.md`(LibVLC(LGPL2.1)/ffmpeg(LGPL)/Whisper.net(MIT)/whisper.cpp(MIT)/各NuGet)

**仕様:**
- モデル選択(small/medium/large-v3-turbo)。未DLモデル選択時はDL確認→進捗バー(`ModelDownloader`)
- 使用中ランタイム表示(CUDA/Vulkan/CPU)
- ライセンス表示タブ(THIRD-PARTY-NOTICES.mdを埋め込み表示)
- モデル変更後は次の解析から新モデル使用(キャッシュはファイル名で自動的に分離される)

**Steps:** 実装 → 手動確認 → コミット `feat: settings window with model management and licenses`

### Task 18: 統合 — TranscriptionCoordinator

**Files:**
- Create: `src/YomiagePlayer/Services/TranscriptionCoordinator.cs`
- Test: `tests/YomiagePlayer.Tests/TranscriptionCoordinatorTests.cs`(依存をインターフェース化してモック)

**仕様:** 「ファイルを開く」で呼ばれる中核オーケストレータ:
1. `ContentHasher.ComputeKey`(Task.Runで非同期化。完了前に再生は開始している)
2. `TranscriptionCache.TryLoad` → 命中なら `LyricsViewModel` へ一括ロード
3. 未命中なら `TranscriptionQueue.Enqueue`: AudioExtractor→WhisperTranscriber のパイプラインをジョブとして投入。セグメント到着イベントを `LyricsViewModel` へ中継(**現在表示中の曲のジョブのみ**UI反映。バックグラウンド完走ジョブはキャッシュ保存のみ)
4. 完了時 `TranscriptionCache.Save`、失敗時はFailed状態へ
5. アプリ終了時 `ShutdownAsync`(キャンセル→temp掃除)
6. 右クリック「再解析」: キャッシュファイル削除→再Enqueue

**Steps:** インターフェース抽出(`IAudioExtractor` 等)→ コーディネータのテスト(キャッシュ命中/未命中/曲切替時のUI切離し/失敗)→ 実装 → コミット `feat: wire transcription pipeline end-to-end`

---

## Phase 4: 統合確認

### Task 19: 手動統合テスト

チェックリスト(結果を `docs/plans/2026-07-08-integration-checklist.md` に記録):
- [ ] mp3(音声のみ)を開く→即再生+解析開始→歌詞が逐次出現→ハイライト追従→行クリックでシーク
- [ ] 同じファイルを開き直す→解析なしで歌詞即表示(キャッシュ命中)
- [ ] ファイルをリネームして開く→キャッシュ命中(ハッシュキー)
- [ ] mp4動画→映像表示+解析(音声抽出経由)
- [ ] BGM付き歌→歌詞精度を目視確認(不足ならボーカル分離を将来課題として起票)
- [ ] ささやきASMR→ハルシネーション(定型句繰り返し)が出ないこと
- [ ] 解析中に曲切替→新曲の解析が先に走り、旧曲も完走してキャッシュされる
- [ ] 解析中にアプリ終了→次回起動でtemp残骸なし・キャッシュ破損なし
- [ ] 音声トラックなし動画→エラー表示されるが再生は可能
- [ ] プレイリスト保存→読み込み→並び順維持
- [ ] リピート/シャッフル動作
- [ ] フォルダ登録→展開→再起動後も登録が残る
- [ ] モデル変更→DL→新モデルで解析
- [ ] GPU(CUDA)ランタイムがロードされていることを設定画面で確認

問題があれば superpowers:systematic-debugging で対処。完了後コミット `docs: record integration test results`

---

## 実装順序と依存関係

```
Task 0-1 (環境) → Task 2-7 (Core, 順不同可だが番号順推奨)
Task 8-10 (パイプライン) は Task 2 完了後着手可
Task 11 (スパイク) は Task 1 完了後いつでも(早めに)
Task 12-13 → Task 14-17 (UI, 14以降は相互独立) → Task 18 (全統合) → Task 19
```

## 注意事項

- コミットは各タスク完了ごと。テストが通らない状態でコミットしない
- Whisper.net / LibVLCSharp のAPIはバージョンで変わりやすい。実装時に `dotnet add package` した実バージョンのAPIを確認すること(特にVAD対応・RuntimeOptions)
- UIスレッド境界: Core層はスレッド非依存に保ち、`Dispatcher` への依存はViewModels層のみに置く
- 例外を握りつぶさない。解析失敗はログ(Serilog)+UI表示の両方へ
