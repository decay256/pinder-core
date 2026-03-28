# Spec: ConversationRegistry — Multi-Session Manager with Fast-Forward and Cross-Chat Shadow Bleed

**Issue:** #56  
**Component:** `Pinder.Core.Conversation`  
**Maturity:** Prototype  
**Depends on:** #53 (OpponentTimingCalculator), #54 (GameClock + IGameClock), #44 (Shadow growth events)

---

## 1. Overview

`ConversationRegistry` is the top-level scheduler for the Pinder async conversation system. It manages multiple simultaneous `GameSession` instances, each wrapped in a `ConversationEntry` that tracks when the opponent's next reply is due and whether the conversation is still active. The registry provides three core operations: scheduling an opponent reply after a player turn, fast-forwarding the game clock to the next pending reply (while checking for ghost/fizzle/decay side-effects on all other sessions), and propagating cross-chat shadow bleed events that modify shadow stats across all active sessions.

The registry does **not** make LLM calls. It is a scheduler and event propagator. `GameSession` remains the executor of actual turns.

---

## 2. New Types

### 2.1 `ConversationLifecycle` Enum

**Namespace:** `Pinder.Core.Conversation`

```
public enum ConversationLifecycle
{
    Active,
    Paused,
    Ghosted,
    Fizzled,
    DateSecured,
    Unmatched
}
```

Terminal states: `Ghosted`, `Fizzled`, `DateSecured`, `Unmatched`. Once a conversation enters a terminal state it cannot return to `Active`.

### 2.2 `CrossChatEvent` Enum

**Namespace:** `Pinder.Core.Conversation`

```
public enum CrossChatEvent
{
    DateSecured,
    Unmatched,
    Nat1Catastrophe,
    ThreeDeadToday,
    DoubleDateToday
}
```

### 2.3 `ConversationEntry` (sealed class)

**Namespace:** `Pinder.Core.Conversation`

