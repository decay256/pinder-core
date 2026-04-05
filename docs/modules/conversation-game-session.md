# Conversation Game Session

## Overview
This document covers `GameSession` features beyond the primary module doc [`game-session.md`](game-session.md): shadow stat reductions (§7) and stateful LLM conversation session wiring.

## Key Components

| File / Class | Description |
|---|---|
| `src/Pinder.Core/Conversation/GameSession.cs` | Contains all shadow reduction logic within existing methods (`ResolveTurnAsync`, `EvaluatePerTurnShadowGrowth`, `EvaluateEndOfGameShadowGrowth`, `RecoverAsync`). |
| `src/Pinder.Core/Stats/SessionShadowTracker.cs` | Provides `ApplyOffset(ShadowStatType, int, string)` — the method used for all reductions (accepts negative deltas, unlike `ApplyGrowth`). |
| `tests/Pinder.Core.Tests/ShadowReductionTests.cs` | Core positive/negative tests for each of the 4 new reduction events. |
| `tests/Pinder.Core.Tests/ShadowReductionSpecTests.cs` | Spec-driven tests — boundary values, edge cases (negative deltas, null shadows, stacking), and coexistence with other shadow events. |
| `src/Pinder.Core/Interfaces/IStatefulLlmAdapter.cs` | Interface extending `ILlmAdapter` with `StartConversation(string)` and `HasActiveConversation` for stateful conversation mode. |
| `tests/Pinder.Core.Tests/Issue542_StatefulSession_TestEngineerTests.cs` | Spec-driven tests for stateful session wiring — interface shape, constructor detection, system prompt format, backward compatibility. |

## API / Public Interface

### `IStatefulLlmAdapter` (Pinder.Core.Interfaces)

```csharp
public interface IStatefulLlmAdapter : ILlmAdapter
{
    void StartConversation(string systemPrompt);
    bool HasActiveConversation { get; }
}
```

- Extends `ILlmAdapter` — any implementor must also satisfy the four `ILlmAdapter` methods.
- `StartConversation` initializes an internal conversation session. Calling again replaces the previous session (no error).
- `HasActiveConversation` returns `false` before `StartConversation` is called, `true` after.
- Lives in `Pinder.Core` (zero NuGet dependencies — pure interface).

### Shadow Reduction API

No new public methods or types for shadow reductions. All changes are internal to existing `GameSession` private/public methods. The reductions use existing `SessionShadowTracker` API:

```csharp
// Used for all reductions (delta is negative, e.g. -1)
public string ApplyOffset(ShadowStatType shadow, int delta, string reason);

// Used in tests to verify reductions
public int GetDelta(ShadowStatType shadow);
```

## Shadow Reduction Events (§7)

| # | Trigger | Shadow | Delta | Location | Reason String |
|---|---------|--------|-------|----------|---------------|
| 1 | `outcome == GameOutcome.DateSecured` | Dread | −1 | `EvaluateEndOfGameShadowGrowth()` | `"Date secured"` |
| 2 | Honesty success at interest ≥ 15 | Denial | −1 | `EvaluatePerTurnShadowGrowth()` (trigger 6 block) | `"Honesty success at high interest"` |
| 3 | Successful `RecoverAsync()` | Madness | −1 | `RecoverAsync()` success branch | `"Recovered from trope trap"` |
| 4 | Success despite Overthinking disadvantage | Overthinking | −1 | `ResolveTurnAsync()` after roll | `"Succeeded despite Overthinking disadvantage"` |
| 5 | 4+ different stats used | Fixation | −1 | `EvaluateEndOfGameShadowGrowth()` (trigger 13) | *(pre-existing, not changed)* |

### Implementation Details

- **All reductions use `ApplyOffset()`**, not `ApplyGrowth()`. `ApplyGrowth` throws `ArgumentOutOfRangeException` on negative amounts.
- **Null-safety**: All reduction code checks `_playerShadows != null` (or uses `?.` operator for `RecoverAsync`).
- **Negative deltas are valid**: A shadow delta can go below 0 (e.g., Dread at 0 → −1 after DateSecured).
- **No per-session cap**: Reductions stack across turns (e.g., Denial −1 fires on every qualifying Honesty success).
- **Overthinking reduction (AC-4)** has an extra guard: checks `StatBlock.ShadowPairs[chosenOption.Stat] == ShadowStatType.Overthinking` in addition to `_shadowDisadvantagedStats.Contains(chosenOption.Stat)`. This ensures only Overthinking-specific disadvantage triggers the reduction.
- **Co-existence**: Reductions fire independently of growth triggers. E.g., DateSecured can trigger both Dread −1 and Denial +1 (for no Honesty success) on the same outcome.

## Architecture Notes

### Stateful LLM Session Wiring

- **Detection via interface check:** `GameSession`'s 6-parameter constructor checks `_llm is IStatefulLlmAdapter stateful` at the end of initialization. The 5-parameter constructor delegates to the 6-parameter constructor, so the check runs for both.
- **System prompt assembly:** When the adapter is stateful, the constructor builds a system prompt by concatenating `_player.AssembledSystemPrompt + "\n\n---\n\n" + _opponent.AssembledSystemPrompt` and passes it to `stateful.StartConversation(systemPrompt)`. This is a temporary format — issue #543 introduces `SessionSystemPromptBuilder` with structured game vision/rules/meta-contract.
- **Transparent to callers:** No `GameSession` method bodies changed. The adapter internally routes calls through the accumulated `ConversationSession` when active. `GameSession` continues calling `_llm.GetDialogueOptionsAsync(context)` etc. as before.
- **Backward compatibility:** `NullLlmAdapter` implements only `ILlmAdapter` (not `IStatefulLlmAdapter`), so the `is` check returns `false` for all existing tests. Zero behavioral change on the stateless path.
- **One adapter per session:** Architecture assumes 1:1 adapter-to-GameSession relationship. Sharing an adapter across sessions silently replaces the conversation (documented as unsupported).
- **Config-independent:** Stateful detection uses the adapter's type, not `GameSessionConfig`. A null config does not prevent stateful mode.

### Shadow Reductions

- Shadow reductions follow the same event recording pattern as growth triggers — `ApplyOffset` logs the event string, which is later drained via `DrainGrowthEvents()` and surfaced in `TurnResult.ShadowGrowthEvents`.
- The Overthinking reduction is placed in `ResolveTurnAsync()` (not in `EvaluatePerTurnShadowGrowth()`) because it depends on `_shadowDisadvantagedStats`, which is computed during turn resolution.
- See [`game-session.md`](game-session.md) for the full `GameSession` module documentation.

## Change Log

| Date | Issue | Summary |
|------|-------|---------|
| 2026-04-03 | #270 | Initial creation — documented 4 new shadow reduction events (Dread, Denial, Madness, Overthinking) added to `GameSession`. Two new test files (1202 lines). |
| 2026-04-05 | #542 | Stateful LLM session wiring — `IStatefulLlmAdapter` interface added to `Pinder.Core.Interfaces`. `GameSession` constructor detects stateful adapters and calls `StartConversation` with player + opponent system prompts (separated by `\n\n---\n\n`). `NullLlmAdapter` unchanged (stateless path preserved). Test file: `Issue542_StatefulSession_TestEngineerTests.cs`. |
