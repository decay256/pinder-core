# Spec: Issue #56 — ConversationRegistry

## 1. Overview

`ConversationRegistry` is the multi-session scheduler for Pinder.Core. It manages a collection of active conversations (each backed by a `GameSession`), determines when opponent replies arrive, advances the game clock to deliver them, and propagates cross-chat shadow events (ghost triggers, fizzle, interest decay, and shadow bleed). The registry **does not** make LLM calls or execute turns — it schedules time and applies side-effects; the host is responsible for calling `GameSession` methods to execute actual turns.

This component lives in `Pinder.Core.Conversation` and depends on `IGameClock` (issue #54), `SessionShadowTracker` (issue #130 / Wave 0), and shadow growth mechanics (issue #44).

---

## 2. Type Definitions

### 2.1 `ConversationLifecycle` (enum)

**File:** `src/Pinder.Core/Conversation/ConversationLifecycle.cs`

```csharp
namespace Pinder.Core.Conversation
{
    public enum ConversationLifecycle
    {
        Active,
        Paused,
        Ghosted,
        Fizzled,
        DateSecured,
        Unmatched
    }
}
```

- `Active` — conversation is ongoing; eligible for FastForward, decay, ghost, fizzle checks.
- `Paused` — temporarily suspended; excluded from scheduling but retained in registry.
- `Ghosted` — opponent ghosted (Interest ≤ 4 + 24h silence). Terminal state.
- `Fizzled` — conversation died naturally (Interest 5–9 + 24h silence). Terminal state.
- `DateSecured` — player secured a date (Interest hit 25). Terminal state.
- `Unmatched` — conversation ended at Interest 0. Terminal state.

### 2.2 `CrossChatEvent` (enum)

**File:** `src/Pinder.Core/Conversation/CrossChatEvent.cs`

```csharp
namespace Pinder.Core.Conversation
{
    public enum CrossChatEvent
    {
        DateSecured,
        Unmatched,
        Nat1Catastrophe,
        ThreeDeadToday,
        DoubleDateToday
    }
}
```

### 2.3 `ConversationEntry` (sealed class)

**File:** `src/Pinder.Core/Conversation/ConversationEntry.cs`

```csharp
namespace Pinder.Core.Conversation
{
    public sealed class ConversationEntry
    {
        public GameSession Session { get; }
        public DateTimeOffset? PendingReplyAt { get; set; }
        public DateTimeOffset LastInteractionAt { get; set; }
        public ConversationLifecycle Status { get; set; }

        public ConversationEntry(
            GameSession session,
            DateTimeOffset lastInteractionAt,
            DateTimeOffset? pendingReplyAt = null,
            ConversationLifecycle status = ConversationLifecycle.Active);
    }
}
```

**Properties:**

| Property | Type | Description |
|---|---|---|
| `Session` | `GameSession` | The game session this entry wraps. Set at construction, never null. |
| `PendingReplyAt` | `DateTimeOffset?` | When the opponent's reply is expected. `null` means no pending reply (player's turn or idle). |
| `LastInteractionAt` | `DateTimeOffset` | Timestamp of the most recent interaction (player or opponent). Used for silence duration calculations. Updated by `ScheduleOpponentReply` and by `FastForward` when a reply is delivered. |
| `Status` | `ConversationLifecycle` | Current lifecycle state. Mutable — changed by FastForward (ghost/fizzle) or by host. |

**Constructor behavior:**
- Throws `ArgumentNullException` if `session` is null.
- `lastInteractionAt` is required — there is no sensible default for silence calculations.
- `pendingReplyAt` defaults to `null`.
- `status` defaults to `ConversationLifecycle.Active`.

---

## 3. Function Signatures

### 3.1 `ConversationRegistry`

**File:** `src/Pinder.Core/Conversation/ConversationRegistry.cs`  
**Namespace:** `Pinder.Core.Conversation`

