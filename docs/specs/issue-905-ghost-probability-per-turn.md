# Wire Field: `ghost_probability_per_turn`

**Issue:** pinder-core #905  
**Added in:** `GameStateSnapshot` (wire field) + `TurnSnapshot` (session-runner)  
**Status:** Shipped

## Field Definition

| Property | Value |
|---|---|
| JSON key | `ghost_probability_per_turn` |
| C# property | `GameStateSnapshot.GhostProbabilityPerTurn` |
| Type | `double` (0.0..1.0) |
| Default | `0.0` |

## Derivation Formula

```csharp
double ghostProbabilityPerTurn = state == InterestState.Bored ? 0.25 : 0.0;
```

Computed in `GameSessionHelpers.CreateSnapshot` from the current `InterestState`. All states except `Bored` return `0.0`.

## Interest State → Ghost Probability

| InterestState | GhostProbabilityPerTurn |
|---|---|
| Bored (interest 1–4) | 0.25 |
| Lukewarm (5–9) | 0.0 |
| Interested (10–15) | 0.0 |
| VeryIntoIt (16–20) | 0.0 |
| AlmostThere (21–24) | 0.0 |
| DateSecured (25) | 0.0 |
| Unmatched (0) | 0.0 |

## Why on the Wire

The `_dice.Roll(4) == 1` ghost-trigger check fires at the start of every action
when `InterestState == Bored`. That's a 25% chance. Exposing this as a
pre-computed probability allows:

- Frontend ghost-risk UI (e.g., warning indicator, tooltip) without baking
  the interest-threshold thresholds into the frontend.
- Replay/resimulation tooling to reproduce ghost-risk context deterministically
  from a `TurnSnapshot`.
- Future flexibility: the probability value can evolve (e.g., `0.15` for a
  difficulty setting, or a continuous interpolation over interest range) by
  changing only the derivation in `GameSessionHelpers.CreateSnapshot`, with no
  frontend or spec schema change needed.

## Serialization

`GameStateSnapshot` uses `System.Text.Json` with a `[JsonPropertyName("ghost_probability_per_turn")]`
attribute. The key is always present in the JSON output (not omitted when zero).

## TurnSnapshot

`TurnSnapshot` (in `session-runner/Snapshot/SessionSnapshot.cs`) does **not** inline
`GameStateSnapshot` — it has individual fields. The `GhostProbabilityPerTurn` field
was added **explicitly** to `TurnSnapshot` and is populated in `BuildTurnSnapshot` via
`state.GhostProbabilityPerTurn` where `state = result.StateAfter`. PascalCase
serialization applies (`GhostProbabilityPerTurn` on disk in `.turn-NN.snap.json`).

## Do Not

- Do not change the ghost-trigger roll rule itself (`_dice.Roll(4) == 1`).
- Do not mutate the probability in `GameStateSnapshot` after construction.
- Do not skip the `TurnSnapshot` update when the derivation formula changes.
