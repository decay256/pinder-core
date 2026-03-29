# Spec: XP Tracking — §10 XP Sources and Level-Up Accumulation

**Issue:** #48
**Sprint:** 7 — RPG Rules Complete
**Depends on:** #42 (RollEngine.ResolveFixedDC, externalBonus/dcAdjustment params), #43 (Read/Recover/Wait actions), #44 (Shadow growth events)
**Contract:** `contracts/sprint-7-xp-tracking.md`
**Maturity:** Prototype

---

## 1. Overview

Rules v3.4 §10 defines nine XP sources that reward players for actions during a conversation. Currently `LevelTable` exists (mapping XP totals to levels, bonuses, build points, item slots) but no XP is ever tracked or awarded during gameplay. This feature introduces `XpLedger`, a session-scoped accumulator that records XP events with source labels, and wires all nine XP sources into `GameSession` so that `TurnResult.XpEarned` is populated each turn and end-of-game XP is recorded.

---

## 2. Function Signatures

### 2.1 XpLedger (new class)

**File:** `src/Pinder.Core/Progression/XpLedger.cs`
**Namespace:** `Pinder.Core.Progression`

```csharp
public sealed class XpLedger
{
    /// <summary>Total XP accumulated across all recorded events this session.</summary>
    public int TotalXp { get; }

    /// <summary>All recorded events, in order. Each event is (Amount, Source).</summary>
    public IReadOnlyList<(int Amount, string Source)> Events { get; }

    /// <summary>XP amount from the most recent Record() call. 0 if no events recorded yet.</summary>
    public int LastRecordedAmount { get; }

    /// <summary>
    /// Record an XP event.
    /// </summary>
    /// <param name="amount">XP to add. Must be positive (>0).</param>
    /// <param name="source">Human-readable source label (e.g. "Nat 20", "Failed check").</param>
    /// <exception cref="ArgumentOutOfRangeException">If amount is less than or equal to 0.</exception>
    /// <exception cref="ArgumentNullException">If source is null.</exception>
    public void Record(int amount, string source);
}
```

**Internal state:**
- A `List<(int Amount, string Source)>` backing the `Events` property.
- `TotalXp` is a running sum updated on each `Record()` call.
- `LastRecordedAmount` is set to `amount` on each `Record()` call.

### 2.2 GameSession changes

The following changes are made to `Pinder.Core.Conversation.GameSession`:

#### 2.2.1 New field

```csharp
private readonly XpLedger _xpLedger;
```

Instantiated in both existing and new (config-based) constructors as `new XpLedger()`.

#### 2.2.2 New public property

```csharp
/// <summary>Total XP earned in this session so far.</summary>
public int TotalXpEarned => _xpLedger.TotalXp;
```

#### 2.2.3 New public property (ledger access)

```csharp
/// <summary>The XP ledger for this session. Read-only access for the host.</summary>
public XpLedger XpLedger => _xpLedger;
```

#### 2.2.4 ResolveTurnAsync XP recording

After the roll is resolved and before constructing `TurnResult`, XP is recorded:

```csharp
int xpEarned = RecordTurnXp(roll);
// ... pass xpEarned to TurnResult constructor
```

Private helper:

```csharp
private int RecordTurnXp(RollResult roll);
```

Returns the XP amount recorded for this turn.

#### 2.2.5 ReadAsync XP recording (from #43)

After the Read roll resolves:
- On success: `_xpLedger.Record(5, "Read success")` (DC 12 ≤ 13 tier → 5 XP)
- On failure: `_xpLedger.Record(2, "Failed check")`

The XP amount is exposed on `ReadResult.XpEarned`.

#### 2.2.6 RecoverAsync XP recording (from #43)

After the Recover roll resolves:
- On success: `_xpLedger.Record(15, "Trap recovery")`
- On failure: `_xpLedger.Record(2, "Failed check")`

The XP amount is exposed on `RecoverResult.XpEarned`.

#### 2.2.7 Wait — no XP

`Wait()` does not perform a roll and awards no XP.

#### 2.2.8 Game end XP

When the game ends (interest reaches 0 or 25, or ghosted):
- `GameOutcome.DateSecured` → `_xpLedger.Record(50, "Date secured")`
- `GameOutcome.Unmatched` → `_xpLedger.Record(5, "Conversation complete")`
- `GameOutcome.Ghosted` → `_xpLedger.Record(5, "Conversation complete")`

