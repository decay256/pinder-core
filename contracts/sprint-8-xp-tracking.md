# Contract: Issue #48 — XP Tracking (§10)

## Component
- `Pinder.Core.Progression.XpLedger` (new class)
- `Pinder.Core.Conversation.GameSession` (extend)

## Depends on
- #43: Read/Recover actions (XP from those)
- #44: Shadow growth (potential XP from recovery events)

## Maturity: Prototype

---

## XpLedger

**File:** `src/Pinder.Core/Progression/XpLedger.cs`

```csharp
namespace Pinder.Core.Progression
{
    public sealed class XpLedger
    {
        /// <summary>Record an XP event with label and amount.</summary>
        public void Record(string label, int amount);

        /// <summary>Total XP accumulated this session.</summary>
        public int Total { get; }

        /// <summary>All recorded events for audit/display.</summary>
        public IReadOnlyList<(string Label, int Amount)> Events { get; }
    }
}
```

## XP Source Table

| Action | XP | Label | When |
|---|---|---|---|
| Success (DC ≤ 13) | 5 | "success-easy" | ResolveTurnAsync |
| Success (DC 14–17) | 10 | "success-medium" | ResolveTurnAsync |
| Success (DC ≥ 18) | 15 | "success-hard" | ResolveTurnAsync |
| Failed check | 2 | "failure" | ResolveTurnAsync |
| Nat 20 | 25 | "nat20" | ResolveTurnAsync (replaces success XP) |
| Nat 1 | 10 | "nat1" | ResolveTurnAsync (replaces failure XP) |
| Date secured | 50 | "date-secured" | Game end |
| Trap recovery | 15 | "recovery" | RecoverAsync success |
| Conversation complete (no date) | 5 | "conversation-complete" | Game end (Unmatched/Ghosted) |

**Nat 20/Nat 1 override**: They REPLACE the normal success/failure XP, not stack.

## GameSession Integration

1. GameSession creates `XpLedger` on construction
2. After each roll resolution, compute XP and call `_xpLedger.Record(label, amount)`
3. Pass `_xpLedger.Total - previousTotal` as `xpEarned` on `TurnResult`
4. On game end: record date-secured (50) or conversation-complete (5)
5. Expose `public int TotalXpEarned => _xpLedger.Total` on GameSession for host

## Dependencies
- `LevelTable.GetLevel()` (already exists)

## Consumers
- `TurnResult.XpEarned` (already exists)
- Host reads `GameSession.TotalXpEarned` at session end