```csharp
public sealed class ConversationRegistry
{
    // --- Constructor ---

    /// <param name="clock">Injectable game clock (IGameClock). Must not be null.</param>
    public ConversationRegistry(IGameClock clock);

    // --- Properties ---

    /// <summary>All registered entries (read-only view).</summary>
    public IReadOnlyList<ConversationEntry> Entries { get; }

    // --- Methods ---

    /// <summary>Add a conversation entry to the registry.</summary>
    /// <param name="entry">Must not be null. Must not already be registered (same Session reference).</param>
    public void Register(ConversationEntry entry);

    /// <summary>Schedule when the opponent will reply to the given session.</summary>
    /// <param name="session">Must be a registered session.</param>
    /// <param name="delayMinutes">Minutes from clock.Now until reply arrives. Must be > 0.</param>
    public void ScheduleOpponentReply(GameSession session, double delayMinutes);

    /// <summary>
    /// Advance the clock to the next pending reply. Applies ghost, fizzle, and decay
    /// checks on all other conversations. Returns the entry whose reply arrived.
    /// Returns null if no entries have a pending reply.
    /// </summary>
    public ConversationEntry? FastForward();

    /// <summary>Apply a cross-chat event to all active sessions in the registry.</summary>
    public void ApplyCrossChatEvent(CrossChatEvent evt);

    /// <summary>
    /// Pass-through to IGameClock.ConsumeEnergy(amount).
    /// Returns true if energy was consumed, false if insufficient.
    /// </summary>
    public bool ConsumeEnergy(int amount);

    /// <summary>Return all entries matching the given status.</summary>
    public IReadOnlyList<ConversationEntry> GetByStatus(ConversationLifecycle status);

    /// <summary>Return all entries (alias for Entries property — included for query symmetry).</summary>
    public IReadOnlyList<ConversationEntry> GetAllEntries();
}
```

---

## 4. Input/Output Examples

### Example 1: Basic FastForward

**Setup:**
- Clock starts at `2026-01-15T14:00:00+00:00`
- Entry A: `PendingReplyAt = 2026-01-15T14:30:00`, Interest = 12, Status = Active, LastInteractionAt = 2026-01-15T14:00:00
- Entry B: `PendingReplyAt = 2026-01-15T15:00:00`, Interest = 8, Status = Active, LastInteractionAt = 2026-01-15T14:00:00

**Call:** `registry.FastForward()`

**Result:**
- Clock advances to `2026-01-15T14:30:00`
- Returns Entry A (earliest pending reply)
- Entry A: `PendingReplyAt` set to `null`, `LastInteractionAt` updated to `2026-01-15T14:30:00`
- Entry B: unchanged (only 30 min silence, no decay/ghost/fizzle)

### Example 2: Ghost Trigger

**Setup:**
- Clock at `2026-01-15T14:00:00`
- Entry A: `PendingReplyAt = 2026-01-15T14:05:00`, Interest = 15, Active, LastInteractionAt = 2026-01-15T14:00:00
- Entry B: `PendingReplyAt = null`, Interest = 3 (Bored), Active, LastInteractionAt = 2026-01-13T12:00:00 (25.5h silence at time of FastForward)

**Call:** `registry.FastForward()`

**Result:**
- Clock advances to `2026-01-15T14:05:00`
- Returns Entry A
- Entry B: silence = 25h 5min ≥ 24h AND Interest ≤ 4 → **Ghosted**
  - `Status = ConversationLifecycle.Ghosted`
  - Dread +1 applied globally to all sessions' player `SessionShadowTracker` (via `ApplyGrowth(ShadowStatType.Dread, 1, "Ghosted in another chat")`)

### Example 3: Fizzle

**Setup:**
- Clock at `2026-01-16T10:00:00`
- Entry A: `PendingReplyAt = 2026-01-16T10:10:00`, Interest = 18, Active, LastInteractionAt = 2026-01-16T10:00:00
- Entry B: `PendingReplyAt = null`, Interest = 7, Active, LastInteractionAt = 2026-01-15T08:00:00 (26h silence)

**Call:** `registry.FastForward()`

**Result:**
- Clock advances to `2026-01-16T10:10:00`
- Returns Entry A
- Entry B: silence ≥ 24h AND Interest 5–9 → **Fizzled**
  - `Status = ConversationLifecycle.Fizzled`
  - No shadow penalty applied

### Example 4: Interest Decay on Paused Conversations

**Setup:**
- Clock at `2026-01-15T12:00:00`
- Entry A: `PendingReplyAt = 2026-01-17T12:00:00`, Interest = 14, Active, LastInteractionAt = 2026-01-15T12:00:00
- Entry B: `PendingReplyAt = null`, Interest = 12, Active, LastInteractionAt = 2026-01-15T10:00:00

**Call:** `registry.FastForward()`

**Result:**
- Clock advances to `2026-01-17T12:00:00` (2 days forward)
- Returns Entry A, `LastInteractionAt` updated to `2026-01-17T12:00:00`
- Entry B: silence = 50h → 2 full days → Interest decay of −2 → Interest goes from 12 to 10
  - Note: ghost/fizzle checks run before decay. Interest 12 is not in Bored (≤4) or Fizzle (5–9) range, so neither triggers. Decay is then applied.

