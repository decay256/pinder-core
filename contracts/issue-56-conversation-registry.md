# Contract: Issue #56 — ConversationRegistry

## Component
`Pinder.Core.Conversation.ConversationRegistry`, `ConversationEntry`, `ConversationLifecycle`, `CrossChatEvent`

## Maturity
Prototype

---

## ConversationRegistry (`Pinder.Core.Conversation`)

Multi-session manager that tracks all active conversations, schedules opponent replies, handles fast-forward, and propagates cross-chat events.

```csharp
namespace Pinder.Core.Conversation
{
    public sealed class ConversationRegistry
    {
        /// <summary>
        /// Constructor. GameClock is the single source of truth for time and energy.
        /// </summary>
        public ConversationRegistry(IGameClock gameClock);

        /// <summary>All conversation entries (active, paused, ended).</summary>
        public IReadOnlyList<ConversationEntry> Entries { get; }

        /// <summary>
        /// Register a new conversation session.
        /// </summary>
        public ConversationEntry Register(GameSession session);

        /// <summary>
        /// Schedule an opponent reply for a conversation after a player turn.
        /// </summary>
        /// <param name="entry">The conversation entry.</param>
        /// <param name="delayMinutes">Opponent reply delay in game minutes.</param>
        /// Post: entry.PendingReplyAt = gameClock.Now + TimeSpan.FromMinutes(delayMinutes)
        public void ScheduleOpponentReply(ConversationEntry entry, double delayMinutes);

        /// <summary>
        /// Fast-forward to the next pending event.
        /// 1. Find earliest PendingReplyAt across all active entries.
        /// 2. Advance IGameClock to that time.
        /// 3. Check all OTHER conversations for ghost/fizzle triggers.
        /// 4. Apply interest decay on paused conversations (-1/day of silence).
        /// 5. Return the entry that just received its reply.
        /// </summary>
        /// <returns>The conversation entry with the earliest pending reply, or null if none pending.</returns>
        public ConversationEntry? FastForward();

        /// <summary>
        /// Propagate a cross-chat event to all sessions.
        /// </summary>
        public void ApplyCrossChatEvent(CrossChatEvent evt);

        /// <summary>
        /// Delegate to IGameClock.ConsumeEnergy(). Does NOT own energy state.
        /// </summary>
        public bool ConsumeEnergy(int amount);
    }
}
```

## ConversationEntry

```csharp
namespace Pinder.Core.Conversation
{
    public sealed class ConversationEntry
    {
        public GameSession Session { get; }
        public DateTimeOffset? PendingReplyAt { get; set; }
        public ConversationLifecycle Status { get; set; }
        public DateTimeOffset LastActivityAt { get; set; }

        public ConversationEntry(
            GameSession session,
            DateTimeOffset? pendingReplyAt = null,
            ConversationLifecycle status = ConversationLifecycle.Active);
    }
}
```

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

## CrossChatEvent

```csharp
public enum CrossChatEvent
{
    DateSecured,         // +1 to all rolls in other chats for 1 hour game time
    Unmatched,           // Dread +1 globally on all sessions
    Nat1Catastrophe,     // Madness +1 bleeds into next conversation
    ThreeDeadToday,      // Dread +3, Madness +1 global
    DoubleDateToday      // Overthinking +2 globally
}
```

## FastForward Ghost/Fizzle Rules

- **Ghost trigger**: Entry with Interest ≤ 4 AND silence ≥ 24h → Status = Ghosted, Dread +1 globally.
- **Fizzle trigger**: Entry with Interest 5–9 AND silence ≥ 24h → Status = Fizzled, no penalty.
- **Interest decay**: Active entries with silence > 24h lose -1 interest per full day elapsed.

## Cross-Chat Event Effects

| Event | Effect |
|---|---|
| DateSecured | Set a temporary buff: +1 to all rolls in other active chats for 1 game-hour |
| Unmatched | Dread +1 on all session shadow trackers |
| Nat1Catastrophe | Madness +1 on the NEXT conversation started |
| ThreeDeadToday | Dread +3, Madness +1 on all session shadow trackers |
| DoubleDateToday | Overthinking +2 on all session shadow trackers |

## Dependencies
- `IGameClock` (#54 / #139 Wave 0) — time and energy
- `GameSession` — the managed sessions
- `SessionShadowTracker` (#139 Wave 0) — for shadow bleed events
- `PlayerResponseDelayEvaluator` (#55) — for computing delay penalties during fast-forward

## Consumers
- Host/Unity (calls Register, ScheduleOpponentReply, FastForward, ApplyCrossChatEvent)

## Size Note
This is the largest component in the sprint. It is implementable in one session because:
1. It delegates all game logic to `GameSession` (no roll/interest/trap logic here).
2. It delegates time to `IGameClock` (no clock logic here).
3. It delegates delay penalties to `PlayerResponseDelayEvaluator` (no penalty calculation here).
4. Its own logic is: list management, scheduling, threshold checks, event propagation.
