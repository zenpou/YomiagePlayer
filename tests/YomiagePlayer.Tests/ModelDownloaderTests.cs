using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using YomiagePlayer.Core.Transcription;

namespace YomiagePlayer.Tests;

public class ModelDownloaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(responder(request));
    }

    private static ModelDownloader Create(string dir, byte[] payload, string? pointerSha = null)
    {
        var sha = pointerSha ?? Convert.ToHexStringLower(SHA256.HashData(payload));
        var handler = new StubHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("/raw/"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $"version https://git-lfs.github.com/spec/v1\noid sha256:{sha}\nsize {payload.Length}\n")
                };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload)
            };
        });
        return new ModelDownloader(new HttpClient(handler), dir);
    }

    [Fact]
    public void IsDownloaded_MissingFile_False()
        => Assert.False(Create(_dir, [1]).IsDownloaded(WhisperModel.Small));

    [Fact]
    public async Task Download_Success_FileExistsAndProgressReported()
    {
        var payload = Encoding.UTF8.GetBytes(new string('x', 100_000));
        var d = Create(_dir, payload);
        var progress = new List<double>();
        await d.DownloadAsync(WhisperModel.Small, new Progress<double>(progress.Add), CancellationToken.None);

        Assert.True(d.IsDownloaded(WhisperModel.Small));
        Assert.Equal(payload, File.ReadAllBytes(d.PathFor(WhisperModel.Small)));
        // Progress<T>は非同期コールバックなので少し待つ
        await Task.Delay(100);
        Assert.NotEmpty(progress);
        Assert.Contains(progress, p => p >= 1.0);
    }

    [Fact]
    public async Task Download_HashMismatch_ThrowsAndLeavesNothing()
    {
        var payload = Encoding.UTF8.GetBytes("model-data");
        var d = Create(_dir, payload, pointerSha: new string('0', 64));
        await Assert.ThrowsAsync<InvalidDataException>(
            () => d.DownloadAsync(WhisperModel.Small, null, CancellationToken.None));
        Assert.False(d.IsDownloaded(WhisperModel.Small));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}
