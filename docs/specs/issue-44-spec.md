# Spec: Shadow Growth Events — §7 Growth Table in GameSession

**Issue:** #44  
**Depends on:** #43 (Read/Recover shadow growth for Overthinking)  
**Component:** `Pinder.Core.Conversation`, `Pinder.Core.Characters`  
**Target:** .NET Standard 2.0, C# 8.0, zero NuGet dependencies

---

## 1. Overview

Shadow stats in the RPG engine grow in response to specific in-game events (bad rolls, behavioural patterns, game outcomes). Rules v3.4 §7 defines the full growth table. This feature implements all shadow growth triggers inside `GameSession`, using a new `CharacterState` wrapper class that tracks shadow deltas without mutating the immutable `StatBlock`. Growth events are surfaced to the host via `TurnResult.ShadowGrowthEvents` so the UI can display them.

---

## 2. New Class: `CharacterState`

**Namespace:** `Pinder.Core.Characters`  
**Purpose:** Wraps a `CharacterProfile` with mutable per-session shadow deltas and a growth event log. This is the architect-mandated approach (Option D from #58): StatBlock is never mutated.

### Constructor

```csharp
public CharacterState(CharacterProfile profile)
```

- `profile` — non-null `CharacterProfile`. Stored as `Profile` property.
- Initializes an empty shadow delta dictionary and an empty growth log.
- Throws `ArgumentNullException` if `profile` is null.

### Properties

| Name | Type | Description |
|------|------|-------------|
| `Profile` | `CharacterProfile` (readonly) | The underlying immutable character profile. |

### Methods

#### `ApplyShadowGrowth`

```csharp
public void ApplyShadowGrowth(ShadowStatType shadow, int delta, string reason)
```

- Adds `delta` to the running shadow delta for `shadow`. If no delta exists yet for that shadow type, starts from 0.
- Appends `reason` (a human-readable string) to the internal growth log.
- `delta` is typically +1 or +2 but can be negative (e.g., Fixation offset −1).
- `reason` must not be null or empty; throw `ArgumentException` if so.

#### `GetEffective`

```csharp
public int GetEffective(StatType stat)
```

- Returns the effective modifier for `stat`, accounting for both the base shadow value from `Profile.Stats` and any session deltas.
- Formula: `baseVal - ((baseShadow + sessionDelta) / 3)` where:
  - `baseVal = Profile.Stats.GetBase(stat)`
  - `baseShadow = Profile.Stats.GetShadow(StatBlock.ShadowPairs[stat])`
  - `sessionDelta = _shadowDelta[shadow]` (0 if not present)
- Integer division (floor) as per existing `StatBlock.GetEffective` behaviour.

#### `DrainGrowthEvents`

```csharp
public IReadOnlyList<string> DrainGrowthEvents()
```

- Returns a snapshot of all growth event reason strings accumulated since the last drain.
- Clears the internal log after copying.
- Returns an empty list if no events occurred.
- The returned list is a new `List<string>` (caller owns it).

#### `GetShadowDelta`

```csharp
public int GetShadowDelta(ShadowStatType shadow)
```

- Returns the accumulated session delta for the given shadow type.
- Returns 0 if no growth has occurred for that shadow type.
- Useful for end-of-game checks and testing.

---

## 3. Changes to `GameSession`

### Internal State Changes

- Replace `private readonly CharacterProfile _player` with `private readonly CharacterState _playerState`.
- Add tracking fields for shadow growth trigger detection:

| Field | Type | Purpose |
|-------|------|---------|
| `_statsUsedPerTurn` | `List<StatType>` | Stats chosen each turn, in order. For "same stat 3 turns in a row" and "never picked Chaos" checks. |
| `_honestySuccessCount` | `int` | Count of successful Honesty rolls this session. For Denial trigger. |
| `_tropeTrapCount` | `int` | Count of TropeTrap-tier (or worse) failures this session. For Madness trigger (3+ trope traps). |
| `_lastOpener` | `string?` | The `IntendedText` of the first turn's chosen option. For "same opener twice in a row" (requires cross-session tracking by host — see Edge Cases). |
| `_highestPctOptionPicked` | `List<bool>` | Per-turn: whether the player picked the highest-percentage option. For Fixation trigger (3 in a row). |

### Constructor Change

```csharp
public GameSession(
    CharacterProfile player,
    CharacterProfile opponent,
    ILlmAdapter llm,
    IDiceRoller dice,
    ITrapRegistry trapRegistry,
    string? previousOpener = null)  // NEW optional parameter
```

- `previousOpener`: The `IntendedText` of the opener from the player's previous conversation. Used for the Madness "same opener twice in a row" trigger. Null if no previous conversation or unknown.

### Public API: `TurnResult` Changes

`TurnResult` gains a new property:

```csharp
public IReadOnlyList<string> ShadowGrowthEvents { get; }
```

- List of human-readable shadow growth event descriptions that occurred during this turn.
- Empty list (never null) if no shadow growth happened.
- The `TurnResult` constructor gains a corresponding parameter.

### Turn Flow Changes in `ResolveTurnAsync`

After step 5 (Apply interest delta) and before step 6 (Advance trap timers), insert shadow growth evaluation:

**Per-turn shadow growth checks (evaluated every turn):**

1. **Nat 1 triggers** — If `rollResult.IsNatOne`:
   - Grow the shadow paired with the stat used: `+1` to `StatBlock.ShadowPairs[chosenOption.Stat]`.
   - Reason string: `"Nat 1 on {statName}: +1 {shadowName}"`.
   - This covers: Nat 1 on Charm → +1 Madness, Nat 1 on Wit → +1 Dread, Nat 1 on Honesty → +1 Denial, Nat 1 on Chaos → +1 Fixation, Nat 1 on SA → +1 Overthinking.

2. **Catastrophic Wit fail** — If `chosenOption.Stat == StatType.Wit` AND `!rollResult.IsSuccess` AND `rollResult.Tier == FailureTier.Catastrophe`:
   - `+1 Dread`.
   - Reason: `"Catastrophic Wit failure (miss by 10+): +1 Dread"`.

3. **TropeTrap count** — If `rollResult.Tier >= FailureTier.TropeTrap` (i.e., TropeTrap, Catastrophe, or Legendary):
   - Increment `_tropeTrapCount`.
   - If `_tropeTrapCount == 3`: apply `+1 Madness`.
   - Reason: `"3+ trope traps in one conversation: +1 Madness"`.
   - Only triggers once (on exactly reaching 3, not on 4, 5, etc.).

4. **Same stat 3 turns in a row** — After appending `chosenOption.Stat` to `_statsUsedPerTurn`:
   - If the last 3 entries are identical: `+1 Fixation`.
   - Reason: `"Same stat ({statName}) used 3 turns in a row: +1 Fixation"`.
   - Triggers each time a new consecutive-3 is formed (e.g., turns 3, 4, 5 all Charm → triggers on turn 5; turns 4, 5, 6 all Charm → triggers again on turn 6 only if turn 6 extends the streak to a new group of 3).
   - Implementation note: trigger when `_statsUsedPerTurn.Count >= 3` and last 3 are equal. To avoid re-triggering every turn during a long streak, only trigger when the count of consecutive same-stat turns at the tail is exactly 3 (modulo 3 == 0, or simply: trigger on turns 3, 6, 9… of a continuous streak).

5. **Highest-% option 3 turns in a row** — The "highest-percentage option" is defined as option index 0 (the first option returned by the LLM, which is conventionally the safest/highest-probability choice). After recording whether `optionIndex == 0`:
   - If the last 3 entries in `_highestPctOptionPicked` are all `true`: `+1 Fixation`.
   - Reason: `"Highest-% option picked 3 turns in a row: +1 Fixation"`.
   - Same re-trigger logic as "same stat 3 turns in a row".

6. **Honesty success tracking** — If `chosenOption.Stat == StatType.Honesty` AND `rollResult.IsSuccess`:
   - Increment `_honestySuccessCount`.

7. **Interest hits 0** — If `interestAfter == 0` (i.e., `_interest.IsZero` after applying delta):
   - `+2 Dread`.
   - Reason: `"Interest hit 0 (unmatch): +2 Dread"`.

8. **Getting ghosted** — When `GameOutcome.Ghosted` is determined (in `StartTurnAsync`):
   - `+1 Dread`.
   - Reason: `"Ghosted: +1 Dread"`.
   - Note: This happens in `StartTurnAsync`, not `ResolveTurnAsync`. The ghost event must be logged to `_playerState` and drained into a `TurnStart.ShadowGrowthEvents` or the `GameEndedException` must carry the events. **Recommended approach**: Apply the growth before throwing `GameEndedException`, and add a `ShadowGrowthEvents` property to `GameEndedException`.

9. **SA used 3+ times** — Track SA usage count. If, after this turn, SA has been used 3 times total:
   - `+1 Overthinking`.
   - Reason: `"SA used 3+ times in one conversation: +1 Overthinking"`.
   - Triggers once, when the count first reaches 3.

10. **Read/Recover action failures** (from #43):
    - When a Read action fails: `+1 Overthinking`. Reason: `"Read action failed: +1 Overthinking"`.
    - When a Recover action fails: `+1 Overthinking`. Reason: `"Recover action failed: +1 Overthinking"`.
    - The exact mechanism depends on #43's implementation of Read/Recover actions. This spec assumes those actions produce a `RollResult` that `GameSession` can inspect.

**End-of-game shadow growth checks (evaluated when `isGameOver` is true):**

11. **Date secured without Honesty success** — If `outcome == GameOutcome.DateSecured` AND `_honestySuccessCount == 0`:
    - `+1 Denial`.
    - Reason: `"Date secured without any Honesty successes: +1 Denial"`.

12. **Never picked Chaos** — If the game is ending AND `_statsUsedPerTurn` does not contain `StatType.Chaos`:
    - `+1 Fixation`.
    - Reason: `"Never picked Chaos in whole conversation: +1 Fixation"`.

13. **Fixation offset** — If the game is ending AND `_statsUsedPerTurn.Distinct().Count() >= 4`:
    - `−1 Fixation` (reduce, not grow).
    - Reason: `"4+ different stats used in conversation: −1 Fixation"`.
    - This can result in a net negative session delta for Fixation.

After evaluating all applicable triggers, call `_playerState.DrainGrowthEvents()` and pass the result into the `TurnResult` constructor as `ShadowGrowthEvents`.

### "Same opener twice in a row" (Madness)

- On turn 0 (first turn), record `chosenOption.IntendedText` as the session opener.
- Compare against `previousOpener` (passed via constructor).
- If they match (case-insensitive, trimmed): `+1 Madness`.
- Reason: `"Same opener twice in a row: +1 Madness"`.
- The host is responsible for persisting the opener across sessions and passing it into the next `GameSession`.

---

## 4. Input/Output Examples

### Example 1: Nat 1 on Charm

**Setup:** Player rolls Charm, die result is 1.

**Shadow growth:**
- `ApplyShadowGrowth(ShadowStatType.Madness, 1, "Nat 1 on Charm: +1 Madness")`

**TurnResult.ShadowGrowthEvents:** `["Nat 1 on Charm: +1 Madness"]`

### Example 2: Interest drops to 0

**Setup:** Interest is at 1, roll fails with delta −2.

**Shadow growth:**
- `ApplyShadowGrowth(ShadowStatType.Dread, 2, "Interest hit 0 (unmatch): +2 Dread")`

**TurnResult.ShadowGrowthEvents:** `["Interest hit 0 (unmatch): +2 Dread"]`

### Example 3: Multiple triggers in one turn

**Setup:** Player uses Wit for the 3rd consecutive turn, rolls Nat 1.

**Shadow growth:**
- `ApplyShadowGrowth(ShadowStatType.Dread, 1, "Nat 1 on Wit: +1 Dread")` — Nat 1 trigger
- `ApplyShadowGrowth(ShadowStatType.Fixation, 1, "Same stat (Wit) used 3 turns in a row: +1 Fixation")` — same-stat trigger

**TurnResult.ShadowGrowthEvents:** `["Nat 1 on Wit: +1 Dread", "Same stat (Wit) used 3 turns in a row: +1 Fixation"]`

### Example 4: End-of-game with Fixation offset

**Setup:** Game ends (DateSecured). Player used Charm, Wit, Honesty, Chaos across turns. Player had 1 Honesty success. Never used same stat 3x in a row.

**Shadow growth (end-of-game):**
- `ApplyShadowGrowth(ShadowStatType.Fixation, -1, "4+ different stats used in conversation: −1 Fixation")` — offset

**TurnResult.ShadowGrowthEvents** (on final turn): `["4+ different stats used in conversation: −1 Fixation"]`

### Example 5: CharacterState effective modifier

**Setup:** Player has base Charm 4, base Madness shadow 2. Session delta for Madness is +1 (from Nat 1 on Charm).

```
GetEffective(StatType.Charm):
  baseVal = 4
  baseShadow = 2
  sessionDelta = 1
  totalShadow = 3
  penalty = 3 / 3 = 1
  return 4 - 1 = 3
```

---

## 5. Acceptance Criteria

### AC1: All shadow growth events from §7 implemented

All 17 triggers listed in section 3 must be implemented:
- Dread: interest=0 (+2), ghosted (+1), catastrophic Wit fail (+1), Nat 1 Wit (+1)
- Madness: Nat 1 Charm (+1), 3+ trope traps (+1), same opener twice (+1)
- Denial: date secured without Honesty success (+1), Nat 1 Honesty (+1)
- Fixation: highest-% 3 in a row (+1), same stat 3 in a row (+1), never Chaos (+1), Nat 1 Chaos (+1), offset 4+ stats (−1)
- Overthinking: Read fail (+1), Recover fail (+1), SA 3+ times (+1), Nat 1 SA (+1)

### AC2: Shadow stats mutate correctly during a session

- `CharacterState` accumulates deltas without mutating `StatBlock`.
- `GetEffective` returns correct values reflecting both base shadows and session deltas.
- Multiple growths to the same shadow type accumulate additively.

### AC3: `TurnResult.ShadowGrowthEvents` populated when shadow grows

- `TurnResult` exposes `IReadOnlyList<string> ShadowGrowthEvents`.
- Non-empty when any shadow growth occurred during that turn.
- Empty (not null) when no growth occurred.
- Events are drained from `CharacterState` per turn — each event appears in exactly one `TurnResult`.

### AC4: Test coverage for key triggers

Tests must verify at minimum:
- Dread +2 when interest reaches 0.
- Fixation +1 when same stat used 3 turns in a row.
- Madness +1 when Nat 1 on Charm.
- End-of-game Denial +1 when DateSecured with 0 Honesty successes.
- Fixation −1 offset when 4+ distinct stats used.

### AC5: Build clean

- `dotnet build` succeeds with zero warnings in the `Pinder.Core` project.
- All existing tests continue to pass.

---

## 6. Edge Cases

| Scenario | Expected Behaviour |
|----------|-------------------|
| Multiple Nat 1s in same session | Each Nat 1 triggers +1 to the corresponding shadow. They accumulate. |
| Nat 1 on Wit that is also Catastrophe tier | Both "Nat 1 on Wit" (+1 Dread) and "Catastrophic Wit fail" (+1 Dread) trigger. Total: +2 Dread in one turn. |
| Nat 1 on Wit where Nat 1 counts as Legendary, not Catastrophe | "Catastrophic Wit fail" does NOT trigger (tier is Legendary, not Catastrophe). Only Nat 1 trigger fires (+1 Dread). |
| 3+ trope traps: exactly 3 vs. 4+ | +1 Madness fires once when `_tropeTrapCount` reaches 3. Does not fire again at 4, 5, etc. |
| Same stat streak of 6 turns | Triggers Fixation at turn 3 and again at turn 6 (every 3 consecutive). |
| Player uses only 3 distinct stats | Fixation offset does NOT apply (requires 4+). |
| Session with 0 turns (immediate ghost on turn 1 start) | No turn-based triggers fire. Ghost Dread +1 applies. No end-of-game stat checks (conversation didn't really happen). |
| `previousOpener` is null | "Same opener twice" check skips — no Madness growth for opener. |
| `previousOpener` matches but with different casing/whitespace | Match is case-insensitive, trimmed. "Hello there" == "  hello THERE  ". |
| Fixation offset and Fixation growth in same game | Both apply. E.g., +1 (never Chaos) and −1 (4+ stats) net to 0. |
| Shadow delta goes negative | Allowed. `GetShadowDelta` can return negative values. `GetEffective` handles negative total shadow correctly (no penalty if totalShadow < 3). |
| Existing `TurnResult` consumers | Adding `ShadowGrowthEvents` parameter is a breaking change to the constructor. All existing call sites and tests must be updated. |
| Horniness shadow | Not in the §7 growth table — Horniness is rolled fresh each conversation (1d10), not grown by events. No triggers for Horniness. |

---

## 7. Error Conditions

| Condition | Error Type | Message |
|-----------|-----------|---------|
| `CharacterState` constructed with null profile | `ArgumentNullException` | `"profile"` |
| `ApplyShadowGrowth` called with null/empty reason | `ArgumentException` | `"reason cannot be null or empty"` |
| `GameSession` constructor with null player | `ArgumentNullException` | `"player"` |
| Ghost event shadow growth when game ends in `StartTurnAsync` | Growth applied to `_playerState` before `GameEndedException` is thrown. Events accessible via `GameEndedException.ShadowGrowthEvents` (new property, `IReadOnlyList<string>`). |

---

## 8. Dependencies

| Dependency | Type | Notes |
|------------|------|-------|
| #43 (Read/Recover actions) | Hard dependency | Defines Read/Recover action types and their failure detection. Overthinking triggers for Read/Recover failures depend on this. |
| `Pinder.Core.Stats.StatBlock` | Internal | `ShadowPairs` dictionary, `GetBase`, `GetShadow` methods. Not modified. |
| `Pinder.Core.Stats.StatType` | Internal | Enum values for stat identification. |
| `Pinder.Core.Stats.ShadowStatType` | Internal | Enum values for shadow stat identification. |
| `Pinder.Core.Rolls.RollResult` | Internal | `IsNatOne`, `Tier`, `Stat`, `IsSuccess` properties. |
| `Pinder.Core.Rolls.FailureTier` | Internal | `Catastrophe`, `TropeTrap`, `Legendary` values. |
| `Pinder.Core.Characters.CharacterProfile` | Internal | Wrapped by `CharacterState`. Not modified. |
| `Pinder.Core.Conversation.TurnResult` | Internal | Modified: new `ShadowGrowthEvents` property. |
| `Pinder.Core.Conversation.GameSession` | Internal | Modified: uses `CharacterState`, adds tracking fields, evaluates triggers. |
| `Pinder.Core.Conversation.GameEndedException` | Internal | Modified: new `ShadowGrowthEvents` property for ghost-triggered growth. |

---

## 9. Files to Create or Modify

| File | Action | Description |
|------|--------|-------------|
| `src/Pinder.Core/Characters/CharacterState.cs` | **Create** | New `CharacterState` class per §2 above. |
| `src/Pinder.Core/Conversation/GameSession.cs` | **Modify** | Replace `CharacterProfile _player` with `CharacterState _playerState`. Add tracking fields. Add shadow growth evaluation to `ResolveTurnAsync`. Add `previousOpener` constructor parameter. |
| `src/Pinder.Core/Conversation/TurnResult.cs` | **Modify** | Add `ShadowGrowthEvents` property and constructor parameter. |
| `src/Pinder.Core/Conversation/GameEndedException.cs` | **Modify** | Add `ShadowGrowthEvents` property for ghost-triggered growth events. |
