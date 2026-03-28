# Contract: GameSessionConfig — Dependency Bundling (resolves VC-82)

## Component
`Pinder.Core.Conversation.GameSessionConfig` (new)

## Maturity
Prototype

---

## Interface

**File**: `src/Pinder.Core/Conversation/GameSessionConfig.cs`

```csharp
public sealed class GameSessionConfig
{
    public ILlmAdapter Llm { get; }
    public IDiceRoller Dice { get; }
    public ITrapRegistry TrapRegistry { get; }
    public IGameClock? GameClock { get; }                // null = no time tracking
    public SessionShadowTracker? PlayerShadows { get; }  // null = no shadow tracking
    public SessionShadowTracker? OpponentShadows { get; } // null = no opponent shadow tracking

    public GameSessionConfig(
        ILlmAdapter llm,
        IDiceRoller dice,
        ITrapRegistry trapRegistry,
        IGameClock? gameClock = null,
        SessionShadowTracker? playerShadows = null,
        SessionShadowTracker? opponentShadows = null)
    {
        Llm = llm ?? throw new ArgumentNullException(nameof(llm));
        Dice = dice ?? throw new ArgumentNullException(nameof(dice));
        TrapRegistry = trapRegistry ?? throw new ArgumentNullException(nameof(trapRegistry));
        GameClock = gameClock;
        PlayerShadows = playerShadows;
        OpponentShadows = opponentShadows;
    }
}
```

## Behavioural Contract
- Required params: Llm, Dice, TrapRegistry (same as current constructor)
- Optional params: all nullable, default null
- Old `GameSession` constructor wraps into a `GameSessionConfig` for backward compat
- New features check `config.PlayerShadows != null` before applying shadow logic

## Consumers
- GameSession (constructor)
- Host (creates config before session)