Game-end XP is recorded at the point where `_ended` is set to `true` and `_outcome` is assigned, before throwing `GameEndedException` or returning the final `TurnResult`.

### 2.3 ReadResult / RecoverResult changes

Both `ReadResult` and `RecoverResult` (introduced by #43) gain an `XpEarned` property:

```csharp
/// <summary>Amount of XP earned from this action. 0 if none.</summary>
public int XpEarned { get; }
```

Added as an optional constructor parameter with default value `0` for backward compatibility.

---

## 3. XP Source Table

| # | Action | XP Amount | Source Label | When Recorded |
|---|--------|-----------|--------------|---------------|
| 1 | Successful check (DC ≤ 13) | 5 | `"Success (easy)"` | `ResolveTurnAsync`, after roll |
| 2 | Successful check (DC 14–17) | 10 | `"Success (medium)"` | `ResolveTurnAsync`, after roll |
| 3 | Successful check (DC ≥ 18) | 15 | `"Success (hard)"` | `ResolveTurnAsync`, after roll |
| 4 | Failed check | 2 | `"Failed check"` | `ResolveTurnAsync`, `ReadAsync`, `RecoverAsync` |
| 5 | Natural 20 | 25 | `"Nat 20"` | `ResolveTurnAsync`, after roll |
| 6 | Natural 1 | 10 | `"Nat 1"` | `ResolveTurnAsync`, after roll |
| 7 | Date secured | 50 | `"Date secured"` | Game end |
| 8 | Trap recovery | 15 | `"Trap recovery"` | `RecoverAsync`, on success |
| 9 | Conversation complete (no date) | 5 | `"Conversation complete"` | Game end (Unmatched or Ghosted) |

### 3.1 DC Tier Logic for Success XP

The DC used is `RollResult.DC` (the actual DC the roll was resolved against):

- `roll.DC <= 13` → 5 XP
- `roll.DC >= 14 && roll.DC <= 17` → 10 XP
- `roll.DC >= 18` → 15 XP

For fixed-DC rolls (Read/Recover use DC 12), the DC tier is always ≤13, so success XP is always 5.

### 3.2 Precedence Rules

Nat 20 and Nat 1 XP **replace** the normal success/fail XP for that roll — they are **not additive**:

- If `roll.IsNatTwenty` is `true`: record 25 XP (`"Nat 20"`). Do **not** also record success XP.
- If `roll.IsNatOne` is `true`: record 10 XP (`"Nat 1"`). Do **not** also record failure XP.
- Otherwise: record success or failure XP based on `roll.IsSuccess`.

Exactly **one** XP event is recorded per roll.

Trap recovery XP (15) from `RecoverAsync` success is a **separate** source — it is recorded **instead of** the normal success XP (5). A successful Recover awards 15 XP total, not 15 + 5. Similarly, if the Recover roll is a Nat 20, it awards 25 XP (Nat 20 takes precedence over Trap recovery).

Game-end XP (50 or 5) is recorded **in addition to** the last turn's roll XP.

---

## 4. Input/Output Examples

### Example 1: Normal success against easy DC

```
Roll: d20=14, stat=3, level=0, DC=13 → Total=17, IsSuccess=true, IsNatTwenty=false
XP recorded: (5, "Success (easy)")
TurnResult.XpEarned: 5
```

### Example 2: Normal success against hard DC

```
Roll: d20=18, stat=2, level=1, DC=19 → Total=21, IsSuccess=true, IsNatTwenty=false
XP recorded: (15, "Success (hard)")
TurnResult.XpEarned: 15
```

### Example 3: Normal success against medium DC

```
Roll: d20=12, stat=3, level=0, DC=16 → Total=15, IsSuccess=false... 
Actually: DC=15, Total=15, IsSuccess=true
XP recorded: (10, "Success (medium)")
TurnResult.XpEarned: 10
```

### Example 4: Failed check

```
Roll: d20=5, stat=2, level=0, DC=15 → Total=7, IsSuccess=false
XP recorded: (2, "Failed check")
TurnResult.XpEarned: 2
```

### Example 5: Nat 20

```
Roll: d20=20, stat=1, level=0, DC=18 → Total=21, IsNatTwenty=true, IsSuccess=true
XP recorded: (25, "Nat 20") — NOT (15, "Success (hard)")
TurnResult.XpEarned: 25
```

### Example 6: Nat 1

```
Roll: d20=1, stat=4, level=2, DC=13 → Total=7, IsNatOne=true, IsSuccess=false
XP recorded: (10, "Nat 1") — NOT (2, "Failed check")
TurnResult.XpEarned: 10
```

### Example 7: Successful Recover

```
RecoverAsync succeeds (DC 12, SA roll)
XP recorded: (15, "Trap recovery")
RecoverResult.XpEarned: 15
```

### Example 8: Failed Read

```
ReadAsync fails (DC 12, SA roll)
XP recorded: (2, "Failed check")
ReadResult.XpEarned: 2
```

### Example 9: Game end — Date secured after 8 turns

```
Turn 8 roll succeeds (DC 14): XP recorded (10, "Success (medium)")
Interest reaches 25 → DateSecured
XP recorded: (50, "Date secured")
TurnResult.XpEarned: 10 (turn XP only — game-end XP is in ledger but not in TurnResult)
XpLedger.TotalXp: sum of all 8 turns + 50
```

### Example 10: Accumulation across session

```
Turn 1: success DC 13 → 5 XP. TotalXp = 5
Turn 2: fail → 2 XP. TotalXp = 7
Turn 3: Nat 20 → 25 XP. TotalXp = 32
Turn 4: RecoverAsync success → 15 XP. TotalXp = 47
Turn 5: success DC 16 → 10 XP. TotalXp = 57
Game end (Unmatched) → 5 XP. TotalXp = 62
LevelTable.GetLevel(62) → 2 (threshold is 50 for L2)
```

---

## 5. Acceptance Criteria

### AC1: XpLedger tracks XP events with source labels

`XpLedger` is a `sealed class` in `Pinder.Core.Progression` that:
- Exposes `TotalXp` (int) — running sum of all recorded amounts
- Exposes `Events` (`IReadOnlyList<(int Amount, string Source)>`) — full event history
- Exposes `LastRecordedAmount` (int) — the amount from the most recent `Record()` call
- Has a `Record(int amount, string source)` method that appends an event, updates TotalXp, and sets LastRecordedAmount
- Throws `ArgumentOutOfRangeException` if `amount <= 0`
- Throws `ArgumentNullException` if `source` is null

### AC2: All XP sources from §10 wired into GameSession

All nine XP sources from the table in §3 are recorded at the appropriate points in `GameSession`:
- `ResolveTurnAsync`: success (tiered by DC), failure, Nat 20, Nat 1
- `ReadAsync`: success (5 XP), failure (2 XP)
- `RecoverAsync`: success (15 XP), failure (2 XP)
- Game end: DateSecured (50 XP), Unmatched/Ghosted (5 XP)
- `Wait`: no XP

### AC3: TurnResult.XpEarned populated each turn

After `ResolveTurnAsync`, the `TurnResult.XpEarned` field contains the XP earned from that specific turn's roll (not cumulative, not including game-end XP). This uses the existing `xpEarned` constructor parameter on `TurnResult`.

### AC4: Date secured grants 50 XP at game end

When interest reaches 25 and `GameOutcome.DateSecured` is triggered, `_xpLedger.Record(50, "Date secured")` is called. This happens regardless of whether the game ends during `ResolveTurnAsync`, `StartTurnAsync`, or any other action.

### AC5: Trap recovery (via Recover action) grants 15 XP

When `RecoverAsync` succeeds (clears a trap), `_xpLedger.Record(15, "Trap recovery")` is called. This replaces the normal success XP for that roll — only 15 XP total, not 15 + 5.

### AC6: Tests verify XP accumulation for success, fail, nat20, nat1, trap recovery

Tests must cover:
- Each of the 9 XP sources individually
- Nat 20 replaces success XP (not additive)
- Nat 1 replaces failure XP (not additive)
- DC tier boundaries: DC=13 (5 XP), DC=14 (10 XP), DC=17 (10 XP), DC=18 (15 XP)
- Multi-turn accumulation (TotalXp grows correctly)
- Game-end XP recorded for all three outcomes
- `ReadAsync` and `RecoverAsync` XP recording
- `Wait` awards no XP
- Ledger events list is complete and ordered

### AC7: Build clean

The solution compiles with zero errors and zero warnings under `netstandard2.0` / `LangVersion 8.0`. All existing 254+ tests continue to pass.

---

## 6. Edge Cases

### 6.1 DC boundary values

| DC | Expected tier | XP |
|----|---------------|-----|
| 1 | ≤ 13 | 5 |
| 13 | ≤ 13 | 5 |
| 14 | 14–17 | 10 |
| 17 | 14–17 | 10 |
| 18 | ≥ 18 | 15 |
| 25 | ≥ 18 | 15 |

### 6.2 Nat 20 against easy DC

Even though DC ≤ 13 would only award 5 XP normally, a Nat 20 awards 25 XP. The DC tier is irrelevant when `IsNatTwenty` is true.

### 6.3 Nat 1 with high stat mod

Even if the player's stat modifier is very high, a Nat 1 is an auto-fail. XP awarded is 10 (`"Nat 1"`), not 2 (`"Failed check"`).

### 6.4 Game ends on the same turn as a roll

Both the turn's roll XP and the game-end XP are recorded. `TurnResult.XpEarned` reflects only the roll XP. The game-end XP is visible in `XpLedger.Events` and `XpLedger.TotalXp`.

### 6.5 Game ends via ghost trigger (no roll)

If the game ends due to ghosting at the start of a turn (`StartTurnAsync` ghost check), game-end XP (5, `"Conversation complete"`) is still recorded. No roll XP is awarded since no roll occurred.

### 6.6 Empty ledger

Before any turns, `TotalXp` is 0, `Events` is empty, `LastRecordedAmount` is 0.

### 6.7 Recover with Nat 20

A Recover roll that results in Nat 20: awards 25 XP (`"Nat 20"`), not 15 XP (`"Trap recovery"`). Nat 20 takes precedence over all other success sources.

### 6.8 Recover with Nat 1

A Recover roll that results in Nat 1: awards 10 XP (`"Nat 1"`), not 2 XP (`"Failed check"`). Nat 1 takes precedence over normal failure XP.

### 6.9 Read success

Read uses fixed DC 12. On success: 5 XP (`"Read success"`). This is a distinct source label from `"Success (easy)"` to distinguish the action type, though both award 5 XP.

### 6.10 Multiple Records in one turn

A single `ResolveTurnAsync` call records exactly one roll-based XP event. If the game also ends on that turn, a second event (game-end XP) is also recorded. The ledger supports any number of events.

### 6.11 Wait does not record XP

`Wait()` has no roll and awards no XP. The ledger is unchanged after a Wait action.

### 6.12 Very long session

XP accumulates indefinitely. There is no cap on `TotalXp`. `LevelTable.GetLevel()` handles arbitrarily large XP values (returns L11 for any XP ≥ 3500).

---

## 7. Error Conditions

### 7.1 XpLedger.Record with invalid amount

```csharp
ledger.Record(0, "test");   // throws ArgumentOutOfRangeException
ledger.Record(-5, "test");  // throws ArgumentOutOfRangeException
```

### 7.2 XpLedger.Record with null source

```csharp
ledger.Record(10, null);    // throws ArgumentNullException
```

### 7.3 GameSession already ended

If `ResolveTurnAsync`, `ReadAsync`, `RecoverAsync`, or `StartTurnAsync` is called after the game has ended, `GameEndedException` is thrown (existing behavior). No XP is recorded for the invalid call.

### 7.4 RecoverAsync with no active trap

If `RecoverAsync` is called when `TrapState.HasActive` is false, it should throw `InvalidOperationException` (defined by #43 spec). No XP is recorded.

---

## 8. Order of Operations in ResolveTurnAsync

XP recording fits into the existing `ResolveTurnAsync` flow at this position:

```
1. Validate option index
2. Compute externalBonus (callback + tell + triple combo)
3. Compute dcAdjustment (weakness window)
4. RollEngine.Resolve() → RollResult
5. Compute interest delta (SuccessScale/FailureScale + RiskTierBonus + momentum + combo bonus)
6. Shadow growth events (#44)
7. *** XP recording (#48) ← HERE ***
8. InterestMeter.Apply(total delta)
9. Check game end conditions
10. Record game-end XP if applicable
11. LLM calls (DeliverMessage, GetOpponentResponse, etc.)
12. Construct and return TurnResult (with xpEarned from step 7)
```

XP is recorded **after** shadow growth and **before** interest application, so the roll result and DC are finalized. Game-end XP is recorded after interest application reveals the end condition.

---

## 9. XP Recording Logic (pseudocode for RecordTurnXp)

```
function RecordTurnXp(roll: RollResult) -> int:
    if roll.IsNatTwenty:
        _xpLedger.Record(25, "Nat 20")
        return 25
    if roll.IsNatOne:
        _xpLedger.Record(10, "Nat 1")
        return 10
    if roll.IsSuccess:
        if roll.DC <= 13:
            _xpLedger.Record(5, "Success (easy)")
            return 5
        if roll.DC <= 17:
            _xpLedger.Record(10, "Success (medium)")
            return 10
        _xpLedger.Record(15, "Success (hard)")
        return 15
    else:
        _xpLedger.Record(2, "Failed check")
        return 2
```

For ReadAsync:
```
function RecordReadXp(roll: RollResult) -> int:
    if roll.IsNatTwenty:
        _xpLedger.Record(25, "Nat 20")
        return 25
    if roll.IsNatOne:
        _xpLedger.Record(10, "Nat 1")
        return 10
    if roll.IsSuccess:
        _xpLedger.Record(5, "Read success")
        return 5
    else:
        _xpLedger.Record(2, "Failed check")
        return 2
```

For RecoverAsync:
```
function RecordRecoverXp(roll: RollResult) -> int:
    if roll.IsNatTwenty:
        _xpLedger.Record(25, "Nat 20")
        return 25
    if roll.IsNatOne:
        _xpLedger.Record(10, "Nat 1")
        return 10
    if roll.IsSuccess:
        _xpLedger.Record(15, "Trap recovery")
        return 15
    else:
        _xpLedger.Record(2, "Failed check")
        return 2
```

---

## 10. Standard Source Labels

These exact strings must be used as the `source` parameter to `Record()` for consistency across the codebase:

| Label | Used by |
|-------|---------|
| `"Success (easy)"` | ResolveTurnAsync, DC ≤ 13 |
| `"Success (medium)"` | ResolveTurnAsync, DC 14–17 |
| `"Success (hard)"` | ResolveTurnAsync, DC ≥ 18 |
| `"Failed check"` | ResolveTurnAsync, ReadAsync, RecoverAsync on failure |
| `"Nat 20"` | Any roll with IsNatTwenty |
| `"Nat 1"` | Any roll with IsNatOne |
| `"Date secured"` | Game end with DateSecured |
| `"Trap recovery"` | RecoverAsync on success |
| `"Conversation complete"` | Game end with Unmatched or Ghosted |
| `"Read success"` | ReadAsync on success |

---

## 11. Dependencies

| Dependency | What's needed | Status |
|------------|---------------|--------|
| #42 (Wave 0 — RollEngine) | `RollEngine.ResolveFixedDC()`, `externalBonus`/`dcAdjustment` params on `Resolve()` | Required — XP DC tier reads `RollResult.DC` |
| #43 (Read/Recover/Wait) | `ReadAsync()`, `RecoverAsync()`, `Wait()` methods on GameSession | Required — XP recording hooks into these methods |
| #44 (Shadow growth) | Shadow growth events in ResolveTurnAsync | Co-sprint — XP recording comes after shadow growth in execution order |
| `LevelTable` (existing) | `GetLevel(int xp)` — maps accumulated XP to level | Already implemented |
| `RollResult` (existing) | `DC`, `IsSuccess`, `IsNatTwenty`, `IsNatOne` properties | Already implemented |
| `TurnResult` (existing) | `XpEarned` constructor parameter | Already implemented (PR #117) |
| `GameSession` (existing) | `ResolveTurnAsync`, `StartTurnAsync`, game-end logic | Already implemented |

---

## 12. Cross-Spec Notes

This spec was reviewed and approved in PR #108. The previous review (PR #107) flagged a cross-spec conflict between issues #49/#50 regarding `OpponentResponse` shape — that conflict does **not** affect issue #48. The XP tracking system is self-contained and reads only from `RollResult` properties that are stable across all co-sprint changes.

The `RecordTurnXp` helper can be reused by any action that produces a `RollResult`, making it straightforward to add XP recording to Read/Recover without duplicating logic. However, since Read and Recover have distinct success labels ("Read success" / "Trap recovery" vs. the DC-tiered labels), separate recording functions or a parameter for the success label are needed.
