using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using YomiagePlayer.Core.Library;
using YomiagePlayer.Core.Transcription;
using YomiagePlayer.ViewModels;

namespace YomiagePlayer.Tests;

public class SettingsViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(responder(request));
    }

    private ModelDownloader CreateDownloader(byte[]? payload = null, string? pointerSha = null)
    {
        payload ??= [1, 2, 3];
        var sha = pointerSha ?? Convert.ToHexStringLower(SHA256.HashData(payload));
        var handler = new StubHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("/raw/"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $"version https://git-lfs.github.com/spec/v1\noid sha256:{sha}\nsize {payload.Length}\n")
                };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };
        });
        return new ModelDownloader(new HttpClient(handler), _dir);
    }

    [Fact]
    public async Task DownloadSelectedAsync_Success_RaisesModelDownloaded()
    {
        var vm = new SettingsViewModel(new SettingsStore(Path.Combine(_dir, "settings.json")), CreateDownloader());
        var raised = 0;
        vm.ModelDownloaded += () => raised++;

        await vm.DownloadSelectedCommand.ExecuteAsync(null);

        Assert.Equal(1, raised);
        Assert.True(vm.IsSelectedModelDownloaded);
    }

    [Fact]
    public async Task DownloadSelectedAsync_Failure_DoesNotRaiseModelDownloaded()
    {
        // ポインタのSHAと実体が一致しない -> ハッシュ検証で失敗させる
        var vm = new SettingsViewModel(
            new SettingsStore(Path.Combine(_dir, "settings.json")),
            CreateDownloader(pointerSha: new string('0', 64)));
        var raised = 0;
        vm.ModelDownloaded += () => raised++;

        await vm.DownloadSelectedCommand.ExecuteAsync(null);

        Assert.Equal(0, raised);
        Assert.NotEqual("", vm.DownloadError);
    }

    [Fact]
    public void OnSelectedModelChanged_RaisesModelChanged()
    {
        var vm = new SettingsViewModel(new SettingsStore(Path.Combine(_dir, "settings.json")), CreateDownloader());
        WhisperModel? changedTo = null;
        vm.ModelChanged += m => changedTo = m;

        vm.SelectedModel = vm.Models.First(o => o.Model != vm.SelectedModel!.Model);

        Assert.Equal(vm.SelectedModel.Model, changedTo);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}
