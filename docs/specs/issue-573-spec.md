**Module**: docs/modules/conversation.md

## Overview
The `NarrativeBeat` LLM call mechanic was originally designed to generate atmospheric beats when the interest meter crossed a threshold. However, using the conversational session context for this generated character echoes rather than proper stage directions. This feature completely removes the LLM call from the NarrativeBeat flow to maintain stateless generation and eliminate voice bleed. `GameSession` will instead provide a simple UI signal (the new interest state) via `TurnResult.NarrativeBeat` without making any LLM API requests. The session runner is updated to display this as a plain state-change marker rather than quoted narrative text.

## Function Signatures

### `Pinder.Core.Interfaces.ILlmAdapter`
- **Action**: Remove the method `Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context);` completely.

### `Pinder.Core.Conversation.InterestChangeContext`
- **Action**: Delete this class completely, as it is no longer needed.

### `Pinder.Core.Conversation.GameSession`
- **Action**: In `ResolveTurnAsync`, remove the instantiation of `InterestChangeContext` and the call to `_llm.GetInterestChangeBeatAsync`.

### `Pinder.LlmAdapters.SessionDocumentBuilder`
- **Action**: Remove `BuildInterestChangeBeatPrompt` and `GetThresholdInstruction`.

### `Pinder.LlmAdapters.Anthropic.AnthropicOptions`
- **Action**: Remove `InterestChangeBeatTemperature`.

## Input/Output Examples

**GameSession Local Processing (Before)**:
```csharp
var interestChangeContext = new InterestChangeContext(...);
narrativeBeat = await _llm.GetInterestChangeBeatAsync(interestChangeContext);
// Output was an LLM-generated paragraph.
```

**GameSession Local Processing (After)**:
```csharp
// No LLM call. Set directly to a UI signal.
narrativeBeat = $"*** Interest is now {stateAfter} ***";
```

**Session Runner Console Output (Before)**:
`✨ The tension in the air drops noticeably as Velvet sighs.`

**Session Runner Console Output (After)**:
`*** Interest is now Bored ***`

## Acceptance Criteria

### 1. No LLM API call fires for NarrativeBeat during a session
- The method `GetInterestChangeBeatAsync` is removed from `ILlmAdapter`.
- Implementations of `ILlmAdapter` (`AnthropicLlmAdapter` and `NullLlmAdapter`) delete this method.
- `GameSession.ResolveTurnAsync` no longer makes any LLM call when `stateBefore != stateAfter`.

### 2. Session runner does not display ✨ quoted beat text
- In `session-runner/Program.cs`, the display logic for `result.NarrativeBeat` is updated.
- The `✨` emoji is removed. It simply prints the value of `TurnResult.NarrativeBeat`.

### 3. TurnResult.NarrativeBeat field can remain but holds no LLM-generated string
- The property `TurnResult.NarrativeBeat` remains in the codebase (in `TurnResult`).
- `GameSession` populates `narrativeBeat` with a simple hardcoded string indicating the new state (e.g., `"*** Interest is now " + stateAfter + " ***"`).

### 4. Clean up prompt builder methods and options
- Remove `BuildInterestChangeBeatPrompt` and `GetThresholdInstruction` from `Pinder.LlmAdapters.SessionDocumentBuilder`.
- Remove `InterestChangeBeatInstruction` from `Pinder.LlmAdapters.PromptTemplates` (if it exists).
- Remove `InterestChangeBeatTemperature` and its constant `DefaultInterestChangeBeatTemperature` from `Pinder.LlmAdapters.Anthropic.AnthropicLlmAdapter` and `AnthropicOptions`.

### 5. Build and tests pass
- `InterestChangeContext.cs` is deleted.
- All unit tests that mocked or tested `GetInterestChangeBeatAsync`, `InterestChangeContext`, or `BuildInterestChangeBeatPrompt` are safely removed.
- All tests pass and the solution builds cleanly.

## Edge Cases
- **No State Change**: If the interest roll does not result in a state change (e.g., remaining in `Interested`), `TurnResult.NarrativeBeat` must remain `null`. The session runner must not print anything.
- **Null Safety**: The session runner must still check `if (result.NarrativeBeat != null)` before printing to avoid empty lines.

## Error Conditions
- N/A — Removing this network call eliminates the possibility of timeouts or serialization errors during interest state transitions.

## Dependencies
- Modifying `ILlmAdapter` requires updating `Pinder.LlmAdapters/Anthropic/AnthropicLlmAdapter.cs`, `Pinder.Core/Conversation/NullLlmAdapter.cs`, and `Pinder.Core/Conversation/GameSession.cs`.
- Requires modifying `session-runner/Program.cs` for output formatting.
