# Contract: Issue #48 — XP Tracking

## Component
`Pinder.Core.Progression.XpLedger` + GameSession wiring

## Dependencies
- #130 (GameSessionConfig — to expose XpLedger)
- #43 (Recover action → 15 XP)

## Files
- `Progression/XpLedger.cs` — new
- `Conversation/GameSession.cs` — wire XP recording

## Interface

```csharp
namespace Pinder.Core.Progression
{
    /// <summary>
    /// Accumulates XP events during a session. Immutable entries, append-only.
    /// </summary>
    public sealed class XpLedger
    {
        /// <summary>Total XP earned this session.</summary>
        public int TotalXp { get; private set; }

        /// <summary>Record an XP event.</summary>
        public void Record(string source, int amount)
        {
            TotalXp += amount;
            // Optionally store entries for debugging/display
        }

        /// <summary>XP for a successful check, by DC tier.</summary>
        public static int SuccessXp(int dc)
        {
            if (dc <= 13) return 5;
            if (dc <= 17) return 10;
            return 15;
        }

        public const int FailXp = 2;
        public const int Nat20Xp = 25;
        public const int Nat1Xp = 10;
        public const int DateSecuredXp = 50;
        public const int TrapRecoveryXp = 15;
        public const int ConversationCompleteXp = 5;
    }
}
```

### XP source table
| Action | XP | Source label |
|--------|-----|-------------|
| Successful check (DC ≤13) | 5 | "success_easy" |
| Successful check (DC 14–17) | 10 | "success_medium" |
| Successful check (DC ≥18) | 15 | "success_hard" |
| Failed check | 2 | "fail" |
| Nat 20 | 25 | "nat20" |
| Nat 1 | 10 | "nat1" |
| Date secured | 50 | "date_secured" |
| Trap recovery (Recover success) | 15 | "trap_recovery" |
| Conversation complete (no date) | 5 | "conversation_complete" |

### GameSession integration
1. Create `XpLedger` in constructor (or via config)
2. In `ResolveTurnAsync`:
   - If success: `_xpLedger.Record("success_X", XpLedger.SuccessXp(rollResult.DC));`
   - If fail: `_xpLedger.Record("fail", XpLedger.FailXp);`
   - If Nat 20: `_xpLedger.Record("nat20", XpLedger.Nat20Xp);` (in ADDITION to success XP)
   - If Nat 1: `_xpLedger.Record("nat1", XpLedger.Nat1Xp);` (in ADDITION to fail XP)
   - Set `TurnResult.XpEarned` = sum of XP recorded this turn
3. In `RecoverAsync` (from #43): on success, `_xpLedger.Record("trap_recovery", 15);`
4. In `ReadAsync`: success = 5 XP, fail = 2 XP
5. On game end: if `DateSecured` → 50 XP; else if game over (unmatch/ghost) → 5 XP

### Nat 20/Nat 1 XP stacking
- Nat 20 grants 25 XP **in addition to** the success XP for beating the DC. Total for Nat 20 = 25 + 5/10/15.
- Nat 1 grants 10 XP **in addition to** the fail XP (2). Total for Nat 1 = 10 + 2 = 12.

## Behavioral contracts
- XpLedger is append-only, TotalXp only increases
- `Record` with amount=0 is a no-op
- XP is per-session — host persists total across sessions
- `TurnResult.XpEarned` reflects only this turn's XP (not cumulative)

## Consumers
GameSession, host (reads TotalXp at session end)
