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
