# Conversation Game Session

## Overview

`GameSession` owns the mutable state and semantic conversation history for one game. LLM adapters receive the context required for each call; they do not own a provider-persistent conversation.

See [`game-session.md`](game-session.md) for the turn lifecycle and [`llm-adapters.md`](llm-adapters.md) for adapter implementation details.

## Key Components

| File / Class | Responsibility |
|---|---|
| `src/Pinder.Core/Conversation/GameSession.cs` | Session construction, dependencies, and state ownership. |
| `src/Pinder.Core/Conversation/GameSessionState.cs` | Mutable engine state, including semantic conversation history. |
| `src/Pinder.Core/Conversation/GameSession.Turns.cs` | Public `StartTurnAsync`, `ResolveTurnAsync`, and `Wait` actions. |
| `src/Pinder.Core/Conversation/TurnOrchestrator.cs` | Coordinates turn generation and resolution stages. |
| `src/Pinder.Core/Interfaces/IStatefulLlmAdapter.cs` | Contextual adapter extension for calls that need engine-owned history and specialized generation operations. |
| `src/Pinder.Core/Stats/SessionShadowTracker.cs` | Tracks per-session shadow growth and reductions. |
| `src/Pinder.Core/Conversation/ShadowGrowthEvaluator.cs` | Evaluates current shadow growth and reduction rules. |

## Contextual Adapter Contract

Despite its legacy name, `IStatefulLlmAdapter` does not retain conversation state. It extends `ILlmAdapter` with a datee-response overload that receives the complete engine-owned semantic history and with specialized steering, horniness-question, and success-improvement operations.

```csharp
public interface IStatefulLlmAdapter : ILlmAdapter
{
    Task<StatefulDateeResult> GetDateeResponseAsync(
        DateeContext context,
        IReadOnlyList<ConversationMessage> history,
        CancellationToken cancellationToken = default);

    Task<string> GetSteeringQuestionAsync(
        SteeringContext context,
        CancellationToken ct = default);

    Task<string> GetHorninessQuestionAsync(
        HorninessQuestionContext context,
        CancellationToken ct = default);

    Task<string> GetSuccessImprovementAsync(
        SuccessImprovementContext context,
        CancellationToken ct = default);
}
```

The adapter must remain safe to share between concurrent sessions. It must not retain a session-specific transcript between calls. `GameSession` commits the canonical delivered-player/datee-response pair after a successful turn.

## Session Construction

`GameSession` has one public six-parameter constructor:

```csharp
new GameSession(player, datee, llm, dice, trapRegistry, config)
```

`GameSessionConfig.Clock` is required by the session constructor. The constructor does not call `StartConversation`, initialize provider state, or combine the two character prompts into a provider-owned transcript.

## Shadow State

Shadow changes are evaluated by the current roll-resolution and `ShadowGrowthEvaluator` stages. Reductions use `SessionShadowTracker.ApplyOffset`; growth uses `ApplyGrowth`. Both are recorded and exposed through the turn result's shadow event collection. Consult those implementation classes and their tests for the authoritative trigger list, since the rules have changed repeatedly.

## Historical Note

The original 2026-04-05 implementation used `StartConversation` and `HasActiveConversation` and initialized adapter-held state from the `GameSession` constructor. That design was retired. It is historical context only and must not be used for new integrations.
