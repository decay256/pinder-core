# Spec: Fix SessionFileCounter to correctly read session numbers

**Issue:** #418  
**Module:** `docs/modules/session-runner.md`

---

## Overview

`SessionFileCounter.GetNextSessionNumber()` is returning `1` even when existing playtest session files are present in the target directory, causing new sessions to overwrite `session-001-*.md`. The root cause is a path resolution mismatch: the counter scans a directory that does not match where files were actually written, or the counter is invoked with a path that resolves differently at runtime than expected. This spec defines the fix to ensure the counter reliably finds existing session files and returns the correct next number.

---

## Background

The session runner writes playtest markdown files to an external directory (`/root/.openclaw/agents-extra/pinder/design/playtests/`). Files follow the naming convention `session-NNN-<player>-vs-<opponent>.md` where `NNN` is a zero-padded integer (e.g., `session-005-sable-vs-brick.md`).

`SessionFileCounter.GetNextSessionNumber(string directory)` scans that directory for `session-*.md` files, extracts the numeric portion from the filename, and returns `max(existing) + 1`.

The parsing logic itself (split on `-`, parse `parts[1]`) is correct. The bug is in how the `directory` parameter is resolved or passed — the counter receives a path where no matching files are found, so it returns the default of `1`.

---

## Function Signatures

```csharp
// File: session-runner/SessionFileCounter.cs
// Namespace: (internal, no namespace — top-level in session-runner)

internal static class SessionFileCounter
{
    /// <summary>
    /// Scans the given directory for session-*.md files and returns
    /// the next available session number (highest existing + 1, or 1 if none).
    /// </summary>
    /// <param name="directory">
    ///   Absolute path to the directory containing session files.
    ///   Must exist and be readable.
    /// </param>
    /// <returns>
    ///   int — the next session number (>= 1).
    ///   Returns 1 if the directory contains no session-*.md files.
    /// </returns>
    public static int GetNextSessionNumber(string directory);
}
```

No signature change is required. The fix is behavioral (correct path resolution so the method receives a directory path that actually contains the files it writes to).

---

## Input/Output Examples

### Example 1: Directory with sequential files

**Directory contents:**
```
session-001-gerald-vs-zyx.md
session-002-brick-vs-velvet.md
session-003-sable-vs-zyx.md
```

**Input:** `GetNextSessionNumber("/path/to/playtests")`  
**Output:** `4`

### Example 2: Directory with gaps

**Directory contents:**
```
session-001-sable-vs-brick.md
session-005-sable-vs-brick.md
```

**Input:** `GetNextSessionNumber("/path/to/playtests")`  
**Output:** `6` (returns max + 1, does not fill gaps)

### Example 3: Character name containing digits

**Directory contents:**
```
session-008-gerald42-vs-zyx.md
```

**Input:** `GetNextSessionNumber("/path/to/playtests")`  
**Output:** `9`

### Example 4: Empty directory

**Directory contents:** (none)  
**Input:** `GetNextSessionNumber("/path/to/playtests")`  
**Output:** `1`

### Example 5: Only non-session files

**Directory contents:**
```
notes.md
readme.txt
```

**Input:** `GetNextSessionNumber("/path/to/playtests")`  
**Output:** `1`

---

## Acceptance Criteria

### AC1: Counter returns correct next number when session files exist

Given a directory containing `session-001-gerald-vs-zyx.md` through `session-008-gerald42-vs-zyx.md`, calling `GetNextSessionNumber` on that directory must return `9`.

**Verification:** Create a temp directory, write files matching the production naming convention (including names with digits like `gerald42`), assert the returned number is `max + 1`.

### AC2: Path resolution matches between write and read

The directory path passed to `GetNextSessionNumber` in `Program.WritePlaytestLog` must resolve to the same filesystem location where `File.WriteAllText` writes the session file. If the path uses relative segments, trailing slashes, or symlinks, the counter must still find the files.

