# Contract: Issue #208 — AnthropicLlmAdapter

## Component
`src/Pinder.LlmAdapters/Anthropic/AnthropicLlmAdapter.cs`

## Maturity: Prototype
## NFR: latency target — p99 < 30s (network-bound)

---

## Public Interface

```csharp
namespace Pinder.LlmAdapters.Anthropic
{
    /// <summary>
    /// Concrete ILlmAdapter implementation for Anthropic Claude.
    /// Wires together AnthropicClient, SessionDocumentBuilder, and response parsing.
    /// </summary>
    public sealed class AnthropicLlmAdapter : ILlmAdapter, IDisposable
    {
        public AnthropicLlmAdapter(AnthropicOptions options);
        public AnthropicLlmAdapter(AnthropicOptions options, HttpClient httpClient);
        public void Dispose();

        // ILlmAdapter implementation
        Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context);
        Task<string> DeliverMessageAsync(DeliveryContext context);
        Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context);
        Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context);
    }
}
```

## Method Specifications

### GetDialogueOptionsAsync
1. Build system blocks: `CacheBlockBuilder.BuildCachedSystemBlocks(context.PlayerPrompt, context.OpponentPrompt)`
2. Build user content: `SessionDocumentBuilder.BuildDialogueOptionsPrompt(...)` 
   - **Must pass** `currentTurn` and `playerName`/`opponentName` — see "Context Field Requirements" below
3. Create `MessagesRequest` with temperature = `options.DialogueOptionsTemperature ?? options.Temperature` (default 0.9)
4. `await _client.SendMessagesAsync(request)`
5. Parse response with `ParseDialogueOptions(response.GetText())`
6. **Return**: `DialogueOption[]` (always exactly 4 — pad with defaults if parse yields fewer)

### DeliverMessageAsync
1. Build system blocks: `CacheBlockBuilder.BuildCachedSystemBlocks(context.PlayerPrompt, context.OpponentPrompt)`
2. Build user content: `SessionDocumentBuilder.BuildDeliveryPrompt(...)`
3. Create `MessagesRequest` with temperature = `options.DeliveryTemperature ?? 0.7`
4. `await _client.SendMessagesAsync(request)`
5. **Return**: `response.GetText()` (raw string)

### GetOpponentResponseAsync
1. Build system blocks: `CacheBlockBuilder.BuildOpponentOnlySystemBlocks(context.OpponentPrompt)` — **only opponent prompt** cached
2. Build user content: `SessionDocumentBuilder.BuildOpponentPrompt(...)`
3. Create `MessagesRequest` with temperature = `options.OpponentResponseTemperature ?? 0.85`
4. `await _client.SendMessagesAsync(request)`
5. Parse response with `ParseOpponentResponse(response.GetText())`
6. **Return**: `OpponentResponse` with message text + parsed Tell/WeaknessWindow from `[SIGNALS]` block

### GetInterestChangeBeatAsync
1. No cached system blocks (short contextless call)
2. Build user content: `SessionDocumentBuilder.BuildInterestChangeBeatPrompt(...)`
3. Create `MessagesRequest` with temperature = `options.InterestChangeBeatTemperature ?? 0.8`, empty system array
4. `await _client.SendMessagesAsync(request)`
5. **Return**: `response.GetText()` or null if empty

## Context Field Requirements (Vision Concern #211)

The `SessionDocumentBuilder.BuildDialogueOptionsPrompt` requires `currentTurn`, `playerName`, `opponentName` which are NOT currently on `DialogueContext`. Two options for the implementer:

**Option A (recommended by #211): Add fields to context DTOs in Pinder.Core.**
- `DialogueContext` gains: `int CurrentTurn`, `string PlayerName`, `string OpponentName`
- `DeliveryContext` gains: `string PlayerName`, `string OpponentName`
- `OpponentContext` gains: `string PlayerName`, `string OpponentName`
- `GameSession` already has `_player.DisplayName`, `_opponent.DisplayName`, `_turnNumber` — wire them through

**These DTO changes are Pinder.Core changes** and must be backward-compatible (new optional constructor parameters with defaults).

**Scope estimate**: ~30 lines of DTO changes in Pinder.Core, ~10 lines in GameSession to wire them.

## Response Parsing

### ParseDialogueOptions(string llmResponse)

Expected LLM output format:
```
OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
"message text here"

OPTION_2
[STAT: WIT] [CALLBACK: pizza_story] [COMBO: The Setup] [TELL_BONUS: yes]
"witty message text"
...
```

Parsing:
- Extract `[STAT: X]` → `(StatType)Enum.Parse(typeof(StatType), value, true)` (netstandard2.0 — no generic overload)
- Extract `[CALLBACK: X]` → `CallbackTurnNumber` (parse int from topic tracking) or null if "none"
- Extract `[COMBO: X]` → `ComboName` or null if "none"
- Extract `[TELL_BONUS: yes/no]` → `HasTellBonus` boolean
- Extract quoted text → `IntendedText`
- **Fallback**: if fewer than 4 options parsed, pad with defaults: Charm/Honesty/Wit/Chaos with "..." text
- **NEVER throw** on parse failure

### ParseOpponentResponse(string llmResponse) — Per #214

Expected LLM output format:
```
[RESPONSE]
"actual opponent message text"

[SIGNALS]
TELL: CHARM (opponent fidgets when player uses charm)
WEAKNESS: WIT -2 (opponent seems distracted, opening for wit)
```

Parsing:
- Extract text between `[RESPONSE]` and `[SIGNALS]` (or end of string) → `MessageText`
- If `[SIGNALS]` present:
  - `TELL: {STAT} ({description})` → `new Tell((StatType)Enum.Parse(typeof(StatType), stat, true), description)`
  - `WEAKNESS: {STAT} -{reduction} ({description})` → `new WeaknessWindow((StatType)Enum.Parse(typeof(StatType), stat, true), reduction)`
- If `[SIGNALS]` absent or malformed → `DetectedTell = null`, `WeaknessWindow = null`
- **NEVER throw** on parse failure — degrade gracefully to null signals

## Test Requirements

**Test location:** `tests/Pinder.LlmAdapters.Tests/AnthropicLlmAdapterTests.cs`

1. `ParseDialogueOptions` — well-formed input → 4 options with correct stats
2. `ParseDialogueOptions` — malformed input → 4 padded defaults (no exception)
3. `ParseOpponentResponse` — with signals → populated Tell + WeaknessWindow
4. `ParseOpponentResponse` — without signals → null Tell/WeaknessWindow
5. `ParseOpponentResponse` — malformed signals → null (no exception)
6. Integration with mock HttpClient → verify request shape (model, system blocks, temperature)
7. Verify opponent response call uses only OpponentPrompt in system blocks

## Dependencies
- #205 (project scaffold, DTOs)
- #206 (AnthropicClient)
- #207 (SessionDocumentBuilder, PromptTemplates, CacheBlockBuilder)

## Consumers
- GameSession (via ILlmAdapter interface)
- Unity host (creates AnthropicLlmAdapter and injects into GameSession)

## What this component does NOT own
- HTTP transport and retries (AnthropicClient)
- Prompt template content (PromptTemplates)
- Conversation history formatting (SessionDocumentBuilder)
- Game state management (GameSession)
