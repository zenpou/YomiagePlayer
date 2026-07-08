using YomiagePlayer.Core.Models;

namespace YomiagePlayer.Core.Transcription;

public interface IAudioExtractorService
{
    Task<string> ExtractWavAsync(string mediaPath, CancellationToken ct);
}

public interface ITranscriber : IDisposable
{
    IAsyncEnumerable<TranscriptSegment> TranscribeAsync(string wavPath, CancellationToken ct = default);
}

public interface ITranscriberFactory
{
    ITranscriber Create(WhisperModel model);
}
