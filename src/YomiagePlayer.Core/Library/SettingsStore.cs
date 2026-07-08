using System.Text.Json;

namespace YomiagePlayer.Core.Library;

public record AppSettings
{
    public List<string> RegisteredFolders { get; init; } = [];
    public string Model { get; init; } = "medium";
    public int Volume { get; init; } = 80;
    public double WindowWidth { get; init; } = 1200;
    public double WindowHeight { get; init; } = 720;
}

/// <summary>
/// アプリ設定のJSON永続化。書き込みは .tmp → rename の原子的置換。
/// 読み込み失敗(不存在/破損)時はデフォルト値を返す。
/// </summary>
public class SettingsStore(string settingsFile)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public SettingsStore() : this(AppPaths.SettingsFile) { }

    public AppSettings Load()
    {
        if (!File.Exists(settingsFile)) return new AppSettings();
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(
                File.ReadAllText(settingsFile), JsonOpts) ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(settingsFile)!);
        var tmp = settingsFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOpts));
        File.Move(tmp, settingsFile, overwrite: true);
    }
}
