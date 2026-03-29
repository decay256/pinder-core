# Contract: Issue #48 — XP Tracking (§10)

## Component
`XpLedger` (Progression/) + GameSession integration

## Dependencies
- #43: Recover action (15 XP on recovery success)
- #44: Shadow growth (some XP sources coincide with shadow events)

---

## XpLedger

**File:** `src/Pinder.Core/Progression/XpLedger.cs`

```csharp
namespace Pinder.Core.Progression
{
    public sealed class XpLedger
    {
        /// <summary>Total XP accumulated this session.</summary>
        public int TotalXp { get; }

        /// <summary>Record an XP event.</summary>
        /// <param name="amount">XP to add (positive).</param>
        /// <param name="source">Human-readable source label.</param>
        public void Record(int amount, string source);

        /// <summary>All recorded events for inspection/logging.</summary>
        public IReadOnlyList<(int Amount, string Source)> Events { get; }

        /// <summary>XP earned in the most recent Record call (for TurnResult.XpEarned).</summary>
        public int LastRecordedAmount { get; }
    }
}
```

---

## XP Source Table

| Action | XP | DC Tier Logic |
|---|---|---|
| Successful check (DC ≤ 13) | 5 | `roll.DC <= 13` |
| Successful check (DC 14–17) | 10 | `roll.DC >= 14 && roll.DC <= 17` |
| Successful check (DC ≥ 18) | 15 | `roll.DC >= 18` |
| Failed check | 2 | Any failure |
| Nat 20 | 25 | `roll.IsNatTwenty` (replaces success XP, not additive) |
| Nat 1 | 10 | `roll.IsNatOne` (replaces failure XP, not additive) |
| Date secured | 50 | Game end with DateSecured outcome |
| Trap recovery (Recover success) | 15 | Via RecoverAsync |
| Conversation complete (no date) | 5 | Game end with Unmatched or Ghosted |

**Priority:** Nat20 XP replaces the normal success XP (don't give both 25 and 15). Nat1 XP replaces the normal fail XP.

---

## GameSession Integration

1. `ResolveTurnAsync`: After roll resolution, compute XP:
   - If `roll.IsNatTwenty` → `_xpLedger.Record(25, "Nat 20")`
   - Else if `roll.IsNatOne` → `_xpLedger.Record(10, "Nat 1")`
   - Else if `roll.IsSuccess` → record 5/10/15 based on DC tier
   - Else → `_xpLedger.Record(2, "Failed check")`
   - Set `TurnResult.XpEarned = _xpLedger.LastRecordedAmount`

2. `RecoverAsync` (from #43): On success → `_xpLedger.Record(15, "Trap recovery")`

3. Game end: Record 50 (DateSecured) or 5 (complete without date)

---

## Behavioral Invariants
- XP is always positive (no XP loss)
- Nat20/Nat1 XP replaces normal success/fail XP, not additive
- Read/Wait don't award check XP (no roll or always same outcome)
- Read success: awards 5 XP (DC 12 ≤ 13 tier)
- Read failure: awards 2 XP
- XpLedger is session-scoped (created per GameSession)
