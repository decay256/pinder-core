# Contract: Issue #44 — Shadow Growth Events (§7)

## Component
Shadow growth detection and application within `GameSession`, using `SessionShadowTracker`

## Dependencies
- #139 Wave 0: `SessionShadowTracker`
- #43: Read/Recover actions (Overthinking growth on Read fail, Recover fail)

---

## Shadow Growth Table

All growth events use `SessionShadowTracker.ApplyGrowth(shadow, amount, reason)`.

### Dread (penalizes Wit)
| Trigger | Amount | When detected |
|---|---|---|
| Interest hits 0 (unmatch) | +2 | End of turn / game end |
| Getting ghosted | +1 | Ghost trigger in StartTurnAsync |
| Catastrophic Wit fail (miss 10+) | +1 | ResolveTurnAsync, after roll |
| Nat 1 on Wit | +1 | ResolveTurnAsync, after roll |

### Madness (penalizes Charm)
| Trigger | Amount | When detected |
|---|---|---|
| Nat 1 on Charm | +1 | ResolveTurnAsync, after roll |
| 3+ trope traps in one conversation | +1 | ResolveTurnAsync, when TropeTrap count reaches 3 |
| Same opener twice in a row | +1 | ResolveTurnAsync, turn 0 comparison (requires session history tracking) |

### Denial (penalizes Honesty)
| Trigger | Amount | When detected |
|---|---|---|
| Date secured without any Honesty successes | +1 | Game end (DateSecured outcome) |
| Nat 1 on Honesty | +1 | ResolveTurnAsync, after roll |

### Fixation (penalizes Chaos)
| Trigger | Amount | When detected |
|---|---|---|
| Same stat used 3 turns in a row | +1 | ResolveTurnAsync, after recording stat |
| Never picked Chaos in whole conversation | +1 | Game end |
| Nat 1 on Chaos | +1 | ResolveTurnAsync, after roll |
| **Offset**: 4+ different stats used in conversation | -1 | Game end (reduces Fixation) |

### Overthinking (penalizes SA)
| Trigger | Amount | When detected |
|---|---|---|
| Read action failed | +1 | ReadAsync (in #43) |
| Recover action failed | +1 | RecoverAsync (in #43) |
| SA used 3+ times in one conversation | +1 | Game end |
| Nat 1 on SA | +1 | ResolveTurnAsync, after roll |

### Horniness (penalizes Rizz)
No growth triggers in §7 — Horniness is driven by time-of-day modifier (#51/#54).

---

## Per-Session Tracking Counters

**New internal state in GameSession** (or a dedicated `SessionTracker` helper class):

```csharp
private int _tropeTrapsActivatedCount;      // Increment when RollResult.Tier == TropeTrap
private bool _hasHonestySuccess;            // Set true on any Honesty roll success
private List<StatType> _statsUsedPerTurn;   // Last N stats for same-stat-3x detection
private HashSet<StatType> _allStatsUsed;    // All stats ever used (for Fixation offset + never-Chaos)
private int _saUsageCount;                  // Times SelfAwareness was used as Speak stat
```

---

## TurnResult Integration

`TurnResult.ShadowGrowthEvents` (already exists as `IReadOnlyList<string>`) is populated with the descriptions from `ApplyGrowth()` calls.

Example output for a turn:
```
["Dread +1 (Catastrophic Wit fail)", "Madness +1 (3rd trope trap activated)"]
```

---

## Behavioral Invariants
- Shadow growth happens AFTER the roll is resolved and interest delta applied (growth does not affect the current roll)
- Multiple growth events can fire in the same turn
- Nat1 on any stat always triggers the matching shadow growth
- End-of-game growth events are applied when the game ends (DateSecured, Unmatched, Ghosted)
- Growth events for the current turn are returned in `TurnResult.ShadowGrowthEvents`
- If `SessionShadowTracker` is null (no config), shadow growth is silently skipped (no error)
