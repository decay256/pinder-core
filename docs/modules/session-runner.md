# Session Runner

## Overview
The session runner is a standalone C# program (`session-runner/Program.cs`) that orchestrates automated Pinder playtest sessions. It runs games between characters and writes playtest log files to disk with sequentially numbered filenames.

## Key Components

| File / Class | Description |
|---|---|
| `session-runner/Program.cs` | Entry point. Contains `WritePlaytestLog` which writes session markdown files, delegating numbering to `SessionFileCounter`. Loads trap definitions via `JsonTrapRepository` with multi-path fallback. |
| `session-runner/SessionFileCounter.cs` | `internal static` class that scans a directory for `session-*.md` files and returns the next available session number. |
| `tests/Pinder.Core.Tests/SessionFileCounterTests.cs` | Tests for `SessionFileCounter.GetNextSessionNumber` — validates glob pattern matching and number extraction from session filenames. |
| `tests/Pinder.Core.Tests/SessionRunnerTrapLoadingTests.cs` | Tests that `JsonTrapRepository` returns real trap definitions (unlike `NullTrapRegistry`), covers all six stats, trap activation, and graceful fallback. |

## API / Public Interface

### WritePlaytestLog (private static)

```csharp
static void WritePlaytestLog(string content, string p1, string p2, GameOutcome? outcome)
```

Writes a playtest log to `/root/.openclaw/agents-extra/pinder/design/playtests/` as a markdown file. The filename follows the pattern `session-{NNN}-{p1}-vs-{p2}.md` where `NNN` is zero-padded to 3 digits and auto-incremented based on existing files.

Delegates session numbering to `SessionFileCounter.GetNextSessionNumber`.

### SessionFileCounter.GetNextSessionNumber

```csharp
internal static class SessionFileCounter
{
    public static int GetNextSessionNumber(string directory)
}
```

Scans the given directory for `session-*.md` files. Splits each filename on `-` and parses the second segment as the session number. Returns `max(existing) + 1`, defaulting to `1` if no matching files exist. Exposed to `Pinder.Core.Tests` via `InternalsVisibleTo`.

## Architecture Notes

- Session files are named with character slugs (e.g., `session-005-sable-vs-brick.md`), which means filenames contain variable numbers of hyphens.
- The glob pattern `session-*.md` (not `session-???.md`) ensures filenames with suffixes beyond the 3-digit number are matched correctly.
- Number extraction uses `Split('-')[1]` to isolate the numeric segment, which is robust against hyphenated character names in the suffix.
- The file counter logic is extracted into `SessionFileCounter` (internal static class) for testability. `session-runner.csproj` uses `InternalsVisibleTo` to expose it to `Pinder.Core.Tests`.
- **Trap loading** uses `JsonTrapRepository` (from `Pinder.Core.Data`) instead of `NullTrapRegistry`. The runner searches three paths in order: a relative path from `AppContext.BaseDirectory`, the repo workspace path, and an absolute playtesting-environment path. If no `traps.json` is found or parsing fails, it falls back to `NullTrapRegistry` with a warning on stderr.

## Change Log
| Date | Issue | Summary |
|------|-------|---------|
| 2026-04-03 | #354 | Initial creation — Fixed file counter bug where `session-???.md` glob missed files with character name suffixes (e.g., `session-005-sable-vs-brick.md`). Changed to `session-*.md` glob with `Split('-')[1]` number extraction. Added `SessionFileCounterTests`. |
| 2026-04-03 | #353 | Replaced hardcoded `NullTrapRegistry` with `JsonTrapRepository` loaded from `data/traps/traps.json`. Added multi-path resolution (relative, repo, playtesting) with graceful fallback. Added `SessionRunnerTrapLoadingTests`. |
| 2026-04-03 | #354 | Extracted inline file counter logic from `WritePlaytestLog` into `SessionFileCounter.GetNextSessionNumber` (new file `SessionFileCounter.cs`). Tests now call the real class instead of a mirrored helper. Added `InternalsVisibleTo` for test access. |