### Example 5: Cross-Chat Event — Unmatched

**Setup:**
- 3 entries, all Active, each with a `GameSessionConfig` containing a `SessionShadowTracker`.

**Call:** `registry.ApplyCrossChatEvent(CrossChatEvent.Unmatched)`

**Result:**
- On every session's player `SessionShadowTracker`: `ApplyGrowth(ShadowStatType.Dread, 1, "Unmatched in another chat")`
- If a session has no `SessionShadowTracker` in its config, the shadow effect is a no-op for that session.

### Example 6: ConsumeEnergy Pass-Through

**Call:** `registry.ConsumeEnergy(1)`

**Result:** Returns `_clock.ConsumeEnergy(1)`. If the clock has ≥1 remaining energy, returns `true` and deducts. Otherwise returns `false` with no deduction.

---

## 5. Acceptance Criteria

### AC-1: ConversationEntry is a sealed class, NOT a record
`ConversationEntry` must be declared as `public sealed class` per netstandard2.0 / C# 8.0 constraint. It must have the properties `Session`, `PendingReplyAt`, `LastInteractionAt`, and `Status` as defined in §2.3.

### AC-2: ConversationRegistry with ScheduleOpponentReply, FastForward, ApplyCrossChatEvent, ConsumeEnergy
All four methods must exist with the signatures defined in §3.1. Additionally `Register`, `GetByStatus`, `GetAllEntries`, and the `Entries` property must be present.

### AC-3: ConversationRegistry accepts IGameClock (not concrete GameClock)
The constructor parameter type must be `IGameClock` (the interface from `Pinder.Core.Interfaces`), not the concrete `GameClock` class. This ensures deterministic testing via `FixedGameClock`.

### AC-4: ConsumeEnergy delegates to IGameClock.ConsumeEnergy()
`ConversationRegistry.ConsumeEnergy(int amount)` must call `_clock.ConsumeEnergy(amount)` and return its result. The registry must NOT maintain any energy counter or state of its own.

### AC-5: FastForward advances clock to earliest pending reply
`FastForward` must find the `ConversationEntry` with the smallest `PendingReplyAt` value among entries where `Status == Active` and `PendingReplyAt != null`, then call `_clock.AdvanceTo(entry.PendingReplyAt.Value)`. After delivery, set `entry.PendingReplyAt = null` and update `entry.LastInteractionAt` to the delivered time.

### AC-6: Ghost trigger fires correctly
During `FastForward`, for each OTHER active entry (not the one being delivered): if `entry.Session.Interest.GetState()` returns `InterestState.Bored` (Interest ≤ 4) **or** `InterestState.Unmatched` (Interest = 0), AND silence duration (`_clock.Now - entry.LastInteractionAt`) ≥ 24 hours, then:
- Set `entry.Status = ConversationLifecycle.Ghosted`
- Apply `Dread +1` globally to all sessions' player `SessionShadowTracker` via `ApplyGrowth(ShadowStatType.Dread, 1, "Ghosted in another chat")`

### AC-7: Fizzle fires correctly
During `FastForward`, for each OTHER active entry: if Interest is 5–9 (i.e., `InterestState.Interested` but `Current <= 9`) AND silence ≥ 24h, then:
- Set `entry.Status = ConversationLifecycle.Fizzled`
- No shadow penalty. No other side-effects.

### AC-8: Interest decay −1/day on paused conversations
During `FastForward`, for each active entry that is NOT the one being delivered and was NOT ghosted/fizzled in this FastForward call: compute silence in full days (`floor((clock.Now - entry.LastInteractionAt).TotalDays)`). Apply `entry.Session.Interest.Apply(-fullDays)` if `fullDays >= 1`. Decay is applied **after** ghost/fizzle checks (ghost/fizzle use the pre-decay interest value).

### AC-9: All 5 cross-chat events propagate correctly
See §6.2 for the exact effect of each `CrossChatEvent` value. Each effect must apply to the correct scope (all sessions, next session only, etc.) via `SessionShadowTracker.ApplyGrowth()`. If a session lacks a `SessionShadowTracker`, shadow effects are silently skipped for that session.

### AC-10: Energy system gates new turns via IGameClock
`ConsumeEnergy(int amount)` delegates to `_clock.ConsumeEnergy(amount)`. The host calls `registry.ConsumeEnergy(1)` before allowing a player turn; if it returns `false`, the turn is blocked. The registry itself does not enforce turn gating — it provides the query mechanism.

