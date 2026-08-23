# Game Session

## Overview

`GameSession` orchestrates one Pinder conversation from match to outcome. It owns mutable gameplay state such as interest, turn count, momentum, combo state, traps, shadows, XP events, and semantic conversation history.

## Key Components

| File / Class | Responsibility |
|---|---|
| `GameSession.cs` | Constructs and exposes the session facade. |
| `GameSessionState.cs` | Stores mutable session state. |
| `GameSession.Turns.cs` | Implements the public turn actions. |
| `TurnOrchestrator.cs` | Coordinates option generation and turn resolution. |
| `RollResolutionStage.cs` | Resolves the selected option and its roll-dependent effects. |
| `ShadowGrowthEvaluator.cs` | Evaluates shadow growth and reduction rules. |
| `GameSessionConfig.cs` | Supplies the clock, rule resolver, trackers, RNG, diagnostics, and other session configuration. |
| `TurnStart.cs` | Result of starting a turn, including selectable dialogue options and pre-rolled pools. |
| `TurnResult.cs` | Result of resolving a selected option. |
| `GameStateSnapshot.cs` | Serializable state snapshot used by API and persistence layers. |

## Public Lifecycle

```csharp
public GameSession(
    CharacterProfile player,
    CharacterProfile datee,
    ILlmAdapter llm,
    IDiceRoller dice,
    ITrapRegistry trapRegistry,
    GameSessionConfig config);

public Task<TurnStart> StartTurnAsync(CancellationToken ct = default);
public Task<TurnResult> ResolveTurnAsync(int optionIndex);
public Task<TurnResult> ResolveTurnAsync(
    int optionIndex,
    IProgress<TurnProgressEvent>? progress,
    CancellationToken ct);
public void Wait();
```

There are three current actions:

1. `StartTurnAsync` generates the full sendable dialogue options and prepares the state needed for resolution.
2. `ResolveTurnAsync` commits one selected option, resolves its roll and effects, calls the datee-response path, and returns the resulting state.
3. `Wait` is a self-contained no-roll action. It clears pending options, consumes an active Triple bonus, applies -1 interest, advances traps, and advances the turn.

`ReadAsync`, `RecoverAsync`, `ReadResult`, and `RecoverResult` are not part of the current production API.

## Required Configuration

The six-parameter constructor requires a non-null `GameSessionConfig`. `GameSessionConfig.Clock` must also be set; session construction throws `InvalidOperationException` when it is absent.

```csharp
var clock = new GameClock(startTime, horninessModifiers);
var config = new GameSessionConfig(clock: clock);
var session = new GameSession(player, datee, llm, dice, traps, config);
```

Session horniness is initialized with a d10 roll plus `Clock.GetHorninessModifier()`, clamped to a minimum of zero. Consequently, constructing a session consumes a dice value for that roll.

## Turn Resolution

The selected option's stat, datee defense, level bonus, active bonuses, advantage state, and configured rule values feed the roll. A natural 1 fails and a natural 20 succeeds regardless of the ordinary total.

Options returned by `StartTurnAsync` already contain complete sendable lines. Resolution commits the selected line and applies the configured success improvement, failure corruption, trap, shadow, horniness, and steering stages when their current rules call for them. Unselected options are not committed to semantic conversation history.

Momentum is a roll bonus. The current streak is evaluated while preparing the turn and contributes to the selected option's external bonus during resolution. Triple, tell, callback, and other bonuses are also represented in the resolved roll and result payload.

## State Ownership

`GameSession` and `GameSessionState` own the canonical gameplay and semantic conversation state. LLM adapters receive a per-call context and, for the contextual datee path, engine-owned history. Adapters must not retain cross-session conversation state.

The authoritative behavior is the implementation in `GameSession.Turns.cs`, `TurnOrchestrator*`, the resolution stages, and their tests. API hosts should persist and rehydrate through the supported session snapshot/persistence path rather than reconstructing private fields.
