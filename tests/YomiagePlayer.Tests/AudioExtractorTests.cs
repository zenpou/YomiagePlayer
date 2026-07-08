using YomiagePlayer.Core.Transcription;

namespace YomiagePlayer.Tests;

public class AudioExtractorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public AudioExtractorTests()
        => FfmpegConfig.ConfigureFromRepoTools(AppContext.BaseDirectory);

    [Fact]
    public void CleanupTemp_RemovesWavFiles()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "orphan.wav"), "x");
        File.WriteAllText(Path.Combine(_dir, "keep.txt"), "x");
        new AudioExtractor(_dir).CleanupTemp();
        Assert.Empty(Directory.GetFiles(_dir, "*.wav"));
        Assert.Single(Directory.GetFiles(_dir, "*.txt"));
    }

    [Fact]
    public void CleanupTemp_MissingDir_DoesNotThrow()
        => new AudioExtractor(Path.Combine(_dir, "nope")).CleanupTemp();

    [Fact]
    public async Task ExtractWav_BrokenFile_ThrowsAudioExtractionException()
    {
        Directory.CreateDirectory(_dir);
        var broken = Path.Combine(_dir, "broken.mp3");
        File.WriteAllBytes(broken, [0x00, 0x01, 0x02]);
        await Assert.ThrowsAsync<AudioExtractionException>(
            () => new AudioExtractor(_dir).ExtractWavAsync(broken, CancellationToken.None));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExtractWav_FromMp4_Produces16kMonoWav()
    {
        var mp4 = FindFixture("tone-1s.mp4");
        var wav = await new AudioExtractor(_dir).ExtractWavAsync(mp4, CancellationToken.None);
        Assert.True(File.Exists(wav));
        // WAVヘッダのサンプルレート(offset 24, LE)とチャンネル数(offset 22)を検証
        var header = new byte[44];
        using (var fs = File.OpenRead(wav)) fs.ReadExactly(header);
        Assert.Equal(1, BitConverter.ToInt16(header, 22));
        Assert.Equal(16000, BitConverter.ToInt32(header, 24));
    }

    private static string FindFixture(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent!)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "fixtures", name);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException(name);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}