**Verification:** After running the session runner and writing a file, a subsequent call to `GetNextSessionNumber` with the same directory string must return a number higher than the file just written.

### AC3: Files with character names containing digits or hyphens parse correctly

Filenames like `session-008-gerald42-vs-zyx.md` or `session-010-mary-jane-vs-peter-parker.md` must not confuse the number extraction. The session number is always `parts[1]` after splitting on `-` (i.e., the segment immediately after `session`).

**Verification:** Unit test with filenames containing digits in character names and hyphenated multi-word names.

### AC4: Existing tests continue to pass

All existing `SessionFileCounterTests` (5 tests) must pass without modification. The fix must be backward-compatible.

---

## Edge Cases

| Case | Expected Behavior |
|------|-------------------|
| Empty directory | Returns `1` |
| Directory with only non-`.md` files | Returns `1` |
| Directory with `.md` files not matching `session-*` pattern | Returns `1` |
| Single file `session-999-a-vs-b.md` | Returns `1000` |
| File named `session-0-a-vs-b.md` (no zero-padding) | Returns `1` (since `0 + 1 = 1`) |
| File named `session-abc-a-vs-b.md` (non-numeric) | Skipped by `int.TryParse`, returns `1` |
| Trailing slash on directory path | Must work (OS normalizes) |
| Directory path with `..` segments | Must work (`Directory.GetFiles` resolves) |
| Very large number `session-99999-a-vs-b.md` | Returns `100000` (`int.TryParse` handles this) |
| Concurrent writes (two runners) | Not addressed — prototype maturity, single-user tool |

---

## Error Conditions

| Condition | Expected Behavior |
|-----------|-------------------|
| `directory` does not exist | `Directory.GetFiles` throws `DirectoryNotFoundException`. Caller (`WritePlaytestLog`) already checks `Directory.Exists(dir)` before calling the counter, so this should not occur in production. |
| `directory` is null | `ArgumentNullException` from `Directory.GetFiles`. No special handling needed. |
| `directory` is not readable (permissions) | `UnauthorizedAccessException` from `Directory.GetFiles`. No special handling at prototype maturity. |

---

## Root Cause Analysis

The counter's parsing logic (`Split('-')[1]` + `int.TryParse`) is correct as verified by existing unit tests. The likely root cause is one of:

1. **Path mismatch**: The directory string passed to `GetNextSessionNumber` resolves to a different filesystem location than where `File.WriteAllText` places files. This could happen if the path is constructed differently (e.g., relative vs absolute, or a working-directory assumption).

2. **Build output directory confusion**: If the session runner is invoked via `dotnet run`, the working directory may differ from the build output directory. A hardcoded relative path could resolve differently.

3. **Directory creation timing**: If `WritePlaytestLog` creates the directory on first run but `GetNextSessionNumber` is called before any files are written in a fresh environment, it correctly returns `1`. But if a *previous* run wrote files to a different resolved path, the counter wouldn't find them.

The implementer should:
- Verify that `WritePlaytestLog` uses the exact same `dir` string for both `GetNextSessionNumber(dir)` and `Path.Combine(dir, slug)` — this is currently the case in the code.
- Add a test that mimics the production flow: create a file via `File.WriteAllText(Path.Combine(dir, slug))`, then call `GetNextSessionNumber(dir)` and assert it returns the next number.
- If the bug is in path resolution outside of `SessionFileCounter` (e.g., in how `Program.cs` constructs the directory path), fix it there and document the change.

---

## Dependencies

| Dependency | Type | Notes |
|------------|------|-------|
| `System.IO.Directory.GetFiles` | .NET BCL | Used for glob matching `session-*.md` |
| `System.IO.Path` | .NET BCL | Used for `GetFileNameWithoutExtension` |
| `session-runner/Program.cs` | Internal | Caller that passes directory path to the counter |
| Playtest output directory | External filesystem | `/root/.openclaw/agents-extra/pinder/design/playtests/` |

No external libraries or NuGet packages. No Pinder.Core dependencies.
