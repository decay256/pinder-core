# Spec: XP Tracking — Implement §10 XP Sources and Level-Up Accumulation

**Issue:** #48  
**Depends on:** #42 (RiskTier on RollResult; Hard/Bold tier bonuses), #43 (Read/Recover turn actions), #44 (Shadow growth events — introduces `CharacterState`)  
**Maturity:** Prototype  
**Target:** .NET Standard 2.0, C# 8.0, zero NuGet dependencies

---

## 1. Overview

Rules v3.4 §10 defines XP sources but the engine currently has no XP tracking. `LevelTable` can resolve XP→level, but nothing records XP events during gameplay. This feature adds an `XpLedger` class that accumulates labeled XP events per session, wires all §10 XP sources into `GameSession`, and surfaces per-turn and session-total XP to the host via `TurnResult.XpEarned` and a session-end accessor.

---

## 2. XP Source Table

These are the XP awards defined by §10 and issue #48:

| Action | XP Amount | When Awarded |
|--------|-----------|--------------|
| Successful check (DC ≤ 13) | 5 | On success in `ResolveTurnAsync` |
| Successful check (DC 14–17) | 10 | On success in `ResolveTurnAsync` |
| Successful check (DC ≥ 18) | 15 | On success in `ResolveTurnAsync` |
| Failed check | 2 | On failure in `ResolveTurnAsync` |
| Nat 20 | 25 | On nat-20 in `ResolveTurnAsync` (replaces success tier XP, not additive) |
| Nat 1 | 10 | On nat-1 in `ResolveTurnAsync` (replaces failed check XP, not additive) |
| Date secured | 50 | When game ends with `GameOutcome.DateSecured` |
| Trap recovery (successful Recover action) | 15 | On successful `RecoverAsync` (from #43) |
| Conversation complete (no date) | 5 | When game ends with `GameOutcome.Unmatched` or `GameOutcome.Ghosted` |

**Precedence rules for roll XP:**
- Nat 20 → award 25 XP (do NOT also award the DC-tier success XP)
- Nat 1 → award 10 XP (do NOT also award the 2 XP failed check)
- Normal success → award 5/10/15 based on DC tier
- Normal failure → award 2

---

## 3. New Class: `XpLedger`

### Namespace
`Pinder.Core.Progression`

### File
`src/Pinder.Core/Progression/XpLedger.cs`

### Purpose
Accumulates XP events with source labels during a single game session. Immutable event log — events can only be added, never removed.

### Nested Type: `XpEvent`

```csharp
namespace Pinder.Core.Progression
{
    public sealed class XpLedger
    {
        public sealed class XpEvent
        {
            /// <summary>Human-readable label identifying the XP source (e.g. "SuccessDC15", "Nat20", "DateSecured").</summary>
            public string Source { get; }

            /// <summary>XP amount awarded. Always positive.</summary>
            public int Amount { get; }

            public XpEvent(string source, int amount);
        }
    }
}
```

**Constructor constraints:**
- `source` must not be null or empty; throw `ArgumentException` if so.
- `amount` must be > 0; throw `ArgumentOutOfRangeException` if ≤ 0.

### Constructor

```csharp
public XpLedger()
```

Creates an empty ledger with `TotalXp == 0` and no events.

### Properties

| Name | Type | Description |
|------|------|-------------|
| `TotalXp` | `int` | Sum of all recorded event amounts. Starts at 0. |
| `Events` | `IReadOnlyList<XpEvent>` | All recorded XP events in chronological order. |

### Methods

#### `Record`

```csharp
public void Record(string source, int amount)
```

- Creates an `XpEvent` with the given `source` and `amount`.
- Appends it to the internal event list.
- Increments `TotalXp` by `amount`.
- Throws `ArgumentException` if `source` is null or empty.
- Throws `ArgumentOutOfRangeException` if `amount` <= 0.

#### `DrainTurnEvents`

```csharp
public IReadOnlyList<XpEvent> DrainTurnEvents()
```

- Returns all events recorded since the last drain (or since construction if never drained).
- Internally tracks a "drain cursor" (index into the event list). Each call returns events from the cursor to the end, then advances the cursor.
- Returns an empty list if no new events since last drain.
- The returned list is a new `List<XpEvent>` — caller owns it.
- Does NOT remove events from the ledger or affect `TotalXp`.

---

## 4. Standard Source Labels

To ensure consistency across all XP recording sites, these exact string labels must be used:

| Label | When Used |
|-------|-----------|
| `"Success_DC_Low"` | Successful check, DC ≤ 13 |
| `"Success_DC_Mid"` | Successful check, DC 14–17 |
| `"Success_DC_High"` | Successful check, DC ≥ 18 |
| `"Failure"` | Normal failed check (not nat-1) |
| `"Nat20"` | Natural 20 rolled |
| `"Nat1"` | Natural 1 rolled |
| `"DateSecured"` | Game ended with DateSecured outcome |
| `"TrapRecovery"` | Successful Recover action (from #43) |
| `"ConversationComplete"` | Game ended without a date (Unmatched or Ghosted) |

---

## 5. Modifications to `GameSession`

### 5.1 New Field

```csharp
private readonly XpLedger _xpLedger;
```

Initialized in the constructor as `new XpLedger()`.

### 5.2 New Public Property

```csharp
/// <summary>Total XP earned during this session.</summary>
public int TotalXpEarned => _xpLedger.TotalXp;

/// <summary>The full XP ledger for this session.</summary>
public XpLedger XpLedger => _xpLedger;
```

### 5.3 XP Recording in `ResolveTurnAsync`

After the roll is resolved and the interest delta is computed (step 2 in existing flow), record XP based on the roll outcome:

```
if (rollResult.IsNatTwenty)
    _xpLedger.Record("Nat20", 25);
else if (rollResult.IsNatOne)
    _xpLedger.Record("Nat1", 10);
else if (rollResult.IsSuccess)
    _xpLedger.Record(DcTierLabel(rollResult.DC), DcTierXp(rollResult.DC));
else
    _xpLedger.Record("Failure", 2);
```

**DC tier helper logic** (private, in GameSession or as a static helper):

| Condition | Label | Amount |
|-----------|-------|--------|
| DC ≤ 13 | `"Success_DC_Low"` | 5 |
| DC 14–17 | `"Success_DC_Mid"` | 10 |
| DC ≥ 18 | `"Success_DC_High"` | 15 |

### 5.4 XP Recording in `RecoverAsync` (from #43)

When `RecoverAsync` succeeds (trap cleared), record:

```
_xpLedger.Record("TrapRecovery", 15);
```

This is added to the success branch of `RecoverAsync` only. Failed recovery attempts do NOT grant XP.

### 5.5 XP Recording at Game End

When the game ends (interest hits 0 or 25, or ghosted), record the appropriate end-of-game XP before returning the final `TurnResult`:

- `GameOutcome.DateSecured` → `_xpLedger.Record("DateSecured", 50)`
- `GameOutcome.Unmatched` → `_xpLedger.Record("ConversationComplete", 5)`
- `GameOutcome.Ghosted` → `_xpLedger.Record("ConversationComplete", 5)`

This recording happens at the point where `_ended = true` and `_outcome` is set in `ResolveTurnAsync` (step 15 in the existing flow), before the `TurnResult` is constructed.

### 5.6 Drain Per Turn

After all XP events for the turn are recorded, call `_xpLedger.DrainTurnEvents()` to get the events for this turn. Sum their amounts to populate `TurnResult.XpEarned`.

---

## 6. Modifications to `TurnResult`

### New Property

```csharp
/// <summary>XP earned during this turn.</summary>
public int XpEarned { get; }
```

### Constructor Change

Add `int xpEarned` parameter to the `TurnResult` constructor. Store it in the `XpEarned` property.

---

## 7. Modifications to `ReadResult` and `RecoverResult` (from #43)

### `RecoverResult` — New Property

```csharp
/// <summary>XP earned from the Recover action (15 on success, 0 on failure).</summary>
public int XpEarned { get; }
```

Constructor updated to accept `int xpEarned`.

### `ReadResult` — New Property

```csharp
/// <summary>XP earned from the Read action. Always 0 (Read does not grant XP).</summary>
public int XpEarned { get; }
```

Constructor updated to accept `int xpEarned`. Always passed as 0.

---

## 8. Input/Output Examples

### Example 1: Normal Success (DC 15)

- Roll succeeds with DC = 15
- XP recorded: `XpEvent("Success_DC_Mid", 10)`
- `TurnResult.XpEarned` = 10

### Example 2: Nat 20 (DC 18)

- Roll is nat-20, DC = 18
- XP recorded: `XpEvent("Nat20", 25)` — NOT `XpEvent("Success_DC_High", 15)`
- `TurnResult.XpEarned` = 25

### Example 3: Nat 1

- Roll is nat-1
- XP recorded: `XpEvent("Nat1", 10)` — NOT `XpEvent("Failure", 2)`
- `TurnResult.XpEarned` = 10

### Example 4: Normal Failure (DC 14)

- Roll fails, DC = 14, not nat-1
- XP recorded: `XpEvent("Failure", 2)`
- `TurnResult.XpEarned` = 2

### Example 5: Success on DC 13 (boundary)

- Roll succeeds, DC = 13
- XP recorded: `XpEvent("Success_DC_Low", 5)`
- `TurnResult.XpEarned` = 5

### Example 6: Date Secured on Final Turn

- Roll succeeds, DC = 16, interest hits 25
- XP recorded this turn: `XpEvent("Success_DC_Mid", 10)` + `XpEvent("DateSecured", 50)`
- `TurnResult.XpEarned` = 60
- `session.TotalXpEarned` = sum of all turns

### Example 7: Successful Trap Recovery

- `RecoverAsync()` succeeds
- XP recorded: `XpEvent("TrapRecovery", 15)`
- `RecoverResult.XpEarned` = 15

### Example 8: Ghosted After Failed Roll

- Roll fails (not nat-1), then ghost triggers
- XP recorded this turn: `XpEvent("Failure", 2)` + `XpEvent("ConversationComplete", 5)`
- `TurnResult.XpEarned` = 7

### Example 9: Full Session XP Ledger

Turn 1: Success DC 13 → 5 XP  
Turn 2: Failure → 2 XP  
Turn 3: Nat 20, DC 18 → 25 XP  
Turn 4: Success DC 15, date secured → 10 + 50 = 60 XP  
**Total:** 5 + 2 + 25 + 60 = 92 XP  
`session.TotalXpEarned` = 92

---

## 9. Acceptance Criteria

### AC-1: `XpLedger` tracks XP events with source labels

- `XpLedger` stores a chronological list of `XpEvent` objects, each with a `string Source` and `int Amount`.
- `TotalXp` returns the sum of all event amounts.
- `Events` returns the full list.
- `DrainTurnEvents()` returns only events since the last drain.

### AC-2: All XP sources from §10 wired into GameSession

- Successful check: 5/10/15 by DC tier (≤13 / 14–17 / ≥18)
- Failed check: 2
- Nat 20: 25 (replaces DC-tier XP)
- Nat 1: 10 (replaces failure XP)
- Date secured: 50
- Trap recovery: 15
- Conversation complete (no date): 5

All nine XP source types are recorded in the `XpLedger` at the correct points in `GameSession`.

### AC-3: `TurnResult.XpEarned` populated

Every `TurnResult` returned by `ResolveTurnAsync` includes an `XpEarned` property containing the total XP earned during that specific turn (including end-of-game XP if the game ended on that turn).

### AC-4: Date secured grants 50 XP at game end

When `GameOutcome.DateSecured` is triggered, exactly one `XpEvent("DateSecured", 50)` is recorded in the ledger. This is included in the final turn's `TurnResult.XpEarned`.

### AC-5: Tests verify XP accumulation for success, fail, nat20, nat1

Unit tests must cover:
- Normal success at each DC tier (≤13, 14–17, ≥18)
- Normal failure
- Nat 20 (verify DC-tier XP is NOT also awarded)
- Nat 1 (verify failure XP is NOT also awarded)
- Date secured end-of-game bonus
- Conversation complete (Unmatched) end-of-game bonus
- Trap recovery XP
- Multi-turn accumulation: `TotalXpEarned` matches sum of all `TurnResult.XpEarned`
- DC boundary values: DC = 13, DC = 14, DC = 17, DC = 18

### AC-6: Build clean

`dotnet build` and `dotnet test` must pass with zero errors. No existing tests may break.

---

## 10. Edge Cases

| Scenario | Expected Behavior |
|----------|-------------------|
| DC exactly 13 | Low tier: 5 XP |
| DC exactly 14 | Mid tier: 10 XP |
| DC exactly 17 | Mid tier: 10 XP |
| DC exactly 18 | High tier: 15 XP |
| DC < 13 (e.g., DC 10 from base 13 minus modifiers) | Low tier: 5 XP |
| DC > 20 (theoretically possible with high opponent stats) | High tier: 15 XP |
| Nat 20 with DC ≤ 13 | 25 XP (nat-20 overrides, NOT 5 + 25) |
| Nat 1 when roll would have succeeded | 10 XP (nat-1 is auto-fail, gets nat-1 XP) |
| Game ends on first turn (interest starts at 10, delta pushes to 0 or 25) | Turn XP + end-game XP both recorded; `TurnResult.XpEarned` includes both |
| Multiple XP events in one turn (e.g. success + date secured) | Both recorded; `XpEarned` = sum of both |
| `ReadAsync` action | No XP awarded (Read does not appear in §10 XP sources) |
| `Wait` action | No XP awarded (Wait does not appear in §10 XP sources) |
| Failed `RecoverAsync` | No XP awarded (only successful recovery grants XP) |
| Ghosted outcome | 5 XP for conversation complete (same as Unmatched) |
| `XpLedger.Record` called with empty source | Throws `ArgumentException` |
| `XpLedger.Record` called with amount = 0 | Throws `ArgumentOutOfRangeException` |
| `XpLedger.Record` called with negative amount | Throws `ArgumentOutOfRangeException` |
| `DrainTurnEvents` called twice with no intervening `Record` | Second call returns empty list |

---

## 11. Error Conditions

| Condition | Exception Type | Message Pattern |
|-----------|---------------|-----------------|
| `XpLedger.Record(null, 5)` | `ArgumentException` | Source must not be null or empty |
| `XpLedger.Record("", 5)` | `ArgumentException` | Source must not be null or empty |
| `XpLedger.Record("Nat20", 0)` | `ArgumentOutOfRangeException` | Amount must be greater than 0 |
| `XpLedger.Record("Nat20", -1)` | `ArgumentOutOfRangeException` | Amount must be greater than 0 |
| `XpEvent` constructor with null source | `ArgumentException` | Source must not be null or empty |
| `XpEvent` constructor with amount ≤ 0 | `ArgumentOutOfRangeException` | Amount must be greater than 0 |

No new exception types are introduced. Existing `GameEndedException`, `InvalidOperationException`, and `ArgumentOutOfRangeException` from `GameSession` are unchanged.

---

## 12. Dependencies

### Internal (same repo)

| Component | Relationship |
|-----------|-------------|
| `Pinder.Core.Progression.LevelTable` | Existing — provides `GetLevel(int xp)` for host to resolve XP→level. Not called by `XpLedger` itself. |
| `Pinder.Core.Conversation.GameSession` | Modified — records XP events during `ResolveTurnAsync` and at game end |
| `Pinder.Core.Conversation.TurnResult` | Modified — new `XpEarned` property |
| `Pinder.Core.Rolls.RollResult` | Read-only — `IsNatTwenty`, `IsNatOne`, `IsSuccess`, `DC` used to determine XP source |
| Issue #42 (RiskTier) | Dependency — `RollResult.DC` must be available (already is). Risk tier does NOT affect XP amounts. |
| Issue #43 (Read/Recover/Wait) | Dependency — `RecoverAsync` success path must record trap recovery XP. `ReadAsync` and `Wait` do NOT record XP. |
| Issue #44 (Shadow growth / `CharacterState`) | Dependency — `GameSession` may use `CharacterState` by this point; XP tracking is additive and does not interact with shadow growth. |

### External

None. Zero NuGet dependencies.

---

## 13. Integration Notes

### XP is Session-Scoped, Not Persistent

`XpLedger` lives inside `GameSession`. When the session object is garbage collected, the ledger is gone. The **host** (Unity) is responsible for:
1. Reading `session.TotalXpEarned` after the game ends
2. Adding it to the player's persistent XP total
3. Calling `LevelTable.GetLevel(persistentXp)` to check for level-ups

The engine does NOT persist XP across sessions.

### Order of Operations in `ResolveTurnAsync`

The XP recording step slots into the existing flow after roll resolution (step 1–2 in current code) and before interest application:

1. Roll → `RollEngine.Resolve()`
2. Compute interest delta (`SuccessScale` / `FailureScale` + risk bonus + momentum)
3. **NEW: Record roll XP to ledger**
4. Apply interest delta → `InterestMeter.Apply()`
5. Advance traps
6. Deliver message via LLM
7. Opponent response via LLM
8. Check end conditions
9. **NEW: Record end-of-game XP if game ended**
10. **NEW: Drain turn events → sum → `TurnResult.XpEarned`**
11. Return `TurnResult`

### Rules-to-Code Sync Table Additions

| Rules Section | Rule Value | C# Location | C# Constant/Expression |
|---|---|---|---|
| §10 XP: success by DC tier | DC ≤13→5, 14–17→10, ≥18→15 | `Conversation/GameSession.cs` | DC tier XP logic in `ResolveTurnAsync` |
| §10 XP: failure | 2 | `Conversation/GameSession.cs` | `_xpLedger.Record("Failure", 2)` |
| §10 XP: nat 20 | 25 | `Conversation/GameSession.cs` | `_xpLedger.Record("Nat20", 25)` |
| §10 XP: nat 1 | 10 | `Conversation/GameSession.cs` | `_xpLedger.Record("Nat1", 10)` |
| §10 XP: date secured | 50 | `Conversation/GameSession.cs` | `_xpLedger.Record("DateSecured", 50)` |
| §10 XP: trap recovery | 15 | `Conversation/GameSession.cs` | `_xpLedger.Record("TrapRecovery", 15)` |
| §10 XP: conversation complete | 5 | `Conversation/GameSession.cs` | `_xpLedger.Record("ConversationComplete", 5)` |
