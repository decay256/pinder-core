# Contract: Issue #48 — XP Tracking

## Component
`Pinder.Core.Progression.XpLedger` (new)

## Maturity
Prototype

---

## XpLedger (`Pinder.Core.Progression`)

Per-session XP accumulator. Records individual XP events with source labels.

```csharp
namespace Pinder.Core.Progression
{
    public sealed class XpLedger
    {
        /// <summary>Total XP earned this session.</summary>
        public int TotalXp { get; }

        /// <summary>Record an XP event.</summary>
        /// <param name="amount">XP amount (always positive).</param>
        /// <param name="source">Human-readable source label.</param>
        public void Record(int amount, string source);

        /// <summary>All recorded events in order.</summary>
        public IReadOnlyList<XpEvent> Events { get; }
    }

    public sealed class XpEvent
    {
        public int Amount { get; }
        public string Source { get; }
        public XpEvent(int amount, string source);
    }
}
```

## XP Source Table (§10)

| Action | XP | Source Label |
|---|---|---|
| Successful check, DC ≤ 13 | 5 | "Success (easy)" |
| Successful check, DC 14–17 | 10 | "Success (medium)" |
| Successful check, DC ≥ 18 | 15 | "Success (hard)" |
| Failed check | 2 | "Failure" |
| Nat 20 | 25 | "Natural 20" |
| Nat 1 | 10 | "Natural 1" |
| Date secured | 50 | "Date secured" |
| Trap recovery | 15 | "Trap recovery" |
| Conversation complete (no date) | 5 | "Conversation complete" |

**Stacking**: Nat 20 replaces the normal success XP (not additive). Nat 1 replaces failure XP. Trap recovery XP is in addition to the roll's success/failure XP.

## DC Tier Resolution

```
DC ≤ 13 → easy (5 XP)
DC 14–17 → medium (10 XP)
DC ≥ 18 → hard (15 XP)
```

For fixed-DC rolls (Read/Recover at DC 12): DC 12 ≤ 13 → easy → 5 XP on success.

## Integration with GameSession

- `GameSession` owns an `XpLedger` instance.
- `ResolveTurnAsync` records roll XP after each turn. Populates `TurnResult.XpEarned`.
- `ReadAsync`/`RecoverAsync` record their XP. `RecoverAsync` success gets trap recovery XP (15) in addition to success XP (5).
- End-of-game XP (date secured 50, conversation complete 5) recorded when game ends.
- `GameSession` exposes `XpLedger` (or `TotalXpEarned`) for the host to read at session end.

## Dependencies
- `RollResult` (for DC, IsNatOne, IsNatTwenty, IsSuccess)

## Consumers
- `GameSession` (records events)
- Host/Unity (reads total at session end)
