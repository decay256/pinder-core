# Contract: Issue #26 — ILlmAdapter + Context Types + NullLlmAdapter

## Component
`Pinder.Core.Interfaces.ILlmAdapter` (new interface)
`Pinder.Core.Conversation.*Context` types (new)
`Pinder.Core.Conversation.DialogueOption` (new)
`Pinder.Core.Conversation.NullLlmAdapter` (new, test implementation)

## Maturity
Prototype

## NFR
- Latency: N/A for interface definition; NullLlmAdapter returns synchronously wrapped Tasks
- No external dependencies: all types must be pure C# netstandard2.0

## Platform Constraints
- **Target**: netstandard2.0, LangVersion 8.0
- **No `record` types** (C# 9+). Use `sealed class` with readonly properties and constructor.
- **Nullable enabled**: use `?` annotations
- **No NuGet packages**: `Task<T>` comes from `System.Threading.Tasks`

---

## Interface: ILlmAdapter

**File**: `src/Pinder.Core/Interfaces/ILlmAdapter.cs`

```csharp
using System.Threading.Tasks;
using Pinder.Core.Conversation;

namespace Pinder.Core.Interfaces
{
    /// <summary>
    /// Abstraction for all LLM interactions. Injected into GameSession.
    /// Implementations: NullLlmAdapter (test), EigenCoreLlmAdapter (production), etc.
    /// </summary>
    public interface ILlmAdapter
    {
        /// <summary>
        /// Generate 4 dialogue options for the player's turn.
        /// Each option targets a different stat family.
        /// </summary>
        Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context);

        /// <summary>
        /// Deliver the player's chosen message with outcome degradation applied.
        /// Returns the actual text that gets "sent" (may be degraded from intended text on failure).
        /// </summary>
        Task<string> DeliverMessageAsync(DeliveryContext context);

        /// <summary>
        /// Generate the opponent's response to the player's delivered message.
        /// </summary>
        Task<string> GetOpponentResponseAsync(OpponentContext context);

        /// <summary>
        /// Generate a narrative beat when Interest crosses a state threshold.
        /// Return null to skip the narrative beat.
        /// </summary>
        Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context);
    }
}
```

---

## Context Types

**File**: `src/Pinder.Core/Conversation/LlmContextTypes.cs`

All context types in a single file. They are simple data carriers — no logic.

### DialogueContext

```csharp
namespace Pinder.Core.Conversation
{
    public sealed class DialogueContext
    {
        /// <summary>Assembled system prompt for the player character (from PromptBuilder).</summary>
        public string PlayerPrompt { get; }

        /// <summary>Assembled system prompt for the opponent character.</summary>
        public string OpponentPrompt { get; }

        /// <summary>Conversation history as (sender, text) pairs in chronological order.</summary>
        public IReadOnlyList<(string Sender, string Text)> ConversationHistory { get; }

        /// <summary>The opponent's last message (convenience — same as last entry in history where sender == opponent).</summary>
        public string OpponentLastMessage { get; }

        /// <summary>Active trap names on the player (for taint injection into option generation).</summary>
        public IReadOnlyList<string> ActiveTrapNames { get; }

        /// <summary>Current interest level (0–25).</summary>
        public int CurrentInterest { get; }

        // Constructor with all fields
    }
}
```

### DeliveryContext

```csharp
public sealed class DeliveryContext
{
    /// <summary>Full dialogue context (player prompt, opponent prompt, history, etc.).</summary>
    public DialogueContext Dialogue { get; }

    /// <summary>The option the player chose.</summary>
    public DialogueOption ChosenOption { get; }

    /// <summary>Roll outcome tier. None = success.</summary>
    public FailureTier Outcome { get; }

    /// <summary>How much the roll beat the DC by (only meaningful on success, 0 on failure).</summary>
    public int BeatDcBy { get; }

    /// <summary>Full LLM taint instructions from active traps (not just names).</summary>
    public IReadOnlyList<string> ActiveTrapInstructions { get; }

    // Constructor with all fields
}
```

### OpponentContext

```csharp
public sealed class OpponentContext
{
    /// <summary>Full dialogue context.</summary>
    public DialogueContext Dialogue { get; }

    /// <summary>The actual text that was "sent" by the player (post-degradation).</summary>
    public string PlayerDeliveredMessage { get; }

    /// <summary>Interest level before this turn's delta was applied.</summary>
    public int InterestBefore { get; }

    /// <summary>Interest level after this turn's delta was applied.</summary>
    public int InterestAfter { get; }

    /// <summary>Computed response delay in minutes (from TimingProfile).</summary>
    public double ResponseDelayMinutes { get; }

    // Constructor with all fields
}
```

### InterestChangeContext

```csharp
public sealed class InterestChangeContext
{
    /// <summary>Opponent's display name.</summary>
    public string OpponentName { get; }

    /// <summary>Interest before the change.</summary>
    public int InterestBefore { get; }

    /// <summary>Interest after the change.</summary>
    public int InterestAfter { get; }

    /// <summary>The new interest state after the change.</summary>
    public InterestState NewState { get; }

    // Constructor with all fields
}
```

---

## DialogueOption

```csharp
public sealed class DialogueOption
{
    /// <summary>Which stat this option uses for the roll.</summary>
    public StatType Stat { get; }

    /// <summary>The intended message text (before degradation).</summary>
    public string IntendedText { get; }

    // Constructor: (StatType stat, string intendedText)
}
```

**Note**: The issue mentions `CallbackTurnNumber`, `ComboName`, and `HasTellBonus` on `DialogueOption`. These reference mechanics that don't exist in the codebase yet. For prototype maturity: **omit these fields**. Add them when the mechanics they reference are implemented. `Stat` + `IntendedText` is the minimum viable option.

---

## NullLlmAdapter

**File**: `src/Pinder.Core/Conversation/NullLlmAdapter.cs`

```csharp
public sealed class NullLlmAdapter : ILlmAdapter
{
    public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
    {
        // Returns 4 options: Charm, Honesty, Wit, Chaos — one each with generic placeholder text
        var options = new[]
        {
            new DialogueOption(StatType.Charm, "Hey, you come here often?"),
            new DialogueOption(StatType.Honesty, "I have to be real with you..."),
            new DialogueOption(StatType.Wit, "Did you know that penguins propose with pebbles?"),
            new DialogueOption(StatType.Chaos, "What if we just set something on fire?")
        };
        return Task.FromResult(options);
    }

    public Task<string> DeliverMessageAsync(DeliveryContext context)
    {
        // Prefix with failure tier if not success, otherwise echo intended text
        var prefix = context.Outcome == FailureTier.None ? "" : $"[{context.Outcome}] ";
        return Task.FromResult($"{prefix}{context.ChosenOption.IntendedText}");
    }

    public Task<string> GetOpponentResponseAsync(OpponentContext context)
    {
        return Task.FromResult("...");
    }

    public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
    {
        return Task.FromResult<string?>(null);
    }
}
```

---

## Behavioural Contract

- `GetDialogueOptionsAsync` MUST return exactly 4 options. Each option MUST have a non-null `Stat` and non-null/non-empty `IntendedText`.
- `DeliverMessageAsync` MUST return a non-null, non-empty string.
- `GetOpponentResponseAsync` MUST return a non-null, non-empty string.
- `GetInterestChangeBeatAsync` MAY return null (signals "skip narrative beat").
- All methods are `async`-compatible but implementations may return synchronous `Task.FromResult` (as `NullLlmAdapter` does).
- Context types are immutable after construction — no setters.

## Files to Create
1. `src/Pinder.Core/Interfaces/ILlmAdapter.cs` — interface
2. `src/Pinder.Core/Conversation/LlmContextTypes.cs` — DialogueContext, DeliveryContext, OpponentContext, InterestChangeContext, DialogueOption
3. `src/Pinder.Core/Conversation/NullLlmAdapter.cs` — test implementation
4. `tests/Pinder.Core.Tests/NullLlmAdapterTests.cs` — unit tests

## Test Requirements
- `NullLlmAdapter.GetDialogueOptionsAsync` returns exactly 4 non-null options
- Each option has a distinct `StatType`
- Each option has non-empty `IntendedText`
- `DeliverMessageAsync` with `FailureTier.None` returns the intended text verbatim
- `DeliverMessageAsync` with `FailureTier.Fumble` prefixes with `[Fumble] `
- `GetOpponentResponseAsync` returns non-null, non-empty string
- `GetInterestChangeBeatAsync` returns null

## Dependencies
- `Pinder.Core.Stats.StatType` (existing)
- `Pinder.Core.Rolls.FailureTier` (existing)
- `Pinder.Core.Conversation.InterestState` (existing, from #6)

## Consumers
- `GameSession` (Issue #27) — calls all 4 methods
- Future: production LLM adapters (EigenCore, OpenAI, etc.)
