using System.IO;
using YomiagePlayer.Core.Library;

namespace YomiagePlayer.Tests;

public class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private string SettingsFile => Path.Combine(_dir, "settings.json");

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var s = new SettingsStore(SettingsFile).Load();
        Assert.Equal("medium", s.Model);
        Assert.Equal(80, s.Volume);
        Assert.Empty(s.RegisteredFolders);
    }

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        var store = new SettingsStore(SettingsFile);
        store.Save(new AppSettings
        {
            RegisteredFolders = [@"C:\音楽", @"D:\ASMR"],
            Model = "large-v3-turbo",
            Volume = 55,
        });
        var loaded = store.Load();
        Assert.Equal([@"C:\音楽", @"D:\ASMR"], loaded.RegisteredFolders);
        Assert.Equal("large-v3-turbo", loaded.Model);
        Assert.Equal(55, loaded.Volume);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsFile, "{ bad json");
        Assert.Equal("medium", new SettingsStore(SettingsFile).Load().Model);
    }

    [Fact]
    public void Save_LeavesNoTmp()
    {
        new SettingsStore(SettingsFile).Save(new AppSettings());
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}
