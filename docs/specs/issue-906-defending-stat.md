# Wire Field: `defending_stat`

**Issue:** pinder-core #906  
**Added in:** `RollResult` (C# property) + `TurnSnapshot.DefendingRollStat` (session-runner)  
**Frontend consumer:** pinder-web #601  
**Status:** Shipped

## Field Definition

| Property | Value |
|---|---|
| JSON key | `defending_stat` |
| C# property | `RollResult.DefendingStat` |
| Type | `StatType` (serialized as enum name string) |
| Placement | `RollResult` only — **NOT on `RollCheckResult`** |

## Placement Rule

`DefendingStat` is an **option-roll-specific** extra. It lives on `RollResult`, not on `RollCheckResult`. Per the #901 spec, `RollCheckResult` is the kind-agnostic check shape; option-roll-specific extras (`Stat`, `RiskTier`, `ActivatedTrap`, `DefendingStat`) stay on `RollResult`.

## Derivation Formula

```csharp
DefendingStat = StatBlock.DefenceTable[stat];
```

Computed in `RollEngine.ResolveFromComponents` (shared by `Resolve` and `ResolveFixedDC`) and in `GameSession.CreateForcedFailResult`. Single source of truth: `StatBlock.DefenceTable`.

## DefenceTable Mapping

| Attacker Stat | Defending Stat |
|---|---|
| Charm | SelfAwareness |
| Rizz | Wit |
| Honesty | Chaos |
| Chaos | Charm |
| Wit | Rizz |
| SelfAwareness | Honesty |

Source: `StatBlock.DefenceTable` (static, authoritative).

## Serialization

`RollResult.DefendingStat` carries two attributes:

```csharp
[JsonPropertyName("defending_stat")]
[JsonConverter(typeof(JsonStringEnumConverter))]
public StatType DefendingStat { get; }
```

- `[JsonPropertyName]` maps the C# `PascalCase` name to `snake_case` on the wire.
- `[JsonConverter(typeof(JsonStringEnumConverter))]` ensures the value is the enum member name (e.g. `"Wit"`), not an integer.

### Example JSON (option roll, attacker used Rizz)

```json
{
  "stat": "Rizz",
  "defending_stat": "Wit",
  "die_roll": 14,
  ...
}
```

## Wire DTO

`RollResultDto` in `pinder-web` (issue pinder-web#601) is the companion change that exposes `defending_stat` on the HTTP/SSE surface. The `[JsonPropertyName]` and `[JsonConverter]` on `RollResult` ensure that if `RollResult` is ever serialized directly, the field also appears correctly.

## TurnSnapshot Mirror

`TurnSnapshot` (in `session-runner/Snapshot/SessionSnapshot.cs`) exposes the defending stat from the actual roll via:

```csharp
public string DefendingRollStat { get; set; } = string.Empty;
```

Populated in `BuildTurnSnapshot`:

```csharp
DefendingRollStat = result.Roll.DefendingStat.ToString(),
```

Empty string is the default (forward-compatible on legacy snapshots that pre-date #906).

## Invariant

`DefendingStat` MUST always equal `StatBlock.DefenceTable[Stat]`. It is **not independently settable** — it is always derived at `RollResult` construction time. Audit test: `Issue906_DefendingStatTests.DefendingStat_AlwaysEqualsDefenceTableLookup_OnResolve`.

## Synthetic / Forced-Fail Results

`GameSession.CreateForcedFailResult` constructs a `RollResult` without access to a live `defender`. It derives `DefendingStat` from `original.Stat`:

```csharp
defendingStat: StatBlock.DefenceTable[original.Stat]
```

This preserves the invariant even on shadow-override failures.

## Frontend Consumer Reference

pinder-web #601 adds `defending_stat` to `RollResultDto.From()` so the SSE `roll_result` event carries the field to the React SPA. The frontend uses it to display which defending stat was in play for the roll.
