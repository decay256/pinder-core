# Contract: Issue #207 — SessionDocumentBuilder + PromptTemplates

## Component
- `src/Pinder.LlmAdapters/SessionDocumentBuilder.cs` — static class, formats conversation history + prompts
- `src/Pinder.LlmAdapters/PromptTemplates.cs` — static const strings for §3.2–3.8 instruction templates
- Helper: `src/Pinder.LlmAdapters/Anthropic/CacheBlockBuilder.cs` — builds `ContentBlock[]` with `cache_control`

## Maturity: Prototype
## NFR: latency target — all methods < 1ms (pure string construction, no I/O)

---

## 1. SessionDocumentBuilder

```csharp
namespace Pinder.LlmAdapters
{
    /// <summary>
    /// Builds the user-message content for each ILlmAdapter method call.
    /// Pure string construction — no I/O, no state.
    /// </summary>
    public static class SessionDocumentBuilder
    {
        /// <summary>
        /// Formats §3.2 dialogue options prompt.
        /// Includes full conversation history with [T{n}|PLAYER|name] markers.
        /// </summary>
        /// <param name="conversationHistory">Turn-by-turn (sender, text) pairs.</param>
        /// <param name="opponentLastMessage">Last opponent message, or "" if turn 1.</param>
        /// <param name="activeTraps">Trap names, may be empty array.</param>
        /// <param name="currentInterest">Current interest meter value.</param>
        /// <param name="currentTurn">1-based turn number.</param>
        /// <param name="playerName">Player's display name for history markers.</param>
        /// <param name="opponentName">Opponent's display name for history markers.</param>
        /// <returns>Formatted user-message string.</returns>
        public static string BuildDialogueOptionsPrompt(
            IReadOnlyList<(string Sender, string Text)> conversationHistory,
            string opponentLastMessage,
            string[] activeTraps,
            int currentInterest,
            int currentTurn,
            string playerName,
            string opponentName);

        /// <summary>
        /// Formats §3.3 (success) or §3.4 (failure) delivery prompt.
        /// </summary>
        /// <param name="conversationHistory">Turn-by-turn (sender, text) pairs.</param>
        /// <param name="chosenOption">The player's chosen DialogueOption.</param>
        /// <param name="outcome">FailureTier.None for success, otherwise degradation tier.</param>
        /// <param name="beatDcBy">How much the roll beat the DC by (negative on failure).</param>
        /// <param name="activeTrapInstructions">Trap instruction strings, may be null/empty.</param>
        /// <returns>Formatted user-message string.</returns>
        public static string BuildDeliveryPrompt(
            IReadOnlyList<(string Sender, string Text)> conversationHistory,
            DialogueOption chosenOption,
            FailureTier outcome,
            int beatDcBy,
            string[]? activeTrapInstructions);

        /// <summary>
        /// Formats §3.5 opponent response prompt.
        /// Includes [SIGNALS] instruction block for Tell/WeaknessWindow generation.
        /// </summary>
        public static string BuildOpponentPrompt(
            IReadOnlyList<(string Sender, string Text)> conversationHistory,
            string playerDeliveredMessage,
            int interestBefore,
            int interestAfter,
            double responseDelayMinutes,
            string[]? activeTrapInstructions);

        /// <summary>
        /// Formats §3.8 interest change beat prompt.
        /// </summary>
        public static string BuildInterestChangeBeatPrompt(
            string opponentName,
            int interestBefore,
            int interestAfter,
            InterestState newState);
    }
}
```

## 2. Conversation History Format

Full history with §3.2 markers — **never truncated**:
```
[CONVERSATION_START]
[T1|PLAYER|GERALD_42] "Hey, you come here often?"
[T1|OPPONENT|VELVET] "..."
[T2|PLAYER|GERALD_42] "Something witty"
[T2|OPPONENT|VELVET] "Something back"
[CURRENT_TURN]
```

Rules:
- Use `Sender` from the history tuple to determine PLAYER vs OPPONENT
- Turn number increments every 2 entries (1 player + 1 opponent = 1 turn)
- Empty history → just `[CONVERSATION_START]\n[CURRENT_TURN]`

## 3. PromptTemplates

```csharp
namespace Pinder.LlmAdapters
{
    /// <summary>
    /// Static instruction templates for §3.2–3.8 prompts.
    /// Source of truth: design/systems/character-construction.md
    /// </summary>
    public static class PromptTemplates
    {
        public const string DialogueOptionsInstruction = "...";   // §3.2
        public const string SuccessDeliveryInstruction = "...";   // §3.3
        public const string FailureDeliveryInstruction = "...";   // §3.4
        public const string OpponentResponseInstruction = "...";  // §3.5 — includes [SIGNALS] block instruction
        public const string InterestBeatInstruction = "...";      // §3.8
    }
}
```

### OpponentResponseInstruction MUST include signal generation (per #214)

The §3.5 template must instruct the LLM to optionally include a `[SIGNALS]` block:
```
[RESPONSE]
"actual opponent message text"

[SIGNALS]
TELL: CHARM (description of tell)
WEAKNESS: WIT -2 (description of opening)
```

Parsing rules (for #208):
- `[SIGNALS]` section absent → `DetectedTell = null`, `WeaknessWindow = null`
- `TELL: {STAT} ({description})` → `new Tell(StatType.X, description)`
- `WEAKNESS: {STAT} -{reduction} ({description})` → `new WeaknessWindow(StatType.X, reduction)`
- Malformed signals → return null (never throw)
- Signals should appear ~30-40% of the time (controlled by prompt wording)

## 4. CacheBlockBuilder

```csharp
namespace Pinder.LlmAdapters.Anthropic
{
    /// <summary>
    /// Builds ContentBlock arrays with cache_control for Anthropic prompt caching.
    /// </summary>
    public static class CacheBlockBuilder
    {
        /// <summary>
        /// Builds system blocks with both prompts cached (for dialogue options, delivery).
        /// Both character prompts get cache_control: ephemeral.
        /// </summary>
        public static ContentBlock[] BuildCachedSystemBlocks(
            string playerPrompt, string opponentPrompt);

        /// <summary>
        /// Builds system blocks with only opponent prompt cached (for §3.5 opponent response).
        /// </summary>
        public static ContentBlock[] BuildOpponentOnlySystemBlocks(
            string opponentPrompt);
    }
}
```

## 5. Test Requirements

**Test location:** `tests/Pinder.LlmAdapters.Tests/SessionDocumentBuilderTests.cs`

1. Empty history → correct `[CONVERSATION_START]\n[CURRENT_TURN]` format
2. 3-turn history → correct `[T{n}|PLAYER|name]` markers with incrementing turns
3. 8-turn history → all turns present (no truncation)
4. Cache blocks have `cache_control` set
5. Opponent-only system blocks have single block

## Dependencies
- #205 (project scaffold, DTOs — for ContentBlock type)

## Consumers
- #208 (AnthropicLlmAdapter calls all builder methods)

## What this component does NOT own
- HTTP transport (that's AnthropicClient)
- LLM response parsing (that's AnthropicLlmAdapter)
- GameSession orchestration
