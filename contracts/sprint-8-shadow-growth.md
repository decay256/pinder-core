# Contract: Issue #44 — Shadow Growth Events (§7 Growth Table)

## Component
`Pinder.Core.Conversation.GameSession` (extend) — shadow growth trigger detection

## Depends on
- #139 Wave 0: `SessionShadowTracker` (with `DrainGrowthEvents()`)
- #43: Read/Recover (Overthinking growth on Read failure)

## Maturity: Prototype

---

## Architectural Decision: No CharacterState class

Per #161 resolution, all shadow mutation goes through `SessionShadowTracker` (from #139).
The `CharacterState` class specified in the original #44 spec is **not implemented**.
`GameSession` uses `GameSessionConfig.PlayerShadows` (a `SessionShadowTracker`) for all shadow growth.

## previousOpener

Per #162 resolution, `previousOpener` is read from `GameSessionConfig.PreviousOpener`, not a constructor param.

## TurnResult.ShadowGrowthEvents

Per #163, this field already exists on `TurnResult`. Implementation **populates** it; does not add it.

---

## Shadow Growth Trigger Table

GameSession detects these conditions and calls `SessionShadowTracker.ApplyGrowth()`:

| Shadow Stat | Trigger | Amount | When Checked |
|---|---|---|---|
| Madness | Nat 1 on Charm roll | +2 | ResolveTurnAsync |
| Madness | 3+ TropeTrap failures in session | +1 (once, on 3rd) | ResolveTurnAsync |
| Horniness | (rolled at session start — not grown during session) | — | — |
| Denial | 3+ successful Honesty checks in session | +1 (once, on 3rd) | ResolveTurnAsync |
| Fixation | Same stat 3 turns in a row | +1 | ResolveTurnAsync |
| Fixation | Picked highest-% option 3 turns in a row | +1 | ResolveTurnAsync |
| Dread | Nat 1 on any stat | +1 | ResolveTurnAsync |
| Dread | Interest drops to Bored from higher state | +1 | ResolveTurnAsync |
| Overthinking | Read action failure | +1 | ReadAsync (#43) |
| Overthinking | Interest ≥ 21 (AlmostThere) and player uses SA | +1 | ResolveTurnAsync |

## Internal Tracking Fields

GameSession needs these new private fields:

```csharp
private readonly List<StatType> _statsUsedPerTurn;     // stat chosen each turn
private int _honestySuccessCount;                       // for Denial trigger
private int _tropeTrapCount;                            // for Madness trigger
private readonly List<bool> _highestPctOptionPicked;    // for Fixation trigger
private bool _madnessTropeTrapTriggered;                // one-shot flag
private bool _denialTriggered;                          // one-shot flag
```

## Data Flow

1. After `RollEngine.Resolve()` in `ResolveTurnAsync`:
   - Check each trigger condition
   - Call `_playerShadows.ApplyGrowth(shadow, amount, reason)` for each matched trigger
   - After all growth: `var events = _playerShadows.DrainGrowthEvents()`
   - Pass `events` to `TurnResult` constructor as `shadowGrowthEvents`

2. "Highest-%" detection: the chosen option index is compared against the option that would have the highest success probability (lowest DC or highest stat modifier). GameSession can approximate this by comparing effective stat values across options.

## Dependencies
- `SessionShadowTracker` with `DrainGrowthEvents()` (#139)
- `GameSessionConfig.PreviousOpener` (#139)

## Consumers
- #45 (threshold effects read effective shadow values)
- #48 (XP ledger records events)
