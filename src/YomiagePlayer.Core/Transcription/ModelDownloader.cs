using System.Security.Cryptography;

namespace YomiagePlayer.Core.Transcription;

/// <summary>
/// Hugging Face (ggerganov/whisper.cpp) からggmlモデルをダウンロードする。
/// 期待SHA256はLFSポインタファイル(/raw/)から取得し、実体(/resolve/)の
/// ダウンロード内容を検証。書き込みは .tmp → rename で原子的に行う。
/// </summary>
public class ModelDownloader(HttpClient http, string modelsDir)
{
    private const string BaseRaw = "https://huggingface.co/ggerganov/whisper.cpp/raw/main/";
    private const string BaseResolve = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/";

    public ModelDownloader(HttpClient http) : this(http, AppPaths.Models) { }

    public string PathFor(WhisperModel model) => Path.Combine(modelsDir, model.FileName());

    public bool IsDownloaded(WhisperModel model)
    {
        var path = PathFor(model);
        return File.Exists(path) && new FileInfo(path).Length > 0;
    }

    public async Task DownloadAsync(WhisperModel model, IProgress<double>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(modelsDir);
        var (expectedSha, expectedSize) = await FetchPointerAsync(model, ct).ConfigureAwait(false);

        var final = PathFor(model);
        var tmp = final + ".tmp";
        try
        {
            using var response = await http.GetAsync(
                BaseResolve + model.FileName(), HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using var sha = SHA256.Create();
            await using (var src = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var dst = File.Create(tmp))
            {
                var buffer = new byte[81920];
                long total = 0;
                int n;
                while ((n = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    sha.TransformBlock(buffer, 0, n, null, 0);
                    await dst.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                    total += n;
                    if (expectedSize > 0)
                        progress?.Report((double)total / expectedSize);
                }
            }
            sha.TransformFinalBlock([], 0, 0);

            var actualSha = Convert.ToHexStringLower(sha.Hash!);
            if (!string.Equals(actualSha, expectedSha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"モデルのチェックサムが一致しません: expected={expectedSha} actual={actualSha}");

            File.Move(tmp, final, overwrite: true);
            progress?.Report(1.0);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    private async Task<(string Sha256, long Size)> FetchPointerAsync(WhisperModel model, CancellationToken ct)
    {
        var text = await http.GetStringAsync(BaseRaw + model.FileName(), ct).ConfigureAwait(false);
        string? sha = null;
        long size = 0;
        foreach (var line in text.Split('\n'))
        {
            if (line.StartsWith("oid sha256:", StringComparison.Ordinal))
                sha = line["oid sha256:".Length..].Trim();
            else if (line.StartsWith("size ", StringComparison.Ordinal))
                _ = long.TryParse(line[5..].Trim(), out size);
        }
        if (sha is null)
            throw new InvalidDataException("LFSポインタからSHA256を取得できませんでした");
        return (sha, size);
    }
}
