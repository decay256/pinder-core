# Sprint 10: Wire GameDefinition

## Architecture Overview
This sprint focuses on solidifying prompt engineering and removing flawed stateful LLM behavior. We are returning to a strict **stateless generation pattern** because the Anthropic model requires persona isolation to prevent voice bleed. Issue #536 is explicitly rescoped/rejected because stateful conversation accumulation breaks the LLM's understanding of its role by mixing meta-engine options, player dialogue, and opponent responses into a single `messages[]` array.

`IStatefulLlmAdapter` is removed. The Game Definition (game vision, world rules, meta contract) is injected into the stateless system prompts via `AnthropicOptions` and a modified `SessionSystemPromptBuilder`. `GameDefinition` stays within the `Pinder.LlmAdapters` project, preserving `Pinder.Core`'s zero-dependency status.

## Separation of Concerns Map

- `AnthropicLlmAdapter`
  - Responsibility:
    - Stateless Anthropic API communication
    - Building persona-isolated system blocks with GameDefinition
  - Interface:
    - Implements `ILlmAdapter` only
    - Takes `AnthropicOptions` in constructor
  - Must NOT know:
    - Stateful conversation tracking (`IStatefulLlmAdapter` and `_session` state are removed)

- `SessionSystemPromptBuilder`
  - Responsibility:
    - Assembling the 5-section GameDefinition system prompt
    - Supporting persona isolation by omitting absent character prompts
  - Interface:
    - `public static string Build(string? playerPrompt, string? opponentPrompt, GameDefinition? gameDef = null)`
  - Must NOT know:
    - Which API endpoint is being called

- `AnthropicOptions`
  - Responsibility:
    - Configuration carrier for the adapter
  - Interface:
    - Adds `public GameDefinition? GameDefinition { get; set; }`
  - Must NOT know:
    - How the definition is used

- `GameSession`
  - Responsibility:
    - Orchestrating stateless turns
  - Interface:
    - `public GameSession(..., ILlmAdapter llm, ...)`
  - Must NOT know:
    - `IStatefulLlmAdapter` (removed)
    - `GameDefinition` (it's injected via `AnthropicOptions` on the host side)

## Interface Definitions

### Issue #536 — Rescope/Close Stateful Architecture
**Decision**: Stateful LLM architecture is REJECTED due to persona violation.
**Interface changes**:
- Delete `src/Pinder.Core/Interfaces/IStatefulLlmAdapter.cs`.
- `GameSession.cs`: Remove the `if (_llm is IStatefulLlmAdapter stateful)` block in the constructor.
- `AnthropicLlmAdapter.cs`: Remove `IStatefulLlmAdapter` from base interfaces. Remove `StartConversation` and `HasActiveConversation`. Remove all `if (_session != null)` branching.

### Issue #575 — Bug: game-definition.yaml not loading
**Problem**: GameSession bypassed `SessionSystemPromptBuilder.Build`. Since we are killing stateful sessions, the GameDefinition must be passed via stateless calls.
**Interface changes**:
1. `AnthropicOptions`: Add `public GameDefinition? GameDefinition { get; set; }`
2. `SessionSystemPromptBuilder.Build(string? playerPrompt, string? opponentPrompt, GameDefinition? gameDef = null)`:
   - Accept nulls for playerPrompt and opponentPrompt.
   - If playerPrompt is null, omit the `== PLAYER CHARACTER ==` section.
   - If opponentPrompt is null, omit the `== OPPONENT CHARACTER ==` section.
3. `AnthropicLlmAdapter.cs`:
   - `GetDialogueOptionsAsync`: Use `SessionSystemPromptBuilder.Build(context.PlayerPrompt, null, _options.GameDefinition)` to build the player-only system block.
   - `DeliverMessageAsync`: Use `SessionSystemPromptBuilder.Build(context.PlayerPrompt, null, _options.GameDefinition)` to build the player-only system block.
   - `GetOpponentResponseAsync`: Use `SessionSystemPromptBuilder.Build(null, context.OpponentPrompt, _options.GameDefinition)` to build the opponent-only system block.

### Issue #572 — Bug: [RESPONSE] format tag appears in output
**Problem**: When LLM fails to format the `[RESPONSE]` tag exactly with quotes, the fallback `else` block parses the whole string, including the literal `[RESPONSE]` text.
**Interface changes**:
- `AnthropicLlmAdapter.ParseOpponentResponse`: In the final fallback, if `messageText` starts with `[RESPONSE]`, substring it out. Also trim leading/trailing quotes if present.

### Issue #573 — Remove NarrativeBeat LLM call
**Problem**: State pollution and unnecessary API cost.
**Interface changes**:
- `AnthropicLlmAdapter.GetInterestChangeBeatAsync`: `return Task.FromResult<string?>(null);`. Do not call Anthropic.
- (Optional, if session runner displays it): Session runner must handle null beat text gracefully (no quote block).

### Issue #574 — Bug: CharacterLoader.ParseBio still returns empty
**Problem**: `CharacterDefinitionLoader` loads the bio from JSON but never passes it to the `CharacterProfile` constructor.
**Interface changes**:
- `session-runner/CharacterDefinitionLoader.cs` line 101: Pass `bio: bio` to the `new CharacterProfile(...)` call.