Must be a `sealed class` (not a `record` — C# 8.0 / netstandard2.0 constraint).

#### Constructor

```csharp
public ConversationEntry(
    GameSession session,
    DateTimeOffset? pendingReplyAt = null,
    ConversationLifecycle status = ConversationLifecycle.Active)
```

- `session` — the `GameSession` this entry wraps. Must not be null; throw `ArgumentNullException` if null.
- `pendingReplyAt` — nullable. When non-null, the game-clock timestamp at which the opponent's reply is expected to arrive.
- `status` — defaults to `Active`.

#### Properties

| Property | Type | Mutable | Description |
|----------|------|---------|-------------|
| `Session` | `GameSession` | No (get-only) | The wrapped game session |
| `PendingReplyAt` | `DateTimeOffset?` | Yes (get/set) | Timestamp of next expected opponent reply, or null if none pending |
| `Status` | `ConversationLifecycle` | Yes (get/set) | Current lifecycle state of this conversation |

### 2.4 `ConversationRegistry` (sealed class)

**Namespace:** `Pinder.Core.Conversation`

#### Constructor

```csharp
public ConversationRegistry(IGameClock clock)
```

- `clock` — an `IGameClock` instance (defined by issue #54). Must not be null; throw `ArgumentNullException` if null.
- Uses the injected clock interface, **not** concrete `GameClock`, per vision concern #67.

---

## 3. Function Signatures

All methods below are on `ConversationRegistry`.

### 3.1 `Add(ConversationEntry entry) → void`

Adds a conversation entry to the registry's internal collection.

```csharp
public void Add(ConversationEntry entry)
```

- Throws `ArgumentNullException` if `entry` is null.
- If an entry with the same `Session` reference already exists, throw `InvalidOperationException` with message `"Session already registered."`.

### 3.2 `ScheduleOpponentReply(GameSession session, double delayMinutes) → void`

After a player turn completes, the caller invokes this to register when the opponent will reply.

```csharp
public void ScheduleOpponentReply(GameSession session, double delayMinutes)
```

- Looks up the `ConversationEntry` whose `Session` matches the provided `session` (reference equality).
- Sets `PendingReplyAt` to `clock.Now + TimeSpan.FromMinutes(delayMinutes)`.
- Throws `ArgumentNullException` if `session` is null.
- Throws `InvalidOperationException` with message `"Session not found in registry."` if no matching entry exists.
- Throws `ArgumentOutOfRangeException` if `delayMinutes` is negative or zero.
- Only operates on entries with `Status == Active`. If the entry's status is not `Active`, throw `InvalidOperationException` with message `"Cannot schedule reply for non-active conversation."`.

### 3.3 `FastForward() → ConversationEntry?`

Advances the game clock to the earliest pending reply and processes side-effects on all other conversations.

```csharp
public ConversationEntry? FastForward()
```

**Algorithm:**

1. **Find earliest reply.** Among all entries where `Status == Active` and `PendingReplyAt != null`, find the one with the smallest `PendingReplyAt` value. If none found, return `null`.

2. **Advance clock.** Call `clock.AdvanceTo(earliest.PendingReplyAt.Value)` to move the game clock forward.

3. **Clear the delivered entry's pending timestamp.** Set `earliest.PendingReplyAt = null`.

4. **Check ghost triggers on all OTHER active entries.** For each entry where `Status == Active` and the entry is not the one being delivered:
   - Compute silence duration: `clock.Now - entry.PendingReplyAt` (or use the last interaction time if `PendingReplyAt` is null — see Edge Cases).
   - **Ghost condition:** The entry's session has interest ≤ 4 (i.e., `entry.Session` interest meter `Current <= 4`) AND silence ≥ 24 hours (1440 minutes of game time).
     - Set `entry.Status = ConversationLifecycle.Ghosted`.
     - Apply Dread +1 **globally** — increment `ShadowStatType.Dread` by 1 on all active sessions' shadow trackers (see §5 on shadow modification).
   - **Fizzle condition** (checked only if ghost did not trigger): Interest is 5–9 (`Current >= 5 && Current <= 9`) AND silence ≥ 24 hours.
     - Set `entry.Status = ConversationLifecycle.Fizzled`.
     - No shadow penalty.

5. **Apply interest decay on paused/idle conversations.** For each entry where `Status == Active` and the entry is not the one being delivered:
   - Compute days of silence (as a whole number, floored): `floor(silenceDuration.TotalDays)`.
   - Apply `InterestMeter.Apply(-1 * daysOfSilence)` — i.e., −1 interest per full day of silence.
   - Interest decay is applied **after** ghost/fizzle checks (a conversation that was just ghosted or fizzled does not also receive decay).

6. **Energy gate.** Call `clock.ConsumeEnergy()`. If it returns `false`, return `null` (the turn cannot proceed due to energy depletion). The delivered entry's `PendingReplyAt` remains cleared — the reply arrived but the player lacks energy to act on it.
   - **Clarification (per VC-75):** `ConversationRegistry` does NOT own energy tracking. It delegates to `IGameClock.ConsumeEnergy()`.

7. **Return** the `ConversationEntry` whose reply was delivered (the earliest one found in step 1).

### 3.4 `ApplyCrossChatEvent(CrossChatEvent chatEvent) → void`

Propagates a cross-chat event to all active sessions' shadow stats.

```csharp
public void ApplyCrossChatEvent(CrossChatEvent chatEvent)
```

**Event effects:**

| Event | Shadow Effect | Scope |
|-------|--------------|-------|
| `DateSecured` | +1 to all rolls in other active chats for 1 hour of game time | All active sessions except the one that secured the date |
| `Unmatched` | Dread +1 | All active sessions (globally) |
| `Nat1Catastrophe` | Madness +1 | The **next** conversation the player enters (or the next active one in the registry) |
| `ThreeDeadToday` | Dread +3, Madness +1 | All active sessions (globally) |
| `DoubleDateToday` | Overthinking +2 | All active sessions (globally) |

**Notes on `DateSecured` roll bonus:**
- This is a temporary effect: +1 to all rolls for 1 hour of game time. The registry must track the expiry time (`clock.Now + 1 hour`). How this bonus is surfaced to `GameSession` during rolls is an integration point — the registry should expose a method or property (e.g., `GetActiveRollBonus(GameSession session) → int`) that `GameSession` can query, or the bonus should be stored on the `ConversationEntry`. The exact mechanism is left to the implementer, but the spec requires the bonus to expire after 1 hour of game time.

**Notes on `Nat1Catastrophe` bleed:**
- Madness +1 applies to the **next** conversation only — not all active ones. The registry must track that one pending Madness bleed is outstanding. When the next `ScheduleOpponentReply` or `FastForward` targets a different session, apply the +1 Madness to that session's shadow tracker and clear the pending bleed.

### 3.5 `GetActiveEntries() → IReadOnlyList<ConversationEntry>`

Returns all entries currently in the registry (all statuses).

```csharp
public IReadOnlyList<ConversationEntry> GetActiveEntries()
```

### 3.6 `GetByStatus(ConversationLifecycle status) → IReadOnlyList<ConversationEntry>`

Returns entries filtered by lifecycle status.

```csharp
public IReadOnlyList<ConversationEntry> GetByStatus(ConversationLifecycle status)
```

---

## 4. Input/Output Examples

### Example 1: Basic Fast-Forward

**Setup:**
- Registry has 2 entries: Session A (pending reply at T+30min), Session B (pending reply at T+60min).
- Clock is at T+0.

**Call:** `registry.FastForward()`

**Result:**
- Clock advances to T+30min.
- Session A's `PendingReplyAt` is cleared to `null`.
- Session B is checked for ghost/fizzle (silence = 30min, neither triggers since < 24h).
- Session B interest decay: 0 days of silence → no decay.
- Returns Session A's `ConversationEntry`.

### Example 2: Ghost Trigger

**Setup:**
- Registry has 2 entries: Session A (pending reply at T+26h), Session B (no pending reply, interest = 3, last interaction was at T+0).
- Clock is at T+0.

**Call:** `registry.FastForward()`

**Result:**
- Clock advances to T+26h.
- Session A is delivered (PendingReplyAt cleared).
- Session B: interest = 3 (≤ 4), silence = 26h (≥ 24h) → **Ghosted**.
- Session B status set to `ConversationLifecycle.Ghosted`.
- Dread +1 applied globally to all active sessions' shadow trackers.

### Example 3: Cross-Chat Event — ThreeDeadToday

**Setup:**
- Registry has 3 active sessions: A, B, C.

**Call:** `registry.ApplyCrossChatEvent(CrossChatEvent.ThreeDeadToday)`

**Result:**
- Dread +3 on sessions A, B, and C shadow trackers.
- Madness +1 on sessions A, B, and C shadow trackers.

### Example 4: Interest Decay

**Setup:**
- Registry has 2 entries: Session A (pending reply at T+72h), Session B (active, last interaction at T+0, interest = 12).
- Clock at T+0.

**Call:** `registry.FastForward()`

**Result:**
- Clock advances to T+72h.
- Session A delivered.
- Session B silence = 72h = 3 full days → interest decay of −3. Interest goes from 12 to 9.

### Example 5: Energy Depletion

**Setup:**
- Registry has Session A (pending reply at T+30min). Clock at T+0. `clock.ConsumeEnergy()` returns `false`.

**Call:** `registry.FastForward()`

**Result:**
- Clock advances to T+30min. Session A's PendingReplyAt is cleared.
- `clock.ConsumeEnergy()` returns `false` → method returns `null`.
- The reply arrived but the player can't act. The caller must wait for energy to replenish before starting the turn.

---

## 5. Acceptance Criteria

### AC1: `ConversationEntry` is a `sealed class`, NOT a `record`
- `ConversationEntry` must use `sealed class` with explicit constructor, get-only `Session`, and get/set `PendingReplyAt` and `Status`.
- Must compile under netstandard2.0 with LangVersion 8.0.

### AC2: `ConversationRegistry` has `ScheduleOpponentReply`, `FastForward`, `ApplyCrossChatEvent`
- All three methods exist with the signatures specified in §3.
- `ConsumeEnergy` is NOT a method on `ConversationRegistry` — energy gating is delegated to `IGameClock.ConsumeEnergy()` inside `FastForward()` per VC-75.

### AC3: `ConversationRegistry` accepts `IGameClock` (not concrete `GameClock`) per #67
- Constructor parameter type is `IGameClock`.
- No direct reference to concrete `GameClock` class.

### AC4: `FastForward` advances clock to earliest pending reply
- Among all active entries with non-null `PendingReplyAt`, the one with the smallest timestamp is selected.
- `clock.AdvanceTo()` is called with that timestamp.
- The entry's `PendingReplyAt` is set to `null`.

### AC5: Ghost trigger fires correctly (Interest ≤ 4 + 24h silence → Dread +1 globally)
- During `FastForward`, any non-delivered active entry with interest ≤ 4 and ≥ 24 hours of silence has its status set to `Ghosted`.
- Dread +1 is applied to shadow trackers on ALL active sessions (including the one just delivered).

### AC6: Fizzle fires correctly (Interest 5–9 + 24h silence → archive)
- During `FastForward`, any non-delivered active entry with interest 5–9 and ≥ 24 hours of silence has its status set to `Fizzled`.
- No shadow penalty is applied for fizzle.
- Ghost check takes priority over fizzle (if interest ≤ 4 and 24h silence, it's a ghost, not a fizzle).

### AC7: Interest decay −1/day on paused conversations
- During `FastForward`, each non-delivered, still-active entry (not ghosted/fizzled in this pass) receives −1 interest per full day of silence.
- Decay is applied via `InterestMeter.Apply()` which handles clamping to [0, 25].

### AC8: All 5 cross-chat events propagate correctly
- `DateSecured`: +1 roll bonus to other active sessions for 1 hour game time.
- `Unmatched`: Dread +1 globally.
- `Nat1Catastrophe`: Madness +1 bleeds to the **next** session only.
- `ThreeDeadToday`: Dread +3 + Madness +1 globally.
- `DoubleDateToday`: Overthinking +2 globally.

### AC9: Energy system gates new turns
- `FastForward` calls `clock.ConsumeEnergy()` after processing the pending reply.
- If `ConsumeEnergy()` returns `false`, `FastForward` returns `null` — the reply arrived but the player cannot act.

### AC10: Build clean
- `dotnet build` succeeds with zero errors and zero warnings on netstandard2.0.

---

## 6. Edge Cases

### 6.1 Empty Registry
- `FastForward()` on an empty registry (no entries) returns `null`.
- `ApplyCrossChatEvent()` on an empty registry is a no-op (no error).

### 6.2 No Pending Replies
- `FastForward()` when all entries have `PendingReplyAt == null` returns `null`. Clock does not advance.

### 6.3 All Conversations in Terminal State
- `FastForward()` when all entries are in terminal states (`Ghosted`, `Fizzled`, `DateSecured`, `Unmatched`) returns `null`.

### 6.4 Simultaneous Pending Replies (Tie-Breaking)
- If two entries have the exact same `PendingReplyAt`, the one added first (insertion order) is delivered. The other receives ghost/fizzle/decay checks as normal.

### 6.5 Interest Decay Drives Ghost/Fizzle
- Interest decay is applied **after** ghost/fizzle checks in a single `FastForward` call. This means decay cannot trigger a ghost in the same call — ghost is checked against the pre-decay interest value.
- However, on the **next** `FastForward` call, the decayed interest may now meet ghost conditions.

### 6.6 Multiple Ghosts in One FastForward
- If multiple conversations meet ghost conditions simultaneously during one `FastForward` call, each one triggers Dread +1 independently. Two ghosts in one call = Dread +2 globally.

### 6.7 Nat1Catastrophe Bleed with No Other Sessions
- If `Nat1Catastrophe` is applied and there is only one session (the one that rolled the Nat 1), the pending Madness bleed is held until a new session is added and interacted with.

### 6.8 Negative or Zero delayMinutes
- `ScheduleOpponentReply` with `delayMinutes <= 0` throws `ArgumentOutOfRangeException`.

### 6.9 Scheduling on a Non-Active Conversation
- `ScheduleOpponentReply` on an entry whose status is not `Active` throws `InvalidOperationException`.

### 6.10 Interest at 0 After Decay
- If interest decays to 0, the session's `InterestMeter.GetState()` returns `Unmatched`. This is distinct from ghosting — the session is `Unmatched` (interest hit 0), not `Ghosted` (silence-based). The caller may fire `ApplyCrossChatEvent(CrossChatEvent.Unmatched)` separately; the registry does not auto-fire cross-chat events from decay.

### 6.11 DateSecured Roll Bonus Expiry
- The +1 roll bonus from `DateSecured` must expire after 1 hour of game time. If the clock advances past the expiry, the bonus is no longer returned.

---

## 7. Error Conditions

| Condition | Exception Type | Message |
|-----------|---------------|---------|
| `ConversationRegistry(null)` | `ArgumentNullException` | `"clock"` |
| `Add(null)` | `ArgumentNullException` | `"entry"` |
| `Add(duplicate session)` | `InvalidOperationException` | `"Session already registered."` |
| `ScheduleOpponentReply(null, ...)` | `ArgumentNullException` | `"session"` |
| `ScheduleOpponentReply(unknown, ...)` | `InvalidOperationException` | `"Session not found in registry."` |
| `ScheduleOpponentReply(session, -5)` | `ArgumentOutOfRangeException` | `"delayMinutes"` |
| `ScheduleOpponentReply(non-active, ...)` | `InvalidOperationException` | `"Cannot schedule reply for non-active conversation."` |
| `ConversationEntry(null, ...)` | `ArgumentNullException` | `"session"` |

---

## 8. Dependencies

| Dependency | Issue | What It Provides |
|------------|-------|-----------------|
| `IGameClock` | #54 | `Now` property (`DateTimeOffset`), `AdvanceTo(DateTimeOffset)` method, `ConsumeEnergy()` method |
| `GameSession` | #27 (existing) | The conversation executor; owns `InterestMeter`, `TrapState`, shadow tracking |
| `InterestMeter` | #6 (existing) | `Current` (int), `Apply(int delta)`, `GetState()` → `InterestState` |
| `OpponentTimingCalculator` | #53 | Computes `delayMinutes` from `TimingProfile` + interest + shadows (caller passes result to `ScheduleOpponentReply`) |
| Shadow growth (#44) | #44 | Provides mutable shadow tracking on `GameSession` that cross-chat events can modify |
| `ShadowStatType` | Existing | Enum: `Madness`, `Horniness`, `Denial`, `Fixation`, `Dread`, `Overthinking` |

### Integration Note: Shadow Modification

The issue specifies that cross-chat events modify shadow stats globally across sessions. Currently, `StatBlock` is immutable (`readonly` dictionaries). Issue #44 (shadow growth events) must provide a mutable shadow tracking mechanism on `GameSession` (e.g., a `SessionShadowTracker`) that `ConversationRegistry` can call to increment shadow values. The exact API for incrementing shadows is defined by #44. `ConversationRegistry` needs a method like `session.AddShadow(ShadowStatType, int amount)` or equivalent.

If #44's shadow tracker is not yet implemented, the implementer should define the minimum interface needed and document the integration contract.

---

## 9. Non-Functional Notes

- **Zero NuGet dependencies.** All code must compile under netstandard2.0 with no external packages.
- **C# 8.0 only.** No `record` types, no `init` properties. Use `sealed class` with constructors.
- **Thread safety is NOT required** for prototype maturity. The registry assumes single-threaded access from the game loop.
- **The registry does NOT make LLM calls.** It is a pure scheduler/propagator. `GameSession` handles LLM interaction.
