# Contract: Issue #56 — ConversationRegistry

## Component
`Pinder.Core.Conversation.ConversationRegistry` (new class) + supporting types

## Depends on
- #54: GameClock (IGameClock for time management)
- #44: Shadow growth (SessionShadowTracker for cross-chat bleed)
- #139: IGameClock, SessionShadowTracker, GameSessionConfig

## Maturity: Prototype

---

## New Types

### ConversationLifecycle

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

### ConversationEntry

```csharp
public sealed class ConversationEntry
{
    public string ConversationId { get; }
    public GameSession Session { get; }
    public ConversationLifecycle Lifecycle { get; }
    public DateTimeOffset? NextOpponentReplyAt { get; }
    public DateTimeOffset LastActivity { get; }
}
```

### CrossChatEvent

```csharp
public sealed class CrossChatEvent
{
    public string SourceConversationId { get; }
    public string TargetConversationId { get; }
    public ShadowStatType Shadow { get; }
    public int Amount { get; }
    public string Reason { get; }
}
```

### ConversationRegistry

```csharp
public sealed class ConversationRegistry
{
    public ConversationRegistry(IGameClock clock);

    /// <summary>Register a new conversation.</summary>
    public void Add(string conversationId, GameSession session);

    /// <summary>Get a conversation by ID.</summary>
    public ConversationEntry? Get(string conversationId);

    /// <summary>All active conversations.</summary>
    public IReadOnlyList<ConversationEntry> Active { get; }

    /// <summary>Schedule when the opponent will reply in a conversation.</summary>
    public void ScheduleOpponentReply(string conversationId, TimeSpan delay);

    /// <summary>
    /// Advance clock to the next scheduled event. Returns conversations with ready replies.
    /// Does NOT execute turns — host is responsible for calling GameSession methods.
    /// </summary>
    public IReadOnlyList<string> FastForward();

    /// <summary>
    /// Check ghost/fizzle conditions across all active conversations.
    /// Returns list of conversations whose lifecycle changed.
    /// </summary>
    public IReadOnlyList<string> CheckLifecycleEvents();

    /// <summary>
    /// Apply cross-chat shadow bleed: shadow growth in one conversation bleeds into others.
    /// Returns list of cross-chat events that were applied.
    /// </summary>
    public IReadOnlyList<CrossChatEvent> ApplyShadowBleed();
}
```

## Behavioral Invariants
- Registry does NOT make LLM calls
- Registry does NOT execute turns on GameSession
- Registry owns scheduling and lifecycle, host owns turn execution
- Ghost: conversation idle > 24h with interest < 10 → Ghosted
- Fizzle: conversation idle > 48h regardless of interest → Fizzled
- Shadow bleed: when shadow growth occurs in one conversation, 50% (rounded down) bleeds to other active conversations' SessionShadowTrackers

## GameSession Public Interest Accessor (#160)
ConversationRegistry needs to read current interest from GameSession. Add:

```csharp
// On GameSession:
public int CurrentInterest => _interest.Current;
```

## Dependencies
- `IGameClock` (#54/#139)
- `SessionShadowTracker` (#139)
- `GameSession` (existing + #160 accessor)

## Consumers
- Host (Unity game loop or test harness)
