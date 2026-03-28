# Contract: Issue #56 — ConversationRegistry

## Component
`Pinder.Core.Conversation.ConversationRegistry` — multi-session orchestrator

## Dependencies
- #130 (IGameClock, SessionShadowTracker)
- #54 (GameClock implementation)
- #44 (shadow growth — for cross-chat shadow bleed)

## Files
- `Conversation/ConversationRegistry.cs` — new
- `Conversation/ConversationEntry.cs` — new
- `Conversation/ConversationLifecycle.cs` — new enum
- `Conversation/CrossChatEvent.cs` — new enum

## Interface

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

    public enum CrossChatEvent
    {
        DateSecured,
        Unmatched,
        Nat1Catastrophe,
        ThreeDeadToday,
        DoubleDateToday
    }

    public sealed class ConversationEntry
    {
        public GameSession Session { get; }
        public DateTimeOffset? PendingReplyAt { get; set; }
        public ConversationLifecycle Status { get; set; }

        public ConversationEntry(
            GameSession session,
            DateTimeOffset? pendingReplyAt = null,
            ConversationLifecycle status = ConversationLifecycle.Active);
    }

    public sealed class ConversationRegistry
    {
        private readonly IGameClock _clock;
        private readonly List<ConversationEntry> _conversations;

        public ConversationRegistry(IGameClock clock);

        /// <summary>Add a new conversation to the registry.</summary>
        public void Register(GameSession session);

        /// <summary>
        /// Schedule an opponent reply for a session.
        /// PendingReplyAt = clock.Now + TimeSpan.FromMinutes(delayMinutes).
        /// </summary>
        public void ScheduleOpponentReply(GameSession session, double delayMinutes);

        /// <summary>
        /// Fast-forward to the next pending reply.
        /// Advances IGameClock, checks ghost/fizzle on other conversations.
        /// Returns the conversation that just received a reply, or null if no pending.
        /// </summary>
        public ConversationEntry? FastForward();

        /// <summary>
        /// Apply a cross-chat event to all other conversations.
        /// </summary>
        public void ApplyCrossChatEvent(CrossChatEvent evt, GameSession source);

        /// <summary>All registered conversations.</summary>
        public IReadOnlyList<ConversationEntry> All { get; }

        /// <summary>Active conversations only.</summary>
        public IEnumerable<ConversationEntry> Active { get; }

        /// <summary>Consume energy via IGameClock (pass-through).</summary>
        public bool ConsumeEnergy(int amount);
    }
}
```

### FastForward behavior
1. Find entry with earliest `PendingReplyAt` where `Status == Active`
2. If none → return null
3. Advance `_clock.AdvanceTo(entry.PendingReplyAt.Value)`
4. Check all OTHER active conversations:
   - If `interest ≤ 4` (Bored) AND silence ≥ 24 hours → ghost (Status = Ghosted, Dread +1 global)
   - If `interest 5–9` AND silence ≥ 24 hours → fizzle (Status = Fizzled, no penalty)
   - Apply interest decay: -1 per day of silence per active conversation
5. Clear `entry.PendingReplyAt`
6. Return entry

### CrossChatEvent effects
| Event | Effect |
|-------|--------|
| DateSecured | All other sessions: +1 to all rolls for 1 game-hour (flag on sessions) |
| Unmatched | All sessions: Dread +1 (via SessionShadowTracker) |
| Nat1Catastrophe | Next new conversation: Madness +1 |
| ThreeDeadToday | All sessions: Dread +3, Madness +1 |
| DoubleDateToday | All sessions: Overthinking +2 |

### Interest decay on paused conversations
- For each active conversation with silence > 24 hours:
  - days_silent = floor(silence / 24 hours)
  - interest -= days_silent (applied via InterestMeter.Apply)
  - But InterestMeter is internal to GameSession → needs a new `GameSession.ApplyExternalInterestDelta(int)` method

**Decision**: Add `public void ApplyExternalInterestDelta(int delta)` to GameSession for ConversationRegistry to call. This is the only way external code can modify interest.

## Behavioral contracts
- ConversationRegistry does NOT own GameSession state — it calls public methods on it
- Energy state owned by IGameClock, not registry
- Ghost/fizzle checks happen during FastForward, not on every tick
- Cross-chat events are applied immediately when called
- Thread safety: NOT thread-safe

## Size assessment
This is the largest component in the sprint. A single agent can implement it if they focus on:
1. The data structures (ConversationEntry, enums) — trivial
2. Register/Schedule — trivial
3. FastForward — medium (main logic)
4. CrossChatEvent — medium (shadow manipulation)

Total: ~200-300 lines of C#. Manageable in one session.

## Consumers
Host (Unity game loop or standalone runner)
