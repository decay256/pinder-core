# Spec: Issue #350 — Session Runner: Enable Shadow Tracking via GameSessionConfig

**Module**: `docs/modules/session-runner.md`

---

## Overview

The session runner currently creates `GameSession` without a `GameSessionConfig`, which means the internal `_playerShadows` field is null and all shadow growth triggers silently do nothing during simulated playtests. This issue wires a `SessionShadowTracker` (wrapping the player's `StatBlock`) into `GameSession` via `GameSessionConfig`, and adds shadow-related output to the playtest markdown — per-turn growth event lines and a session-end shadow delta summary table.

---

## Function Signatures

No new public types or methods are introduced by this issue. All changes are wiring and output formatting within `session-runner/Program.cs`.

### Existing APIs consumed (for reference)

```csharp
// Pinder.Core.Stats.SessionShadowTracker
public SessionShadowTracker(StatBlock baseStats);
public int GetEffectiveShadow(ShadowStatType shadow);
public int GetDelta(ShadowStatType shadow);

// Pinder.Core.Stats.StatBlock
public int GetShadow(ShadowStatType shadow);

// Pinder.Core.Conversation.GameSessionConfig
public GameSessionConfig(
    IGameClock? clock = null,
    SessionShadowTracker? playerShadows = null,
    SessionShadowTracker? opponentShadows = null,
    int? startingInterest = null,
    string? previousOpener = null);

// Pinder.Core.Conversation.GameSession (existing constructor with config)
public GameSession(
    CharacterProfile player,
    CharacterProfile opponent,
    ILlmAdapter llm,
    IDiceRoller dice,
    ITrapRegistry traps,
    GameSessionConfig? config);

// Pinder.Core.Conversation.TurnResult
public IReadOnlyList<string> ShadowGrowthEvents { get; }

// Pinder.Core.Stats.ShadowStatType (enum)
// Values: Madness, Horniness, Denial, Fixation, Dread, Overthinking
```

---

## Input/Output Examples

### Session setup (before the game loop)

**Before (current code):**
```csharp
var session = new GameSession(sable, brick, llm, new SystemRandomDiceRoller(), trapRegistry);
```

**After:**
```csharp
var sableShadows = new SessionShadowTracker(sableStats);
var config = new GameSessionConfig(playerShadows: sableShadows);
var session = new GameSession(sable, brick, llm, new SystemRandomDiceRoller(), trapRegistry, config);
```

Note: `SessionShadowTracker` takes a `StatBlock`, **not** a `Dictionary<ShadowStatType, int>`. The issue body's code example is incorrect per #360 — the `StatBlock` already contains the starting shadow values (e.g., Denial=3, Fixation=2 for Sable).

### Per-turn output (when shadow events fire)

When `TurnResult.ShadowGrowthEvents` is non-empty, each event is printed as a separate line in the turn's post-roll status block. Example:

```
Interest: ████████████░░░░░░░░  15/25  (+2)
⚠️ SHADOW GROWTH: Fixation +1 (same stat 3 turns in a row) → Fixation now 3
Active Traps: none  |  Momentum: 3 wins
```

Each line follows the format:
```
⚠️ SHADOW GROWTH: {event description from ShadowGrowthEvents}
```

The `event description` string comes directly from `TurnResult.ShadowGrowthEvents[i]`. These strings are produced by `SessionShadowTracker.ApplyGrowth()` and have the format `"{ShadowStatName} +{amount} ({reason})"` — e.g., `"Fixation +1 (same stat 3 turns in a row)"`.

The "→ {Shadow} now {value}" suffix is **appended by the session runner** using `sableShadows.GetEffectiveShadow(type)` after the event fires. The implementer must parse or match the shadow stat name from the event string to look up the current value, **or** simply append the full effective shadow table after all events. The simplest correct approach: iterate all `ShadowStatType` values and print each event string as-is (the event string already contains the shadow name and delta).

### Session summary output (after the game loop)

Appended to the session summary section, after the outcome line:

```markdown
## Shadow Changes This Session
| Shadow | Start | End | Delta |
|---|---|---|---|
| Madness | 0 | 0 | 0 |
| Horniness | 0 | 0 | 0 |
| Denial | 3 | 4 | +1 |
| Fixation | 2 | 2 | 0 |
| Dread | 0 | 0 | 0 |
| Overthinking | 0 | 0 | 0 |
```

**Column definitions:**
- **Shadow**: `ShadowStatType` enum name
- **Start**: `sableStats.GetShadow(type)` — the base value from the original `StatBlock`
- **End**: `sableShadows.GetEffectiveShadow(type)` — base + session delta
- **Delta**: `sableShadows.GetDelta(type)` — formatted as `+N` for positive, `-N` for negative, `0` for zero

All six `ShadowStatType` values must be included, even if delta is 0.

---

## Acceptance Criteria

### AC1: Session runner passes `GameSessionConfig` with `PlayerShadows` from character's starting shadow values

- A `SessionShadowTracker` is created wrapping `sableStats` (the player's `StatBlock`)
- A `GameSessionConfig` is created with `playerShadows: sableShadows`
- The `GameSession` constructor receives this config as its 6th argument
- The session runner retains a reference to `sableShadows` for reading shadow state at session end

### AC2: Shadow growth events appear in turn output when they fire

- After each `ResolveTurnAsync()` call, if `result.ShadowGrowthEvents` has any entries (count > 0), each entry is printed on its own line in the post-roll status block
- Format per line: `⚠️ SHADOW GROWTH: {event}`
- Lines appear inside the existing triple-backtick status block, after the interest bar line and before the "Active Traps" line
- When `ShadowGrowthEvents` is empty, no shadow output is printed for that turn (no empty `⚠️ SHADOW GROWTH:` lines)

### AC3: Session summary includes shadow delta table

- After the session outcome line (`**{icon} {outcome} | Turns: ...``), a markdown table titled `## Shadow Changes This Session` is printed
- Table has columns: Shadow, Start, End, Delta
- All six `ShadowStatType` enum values are listed as rows
- Start = `sableStats.GetShadow(type)`, End = `sableShadows.GetEffectiveShadow(type)`, Delta = `sableShadows.GetDelta(type)`
- Delta column uses `+N` format for positive values, `0` for zero

### AC4: Test — running a session where same stat is picked 3 turns in a row shows Fixation +1 in output

This is a behavioral acceptance criterion for manual/integration testing. The session runner must correctly display shadow growth when the game engine fires Fixation growth (triggered by picking the same stat 3 consecutive turns). The prerequisite is that `GameSession` populates `TurnResult.ShadowGrowthEvents` when `PlayerShadows` is non-null — this is already implemented in `GameSession`.

### AC5: Build clean

- `dotnet build session-runner/session-runner.csproj` succeeds with zero errors and zero warnings

---

## Edge Cases

1. **No shadow growth occurs during session**: All deltas are 0. The summary table still prints with all zeros. No `⚠️ SHADOW GROWTH:` lines appear during any turn.

2. **Multiple shadow events in a single turn**: `TurnResult.ShadowGrowthEvents` can contain more than one entry per turn (e.g., Fixation growth + Denial growth). Each event gets its own `⚠️ SHADOW GROWTH:` line.

3. **Game ends early (ghost/unmatched)**: The summary table must still print using whatever shadow state accumulated before the game ended. The `sableShadows` reference remains valid regardless of how the game loop exits.

4. **Negative shadow deltas**: `SessionShadowTracker.ApplyOffset()` can produce negative deltas (e.g., Fixation −1 for stat variety). The delta column should display as `-1`, not `+(-1)`. Use the sign of the integer directly.

5. **`ShadowGrowthEvents` is null**: `TurnResult.ShadowGrowthEvents` defaults to `Array.Empty<string>()` per the existing implementation, so it should never be null. However, a defensive `?.Count > 0` or null check is prudent.

6. **Session ends via `GameEndedException` from `StartTurnAsync()`**: The game loop catches this exception. Shadow summary must still be printed — ensure the summary code runs in the finally/post-loop path regardless of how the loop terminates.

---

## Error Conditions

1. **`sableStats` is null**: `SessionShadowTracker` constructor throws `ArgumentNullException`. This cannot happen in the current session runner because `sableStats` is constructed inline above.

2. **`GameSessionConfig` with `PlayerShadows` but no `OpponentShadows`**: Valid. The opponent's shadow tracking is optional. `GameSession` handles null `OpponentShadows` gracefully (opponent shadow growth is simply not tracked).

3. **Build failure if `SessionShadowTracker` or `GameSessionConfig` types are missing**: Would indicate a broken `Pinder.Core` dependency. Not expected — these types exist and are tested.

---

## Dependencies

- **`Pinder.Core.Stats.SessionShadowTracker`** — already implemented (wraps `StatBlock`, tracks shadow deltas)
- **`Pinder.Core.Stats.ShadowStatType`** — enum, already exists (6 values)
- **`Pinder.Core.Stats.StatBlock`** — already exists, holds base shadow values
- **`Pinder.Core.Conversation.GameSessionConfig`** — already implemented, accepts optional `PlayerShadows`
- **`Pinder.Core.Conversation.GameSession`** — constructor overload accepting `GameSessionConfig` already exists
- **`Pinder.Core.Conversation.TurnResult.ShadowGrowthEvents`** — `IReadOnlyList<string>`, already populated by `GameSession` when `PlayerShadows` is non-null
- **Issue #346** (player agent) — soft dependency; shadow triggers fire more meaningfully with varied stat picks, but the wiring works regardless of which option-picking strategy is used