### AC-11: Tests (specification for test agent)
Required test scenarios:
- FastForward returns the correct session (earliest `PendingReplyAt`)
- Ghost trigger: entry with Interest ≤ 4 and 24h+ silence becomes Ghosted, Dread +1 applied
- Cross-chat Dread propagation (Unmatched event applies Dread +1 to all sessions)
- Energy depletion: `ConsumeEnergy` returns false when clock has insufficient energy
- Fizzle: entry with Interest 5–9 and 24h+ silence becomes Fizzled, no shadow penalty
- Interest decay: correct amount based on full days of silence
- `FastForward` returns null when no pending replies exist
- `ScheduleOpponentReply` sets correct `PendingReplyAt` and updates `LastInteractionAt`

### AC-12: Build clean
Solution must compile with zero warnings under netstandard2.0 with nullable reference types enabled.

---

## 6. Detailed Behavior

### 6.1 FastForward Algorithm

```
1. Find entry E where:
     E.Status == Active
     AND E.PendingReplyAt != null
     AND E.PendingReplyAt is the minimum among all such entries
   If no such entry exists, return null.

2. _clock.AdvanceTo(E.PendingReplyAt.Value)

3. For each entry X where X != E AND X.Status == Active:
   a. silenceDuration = _clock.Now - X.LastInteractionAt
   b. interest = X.Session.Interest.Current

   Ghost check:
     If interest <= 4 AND silenceDuration >= 24 hours:
       X.Status = Ghosted
       Apply Dread +1 globally (all sessions' player SessionShadowTracker)
       Continue to next entry (skip fizzle/decay for X)

   Fizzle check:
     If interest >= 5 AND interest <= 9 AND silenceDuration >= 24 hours:
       X.Status = Fizzled
       Continue to next entry (skip decay for X)

   Decay check:
     fullDays = floor(silenceDuration.TotalDays)
     If fullDays >= 1:
       X.Session.Interest.Apply(-fullDays)

4. E.PendingReplyAt = null
5. E.LastInteractionAt = _clock.Now
6. Return E
```

**Order of operations matters:** Ghost/fizzle checks happen on pre-decay interest values. An entry that gets ghosted or fizzled does not also receive decay.

### 6.2 Cross-Chat Event Effects

| Event | Scope | Effect |
|---|---|---|
| `DateSecured` | All OTHER active sessions | Set a flag granting +1 to all rolls for 1 game-hour from `_clock.Now`. Implementation note: the registry stores `DateSecuredBonusExpiresAt = _clock.Now + 1 hour`. Consumers (GameSession or host) query this to apply the bonus. |
| `Unmatched` | ALL active sessions | `SessionShadowTracker.ApplyGrowth(ShadowStatType.Dread, 1, "Unmatched in another chat")` on each session's player tracker. |
| `Nat1Catastrophe` | NEXT session to receive a turn only | `SessionShadowTracker.ApplyGrowth(ShadowStatType.Madness, 1, "Catastrophe bleed")`. The registry must track which session is "next" — this is the entry returned by the next `FastForward` call. Implementation: store a pending `Madness +1` that is applied during the next `FastForward` delivery. |
| `ThreeDeadToday` | ALL active sessions | `ApplyGrowth(ShadowStatType.Dread, 3, "3 dead conversations today")` AND `ApplyGrowth(ShadowStatType.Madness, 1, "3 dead conversations today")` on each session's player tracker. |
| `DoubleDateToday` | ALL active sessions | `ApplyGrowth(ShadowStatType.Overthinking, 2, "Double-booked dates")` on each session's player tracker. |

### 6.3 ScheduleOpponentReply Behavior

1. Find the `ConversationEntry` whose `Session` matches the given `session` parameter (reference equality).
2. If not found, throw `InvalidOperationException("Session is not registered")`.
3. If `delayMinutes <= 0`, throw `ArgumentOutOfRangeException(nameof(delayMinutes))`.
4. Set `entry.PendingReplyAt = _clock.Now + TimeSpan.FromMinutes(delayMinutes)`.
5. Set `entry.LastInteractionAt = _clock.Now` (the player just spoke, so this is the latest interaction).

### 6.4 Register Behavior

1. If `entry` is null, throw `ArgumentNullException(nameof(entry))`.
2. If an entry with the same `Session` reference already exists in the collection, throw `InvalidOperationException("Session is already registered")`.
3. Add the entry to the internal collection.

### 6.5 ConsumeEnergy Behavior

1. Call `_clock.ConsumeEnergy(amount)`.
2. Return the boolean result.
3. No additional logic.

