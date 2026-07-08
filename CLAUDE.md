# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```powershell
dotnet build                                   # build everything (WinExe + Core + Tests)
dotnet test                                    # run all xUnit tests
dotnet test --filter "FullyQualifiedName~ContentHasher"   # run one test class
dotnet run --project src/YomiagePlayer         # run the app
src\YomiagePlayer\bin\Debug\net10.0-windows\YomiagePlayer.exe "C:\path\to\file.mp3"  # run built exe, optionally with a file to open immediately
```

Before rebuilding, stop a running instance first (`Stop-Process -Name YomiagePlayer -Force`) — the WPF exe locks `YomiagePlayer.Core.dll` and the build fails with MSB3027/MSB3021 otherwise.

ffmpeg is not bundled in git. `tools/ffmpeg/ffmpeg.exe` and `ffprobe.exe` (LGPL build from BtbN/FFmpeg-Builds, see `tools/ffmpeg/README.md`) must be present locally for audio extraction to work; `FfmpegConfig.ConfigureFromRepoTools` walks up from the exe directory looking for `tools/ffmpeg`.

Test fixtures live in `tests/fixtures/` (`tone-1s.wav`/`tone-1s.mp4` generated via ffmpeg sine source, `speech-ja.wav` generated via `System.Speech` SAPI TTS for real Japanese-recognition testing).

## Architecture

Three projects: `src/YomiagePlayer.Core` (platform-agnostic logic, no WPF/LibVLC references), `src/YomiagePlayer` (WPF app: Views/ViewModels/Services), `tests/YomiagePlayer.Tests`.

### End-to-end flow

`MainWindow.FilesOpened` → files go into the playlist → `PlaylistViewModel.PlayRequested` → `MainWindow.PlayFile` starts LibVLC playback immediately and raises `MediaChanged` → `TranscriptionCoordinator.OnMediaChangedAsync` takes over:

1. Compute a fast content hash (`ContentHasher.ComputeKey`: SHA256 of `fileSize + first 1MB + last 1MB` — not a full-file hash, so it stays fast on large video files and survives renames/moves).
2. `TranscriptionCache.TryLoad(hash, model)` — cache file is `{hash}-{model}.json` under `%AppData%\YomiagePlayer\cache\`. Cache hit → segments load into the lyrics pane immediately, no re-analysis.
3. Cache miss → job goes into `TranscriptionQueue` (always 1 concurrent job, LIFO priority — a newly-opened track jumps ahead of anything still queued, but a job already *running* is left to finish and gets cached even if the user has since switched tracks). The job: `AudioExtractor` (ffmpeg → 16kHz mono WAV) → `WhisperTranscriber.TranscribeAsync` (Whisper.net, streams `TranscriptSegment`s via `IAsyncEnumerable`) → each segment passes `HallucinationFilter` before being pushed to `LyricsViewModel.AddSegment` (streaming display, not wait-for-completion) → on completion, `TranscriptionCache.Save` writes atomically (`.tmp` → `File.Move` with overwrite).
4. UI updates are gated on `_currentKey == key` inside `TranscriptionCoordinator` — this is how a background/superseded job's completion avoids clobbering whatever track the user is currently looking at.

`IdleAnalysisService` runs on a timer and, whenever `TranscriptionQueue.IsIdle`, walks the registered library folders pre-analyzing one not-yet-cached file per tick via `TranscriptionCoordinator.EnsureAnalyzedAsync` (cache-only side effect, never touches the lyrics VM). Any user-initiated `OnMediaChangedAsync` still jumps the queue ahead of these background jobs. A per-session `_visited` set avoids rescanning; `LibraryViewModel.FoldersChanged` resets it.

### Playback ↔ lyrics sync

`PlaybackService` wraps a single `LibVLC`/`MediaPlayer` and re-raises its events (`PositionChanged`, `LengthKnown`, `MediaEnded`, `PlayingChanged`) — these fire from VLC's own thread, so every subscriber (`MainWindow`, `PlaybackViewModel`) must marshal to the UI thread itself (`Dispatcher.BeginInvoke`); `PlaybackService` does not do this for you.

libVLC ignores `Time` writes while `Stopped`/`Ended`. `PlaybackService.SeekTo` handles this by stashing the target as `_pendingSeek` and restarting playback off the threadpool (`Player.Stop()` + `Player.Play()` inside a VLC callback would deadlock, hence `ThreadPool.QueueUserWorkItem`); the pending seek is applied when the `Playing` event fires. This is why clicking a lyric line works even when the player was stopped.

`LyricsViewModel.UpdatePosition` looks up the active segment via `SegmentLocator.FindIndex` (binary search over `Start`-sorted segments, half-open `[Start, End)` intervals — gaps between segments correctly return `-1`/no highlight rather than sticking on the previous line). `IsPositionAheadOfAnalysis` flags when playback has moved past everything transcribed so far (still-`Analyzing` state only) so the UI can show an "analyzing this part" banner instead of silently showing nothing.

### Hallucination mitigation

Whisper reliably hallucinates in silence/non-speech-heavy audio (this app's primary ASMR use case), most visibly by emitting bracketed annotations. `HallucinationFilter.ShouldDrop` strips `(...)`/`（...）`/`[...]`/`【...】` spans and drops the segment if nothing else remains (so a bare `(笑)` segment is dropped, but `そうなんだ(笑)` is kept), plus drops short (<2s) exact repeats of the previous segment (repeat-loop detection) while leaving longer repeats alone (could be a legitimate chorus). Whisper.net 1.9.1 has no VAD builder API, so the mitigation is text-based post-filtering plus `WithNoContext()`/`WithNoSpeechThreshold`/`WithEntropyThreshold` on the `WhisperProcessorBuilder` — there is no actual VAD pre-pass.

### DI wiring

`App.xaml.cs` builds a flat `ServiceCollection` — nearly everything is a singleton, including `TranscriptionCoordinator` and `TranscriptionQueue`, so there is exactly one queue/cache/coordinator for the process lifetime. `TranscriptionCoordinator` takes its UI-thread marshaling callback (`Action<Action> uiInvoke`) as a constructor parameter specifically so it can be unit-tested with a synchronous inline invoker (see `TranscriptionCoordinatorTests`) instead of requiring a real Dispatcher.

`ITranscriberFactory`/`IAudioExtractorService` exist purely so `TranscriptionCoordinator` and `IdleAnalysisService` can be tested with fakes — production only ever wires `WhisperTranscriberFactory`/`AudioExtractor`.

### Namespace collision gotcha

`YomiagePlayer.Core` (this project's own root namespace) collides with `LibVLCSharp.Shared.Core` (the static class with `Core.Initialize()`). Always fully qualify as `LibVLCSharp.Shared.Core.Initialize()`.

### Atomic writes

Both `TranscriptionCache.Save` and `SettingsStore.Save` write to `{path}.tmp` then `File.Move(tmp, final, overwrite: true)` — this is deliberate (crash-safety for long-running transcriptions and cheap settings writes alike); don't replace with a direct `File.WriteAllText`.

### Airspace (LibVLCSharp.WPF)

`VideoView.Content` in `MainWindow.xaml` renders as a separate floating HWND overlaid on the video, not a real WPF child — confirmed via `PrintWindow` capture during the Task-11 spike (see `docs/plans/2026-07-08-spike-notes.md`). Drag&drop and overlay controls must be attached to *both* the outer `Window` and the `VideoView.Content` root, not just one.

### PowerShell + Japanese text

Don't use PowerShell (`Get-Content`/`Set-Content`) to bulk-edit source files containing Japanese text — PS 5.1 misreads BOM-less UTF-8 as ANSI and corrupts it silently. Use the Edit/Write tools instead. (This happened once and required restoring 4 test files from git.)

The same corruption hits `.ps1` scripts themselves: Windows PowerShell 5.1 parses a BOM-less UTF-8 script using the system codepage, and Japanese text can corrupt into byte sequences that break tokenization (not just cosmetic mojibake — `scripts/publish.ps1` failed to parse at all until Japanese comments/messages were replaced with ASCII). Keep `.ps1` files ASCII-only unless you add a UTF-8 BOM.

### Distributable build

`scripts/publish.ps1` runs `dotnet publish --self-contained` for a single RID (default win-x64) into `dist/<rid>/`, then deletes the native-file trees for every *other* architecture. This pruning is necessary because LibVLCSharp copies `libvlc/{win-x64,win-x86,win-arm64}/` unconditionally and Whisper.net.Runtime.* copies `runtimes/{<rid>,cuda/<rid>,vulkan/<rid>}/` for win/linux/macos unconditionally — a RID-specific publish does not filter these on its own (verified: an unpruned publish is ~870MB). The script also copies `tools/ffmpeg/*.exe` into `dist/<rid>/ffmpeg/` (matching `FfmpegConfig.Configure`'s exe-adjacent lookup in `App.xaml.cs`) and `docs/licenses/THIRD-PARTY-NOTICES.md` into the output root (matching `SettingsViewModel.LoadLicensesText`'s exe-adjacent lookup). The Whisper model itself is never bundled — it's downloaded post-install via the Settings window.

## Design docs

`docs/plans/` has the full design doc, implementation plan (task-by-task with exact test code), a Fable-model architecture review and its resolution, spike notes, and integration-checklist results — read these before making structural changes, they contain the reasoning behind decisions summarized above.
