using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YomiagePlayer.Core.Library;
using YomiagePlayer.Core.Transcription;

namespace YomiagePlayer.ViewModels;

public partial class ModelOption(WhisperModel model, ModelDownloader downloader) : ObservableObject
{
    public WhisperModel Model { get; } = model;
    public string DisplayName { get; } = model.DisplayName();

    [ObservableProperty]
    private bool _isDownloaded = downloader.IsDownloaded(model);

    public void Refresh(ModelDownloader downloader) => IsDownloaded = downloader.IsDownloaded(Model);
}

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsStore _store;
    private readonly ModelDownloader _downloader;

    public List<ModelOption> Models { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSelectedModelDownloaded))]
    private ModelOption? _selectedModel;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private string _downloadError = "";

    public bool IsSelectedModelDownloaded => SelectedModel?.IsDownloaded ?? false;

    public string LoadedRuntime =>
        WhisperTranscriber.LoadedRuntime ?? "未ロード(初回解析時に決定)";

    public string LicensesText { get; }

    /// <summary>モデル設定が変更された(次の解析から反映)。</summary>
    public event Action<WhisperModel>? ModelChanged;

    public SettingsViewModel(SettingsStore store, ModelDownloader downloader)
    {
        _store = store;
        _downloader = downloader;
        Models = Enum.GetValues<WhisperModel>()
            .Select(m => new ModelOption(m, downloader)).ToList();

        var settings = store.Load();
        WhisperModelInfo.TryParse(settings.Model, out var current);
        SelectedModel = Models.FirstOrDefault(o => o.Model == current) ?? Models[1];

        LicensesText = LoadLicensesText();
    }

    partial void OnSelectedModelChanged(ModelOption? value)
    {
        if (value is null) return;
        var settings = _store.Load();
        if (settings.Model != value.Model.Id())
        {
            _store.Save(settings with { Model = value.Model.Id() });
            ModelChanged?.Invoke(value.Model);
        }
    }

    [RelayCommand]
    private async Task DownloadSelectedAsync()
    {
        if (SelectedModel is null || IsDownloading) return;
        IsDownloading = true;
        DownloadError = "";
        DownloadProgress = 0;
        try
        {
            await _downloader.DownloadAsync(
                SelectedModel.Model,
                new Progress<double>(p => DownloadProgress = p * 100),
                CancellationToken.None);
            SelectedModel.Refresh(_downloader);
            OnPropertyChanged(nameof(IsSelectedModelDownloaded));
        }
        catch (Exception ex)
        {
            DownloadError = $"ダウンロードに失敗しました: {ex.Message}";
        }
        finally
        {
            IsDownloading = false;
        }
    }

    private static string LoadLicensesText()
    {
        // 配布時はexe隣接、開発時はリポジトリのdocs/licensesを探す
        var local = Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.md");
        if (File.Exists(local)) return File.ReadAllText(local);
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent!)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "licenses", "THIRD-PARTY-NOTICES.md");
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }
        return "(ライセンスファイルが見つかりません)";
    }
}