### 6.6 GetByStatus Behavior

1. Return a new list containing all entries where `entry.Status == status`.
2. Returns an empty list if none match.

---

## 7. Edge Cases

| Scenario | Expected Behavior |
|---|---|
| `FastForward` with no entries | Returns `null` |
| `FastForward` with entries but none have `PendingReplyAt` | Returns `null` |
| `FastForward` with only non-Active entries having pending replies | Returns `null` (only Active entries are considered) |
| Multiple entries with identical `PendingReplyAt` | Return any one of them (implementation may pick first added). Only one is delivered per call; the others remain pending for the next `FastForward`. |
| Ghost triggers on multiple entries in a single FastForward | Each ghosted entry triggers Dread +1 independently. If 2 entries ghost, Dread +2 total. |
| Interest exactly 0 during ghost check | Interest 0 → Unmatched state, but ghost check uses `interest <= 4`, so yes, it triggers ghost. However, if the session is already at InterestState.Unmatched (0), the host may have already set Status to Unmatched. The registry checks Status == Active, so already-Unmatched entries are skipped. |
| Interest exactly 5 during fizzle check | 5 is in range 5–9 → fizzle triggers if silence ≥ 24h |
| Interest exactly 10 during fizzle check | 10 is NOT in range 5–9 → no fizzle; only decay applies |
| Silence of exactly 24h | ≥ 24h is satisfied → ghost or fizzle triggers (whichever condition matches) |
| Silence of 23h 59m 59s | < 24h → neither ghost nor fizzle triggers |
| Decay with silence < 24h but ≥ 1 day impossible | Decay uses `floor(TotalDays)`, so < 24h = 0 full days = no decay |
| `ScheduleOpponentReply` on unregistered session | Throws `InvalidOperationException` |
| `ScheduleOpponentReply` with `delayMinutes = 0` | Throws `ArgumentOutOfRangeException` |
| `ScheduleOpponentReply` with negative delay | Throws `ArgumentOutOfRangeException` |
| `Register` same session twice | Throws `InvalidOperationException` |
| `Register` null entry | Throws `ArgumentNullException` |
| Null `IGameClock` in constructor | Throws `ArgumentNullException` |
| `ApplyCrossChatEvent` when sessions lack `SessionShadowTracker` | Shadow effects are silently skipped for those sessions. No exception thrown. |
| `Nat1Catastrophe` with no subsequent `FastForward` | The pending Madness +1 remains stored until a `FastForward` eventually delivers it, or is discarded if no future FastForward occurs. |

---

## 8. Error Conditions

| Error | Type | Message | When |
|---|---|---|---|
| Null clock in constructor | `ArgumentNullException` | `"clock"` | `new ConversationRegistry(null)` |
| Null entry in Register | `ArgumentNullException` | `"entry"` | `Register(null)` |
| Duplicate session in Register | `InvalidOperationException` | `"Session is already registered"` | `Register(entry)` where `entry.Session` is already in registry |
| Unregistered session in ScheduleOpponentReply | `InvalidOperationException` | `"Session is not registered"` | `ScheduleOpponentReply(unknownSession, 5.0)` |
| Non-positive delay | `ArgumentOutOfRangeException` | `"delayMinutes"` | `ScheduleOpponentReply(session, 0)` or negative |
| Null session in ConversationEntry constructor | `ArgumentNullException` | `"session"` | `new ConversationEntry(null, ...)` |

---

## 9. Dependencies

| Dependency | Source | What's Used |
|---|---|---|
| `IGameClock` | `Pinder.Core.Interfaces` (issue #54 / #139) | `.Now`, `.AdvanceTo()`, `.ConsumeEnergy()` |
| `GameSession` | `Pinder.Core.Conversation` (issue #27) | `.Interest` property (InterestMeter), referenced via ConversationEntry |
| `InterestMeter` | `Pinder.Core.Conversation` (issue #6) | `.Current` (int), `.Apply(int delta)`, `.GetState()` |
| `SessionShadowTracker` | `Pinder.Core.Stats` (issue #130 / Wave 0) | `.ApplyGrowth(ShadowStatType, int, string)` for cross-chat shadow bleed |
| `ShadowStatType` | `Pinder.Core.Stats` | `Dread`, `Madness`, `Overthinking` enum values |
| `GameSessionConfig` | `Pinder.Core.Conversation` | Access to `PlayerShadowTracker` property to get the session's `SessionShadowTracker` |

**No external NuGet packages.** All dependencies are internal to `Pinder.Core`.
