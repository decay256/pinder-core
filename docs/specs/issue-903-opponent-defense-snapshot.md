# Wire Field: `opponent_defense_snapshot`

**Issue:** pinder-core #903  
**Added in:** `TurnStart` + `TurnSnapshot` (session-runner)  
**Status:** Shipped

## Field Definition

| Property | Value |
|---|---|
| JSON key (on `TurnStart`) | `opponent_defense_snapshot` (carried as a C# property) |
| C# type | `OpponentDefenseSnapshot` |
| Serialized shape | `{ "by_attacker_stat": { "<StatType>": { "defending_stat": "...", "effective_modifier": N, "base_modifier": N }, ... } }` |
| Entry count | Always 6 (one per `StatType` value) |
| Default | Populated on every `StartTurnAsync`; `null` only in legacy snapshots pre-#903 |

## Record Definitions

```csharp
// src/Pinder.Core/Conversation/OpponentDefenseSnapshot.cs

public sealed class OpponentDefenseSnapshot
{
    [JsonPropertyName("by_attacker_stat")]
    public IReadOnlyDictionary<StatType, OpponentDefenseEntry> ByAttackerStat { get; }
}

public sealed class OpponentDefenseEntry
{
    [JsonPropertyName("defending_stat")]
    public StatType DefendingStat { get; }      // from StatBlock.DefenceTable[attackerStat]

    [JsonPropertyName("effective_modifier")]
    public int EffectiveModifier { get; }       // shadow-adjusted + OpponentDCIncrease trap bonus

    [JsonPropertyName("base_modifier")]
    public int BaseModifier { get; }            // opponent.Stats.GetBase(defendingStat)
}
```

## DefenceTable Mapping

| Attacker Stat | Defender Stat |
|---|---|
| Charm | SelfAwareness |
| Rizz | Wit |
| Honesty | Chaos |
| Chaos | Charm |
| Wit | Rizz |
| SelfAwareness | Honesty |

Source: `StatBlock.DefenceTable` (static, authoritative).

## EffectiveModifier Derivation

```csharp
int effectiveModifier = _opponent.Stats.GetEffective(defenderStat);
// GetEffective = GetBase(stat) - floor(GetShadow(ShadowPairs[stat]) / 3)

// Include OpponentDCIncrease trap bonus for this attacker stat (if active):
var activeTrap = _traps.GetActive(attackerStat);
if (activeTrap != null && activeTrap.Definition.Effect == TrapEffect.OpponentDCIncrease)
    effectiveModifier += activeTrap.Definition.EffectValue;
```

### Example: shadow penalty

Opponent has `SelfAwareness=3`, `Overthinking=6`:
- `GetBase(SelfAwareness) = 3`
- `GetShadow(Overthinking) = 6`, penalty = `6/3 = 2`
- `EffectiveModifier = 3 - 2 = 1`, `BaseModifier = 3`

For the Charm attacker row: `EffectiveModifier(1) ≠ BaseModifier(3)`.

### Example: OpponentDCIncrease trap

Active `Rizz` trap with `OpponentDCIncrease +3`, opponent `Wit=2`, no shadow:
- `GetEffective(Wit) = 2` (no shadow), `GetBase(Wit) = 2`
- `EffectiveModifier = 2 + 3 = 5`, `BaseModifier = 2`

For the Rizz attacker row: `EffectiveModifier(5) > BaseModifier(2)`.

## Why on the Wire

The `OpponentDefenseSnapshot` exposes the opponent's current defense posture at
the start of each turn so the frontend can:

- Render per-stat success probability without knowing the DC formula.
- Surface the effect of active traps on defense (e.g., `OpponentDCIncrease` traps
  making a stat harder to beat).
- Show shadow corruption degrading the opponent's defense stats (which makes the
  opponent *easier* to beat, reducing `EffectiveModifier` below `BaseModifier`).
- Enable replay/resimulation tooling to reproduce the same defense context
  deterministically from a `TurnSnapshot`.

## Serialization

`OpponentDefenseSnapshot` uses `System.Text.Json` with `[JsonPropertyName]`
attributes for all snake_case keys. The outer key `by_attacker_stat` maps to a
dictionary; each nested entry has `defending_stat`, `effective_modifier`, and
`base_modifier`.

Note: `TurnStart` is rebuilt by `SessionsController.BuildTurnState` before serialization
to the SPA; the wire key is set explicitly in the controller mapping. The
`[JsonPropertyName("opponent_defense_snapshot")]` attribute on `TurnStart.OpponentDefenseSnapshot`
is added for consistency with the rest of the class's wire-attribute discipline,
rather than as a primary wire-shape fix.

`StatType` enum keys in the dictionary serialize using their default enum serializer
(integer-keyed). Callers that need string keys should serialize via a custom
converter or use `ByAttackerStat.ToDictionary(kvp => kvp.Key.ToString(), ...)`.

## TurnSnapshot (session-runner)

`TurnSnapshot` (in `session-runner/Snapshot/SessionSnapshot.cs`) stores the
defense snapshot as `Dictionary<string, TurnDefenseEntry>?` where keys are the
`StatType.ToString()` attacker stat names (PascalCase). Null on legacy snapshots
taken before #903.

`BuildTurnSnapshot` in `session-runner/Program.cs` receives
`opponentDefenseSnapshot` as a new optional trailing parameter (defaults `null`
for backward compatibility) and projects `OpponentDefenseEntry` values into
`TurnDefenseEntry`.

PascalCase serialization applies to `TurnSnapshot` fields (`DefendingStat`,
`EffectiveModifier`, `BaseModifier`), consistent with the session-runner's
existing convention (same mismatch with `GameStateSnapshot` snake_case that
`GhostProbabilityPerTurn` lives with — deliberate and documented).

## Do Not

- Do not change `StatBlock.DefenceTable` — it is the authoritative mapping.
- Do not change `StatBlock.GetEffective` — the snapshot derives from it.
- Do not skip the `TurnSnapshot` update when the derivation formula changes.
- Do not include steering rolls, momentum, or level bonuses in the modifier —
  `EffectiveModifier` is the stat modifier only (plus active DC-increase trap
  bonus), not the full DC.
