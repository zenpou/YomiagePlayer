using System.IO;
using System.Net.Http;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using YomiagePlayer.Core;
using YomiagePlayer.Core.Cache;
using YomiagePlayer.Core.Library;
using YomiagePlayer.Core.Transcription;
using YomiagePlayer.Services;
using YomiagePlayer.ViewModels;

namespace YomiagePlayer;

public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Directory.CreateDirectory(AppPaths.Logs);
        Log.Logger = new LoggerConfiguration()
            .WriteTo.File(
                Path.Combine(AppPaths.Logs, "app-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14)
            .CreateLogger();
        Log.Information("YomiagePlayer 起動");

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "未処理例外");
            MessageBox.Show(args.Exception.Message, "エラー",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        // ffmpeg: 配布時はexe隣接、開発時はリポジトリのtools/ffmpegを探す
        if (!FfmpegConfig.Configure(Path.Combine(AppContext.BaseDirectory, "ffmpeg")))
            FfmpegConfig.ConfigureFromRepoTools(AppContext.BaseDirectory);

        WhisperTranscriber.ConfigureRuntimeOrder();

        var services = new ServiceCollection();
        ConfigureServices(services);
        _services = services.BuildServiceProvider();

        _services.GetRequiredService<AudioExtractor>().CleanupTemp();

        var window = _services.GetRequiredService<MainWindow>();
        var coordinator = _services.GetRequiredService<TranscriptionCoordinator>();
        window.MediaChanged += path => _ = coordinator.OnMediaChangedAsync(path);
        window.ReanalyzeRequested += path => _ = coordinator.ReanalyzeAsync(path);
        window.SettingsRequested += () =>
        {
            var settings = new UI.SettingsWindow(
                _services.GetRequiredService<SettingsViewModel>())
            { Owner = window };
            settings.ShowDialog();
        };
        window.Show();
    }

    private static void ConfigureServices(ServiceCollection services)
    {
        services.AddSingleton<PlaybackService>();
        services.AddSingleton(new TranscriptionCache(AppPaths.Cache));
        services.AddSingleton<TranscriptionQueue>();
        services.AddSingleton<AudioExtractor>();
        services.AddSingleton<HallucinationFilter>();
        services.AddSingleton(sp => new ModelDownloader(new HttpClient()));
        services.AddSingleton<SettingsStore>();
        services.AddSingleton<IAudioExtractorService>(sp => sp.GetRequiredService<AudioExtractor>());
        services.AddSingleton<ITranscriberFactory, WhisperTranscriberFactory>();
        services.AddSingleton(sp => new TranscriptionCoordinator(
            sp.GetRequiredService<TranscriptionCache>(),
            sp.GetRequiredService<TranscriptionQueue>(),
            sp.GetRequiredService<IAudioExtractorService>(),
            sp.GetRequiredService<ITranscriberFactory>(),
            sp.GetRequiredService<ModelDownloader>(),
            sp.GetRequiredService<SettingsStore>(),
            sp.GetRequiredService<LyricsViewModel>(),
            action => Current.Dispatcher.BeginInvoke(action)));

        services.AddSingleton<PlaybackViewModel>();
        services.AddSingleton<LyricsViewModel>();
        services.AddSingleton<PlaylistViewModel>();
        services.AddSingleton<LibraryViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("YomiagePlayer 終了");
        // graceful shutdown: 進行中の解析をキャンセルし、一時WAVを掃除
        try
        {
            _services?.GetService<TranscriptionCoordinator>()?
                .ShutdownAsync().Wait(TimeSpan.FromSeconds(5));
        }
        catch { }
        _services?.GetService<AudioExtractor>()?.CleanupTemp();
        _services?.GetService<PlaybackService>()?.Dispose();
        _services?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
