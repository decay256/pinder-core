# Contract: Issue #56 — ConversationRegistry

## Component
`ConversationRegistry` (Conversation/) — multi-session manager

## Dependencies
- #54: `IGameClock` (injected, for time advancement and energy)
- #44: Shadow growth (cross-chat shadow bleed uses `SessionShadowTracker`)
- #139 Wave 0: `SessionShadowTracker`

---

## ConversationRegistry

**File:** `src/Pinder.Core/Conversation/ConversationRegistry.cs`

```csharp
namespace Pinder.Core.Conversation
{
    public sealed class ConversationRegistry
    {
        /// <param name="clock">Injectable game clock. Required.</param>
        public ConversationRegistry(IGameClock clock);

        /// <summary>Add a conversation to the registry.</summary>
        public void Register(ConversationEntry entry);

        /// <summary>
        /// Schedule when the opponent's reply will arrive.
        /// </summary>
        public void ScheduleOpponentReply(GameSession session, double delayMinutes);

        /// <summary>
        /// Advance clock to the next pending reply. Returns the conversation that received it.
        /// Also checks ghost triggers, fizzle, and interest decay on all other conversations.
        /// </summary>
        public ConversationEntry FastForward();

        /// <summary>
        /// Apply a cross-chat event to all sessions in the registry.
        /// </summary>
        public void ApplyCrossChatEvent(CrossChatEvent evt);

        /// <summary>
        /// Pass-through to IGameClock.ConsumeEnergy(). Does NOT own energy state.
        /// </summary>
        public bool ConsumeEnergy(int amount);

        /// <summary>All registered entries.</summary>
        public IReadOnlyList<ConversationEntry> Entries { get; }
    }
}
```

---

## ConversationEntry

**File:** `src/Pinder.Core/Conversation/ConversationEntry.cs`

```csharp
public sealed class ConversationEntry
{
    public GameSession Session { get; }
    public DateTimeOffset? PendingReplyAt { get; set; }
    public ConversationLifecycle Status { get; set; }

    public ConversationEntry(GameSession session,
        DateTimeOffset? pendingReplyAt = null,
        ConversationLifecycle status = ConversationLifecycle.Active);
}
```

---

## ConversationLifecycle

```csharp
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

---

## CrossChatEvent

```csharp
public enum CrossChatEvent
{
    DateSecured,        // +1 to all rolls in other chats for 1 hour
    Unmatched,          // Dread +1 globally
    Nat1Catastrophe,    // Madness +1 bleeds into next conversation
    ThreeDeadToday,     // Dread +3, Madness +1 global
    DoubleDateToday     // Overthinking +2 globally
}
```

---

## FastForward Logic

1. Find entry with earliest `PendingReplyAt` where `Status == Active`
2. `_clock.AdvanceTo(entry.PendingReplyAt.Value)`
3. For each OTHER active entry:
   - Compute silence duration = `_clock.Now - lastActivityTime`
   - If Interest ≤ 4 AND silence ≥ 24h → Ghost (set `Status = Ghosted`, apply Dread +1 globally)
   - If Interest 5–9 AND silence ≥ 24h → Fizzle (set `Status = Fizzled`, no penalty)
   - Interest decay: −1 per full day of silence on each active conversation
4. Return the entry whose reply arrived

---

## Cross-Chat Event Effects

| Event | Effect |
|---|---|
| DateSecured | Set a flag: +1 to all rolls in other active sessions for 1 game-hour (tracked via clock) |
| Unmatched | `ApplyGrowth(Dread, +1, "Unmatched in another chat")` on all sessions' player shadows |
| Nat1Catastrophe | `ApplyGrowth(Madness, +1, "Catastrophe bleed")` on the NEXT session only |
| ThreeDeadToday | `ApplyGrowth(Dread, +3, "3 dead conversations today")` + `ApplyGrowth(Madness, +1, ...)` globally |
| DoubleDateToday | `ApplyGrowth(Overthinking, +2, "Double-booked dates")` globally |

---

## Behavioral Invariants
- Registry does NOT call `GameSession.StartTurnAsync` or `ResolveTurnAsync` — it schedules, the host executes
- Energy is delegated to `IGameClock.ConsumeEnergy()` — registry does NOT track energy independently
- Ghost trigger in registry is different from the per-turn ghost trigger in GameSession (registry checks 24h silence; GameSession checks d4 per turn while Bored)
- Cross-chat events require `SessionShadowTracker` on each session's config; if absent, shadow effects are no-ops
- `FastForward` returns null (or throws) if no pending replies exist
