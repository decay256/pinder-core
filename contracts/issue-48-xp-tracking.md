# Contract: Issue #48 — XP Tracking

## Component
`Pinder.Core.Progression.XpLedger` (new)
`Pinder.Core.Conversation.GameSession` (modified — wire XP recording)

## Maturity
Prototype

---

## XpLedger

**File**: `src/Pinder.Core/Progression/XpLedger.cs`

```csharp
public sealed class XpLedger
{
    private readonly List<(string Source, int Amount)> _events = new List<(string, int)>();

    public int Total { get; private set; }

    public IReadOnlyList<(string Source, int Amount)> Events => _events;

    public void Record(string source, int amount)
    {
        _events.Add((source, amount));
        Total += amount;
    }
}
```

---

## XP Sources

| Action | XP | Source label |
|--------|----|-------------|
| Successful check (DC ≤ 13) | 5 | "success-easy" |
| Successful check (DC 14–17) | 10 | "success-medium" |
| Successful check (DC ≥ 18) | 15 | "success-hard" |
| Failed check | 2 | "fail" |
| Nat 20 | 25 | "nat20" |
| Nat 1 | 10 | "nat1" |
| Date secured | 50 | "date-secured" |
| Trap recovery (successful Recover action) | 15 | "trap-recovery" |
| Conversation complete (no date) | 5 | "conversation-complete" |

**Note**: Nat 20/Nat 1 XP is IN ADDITION to the success/fail XP. A Nat 20 gives 25 + the success tier XP.

---

## DC Tier for XP

```csharp
static int GetSuccessXp(int dc)
{
    if (dc <= 13) return 5;
    if (dc <= 17) return 10;
    return 15;
}
```

---

## Integration into GameSession

In `ResolveTurnAsync`:
```csharp
// After roll
int turnXp = 0;
if (rollResult.IsSuccess)
    turnXp += GetSuccessXp(rollResult.DC);
else
    turnXp += 2;  // fail XP

if (rollResult.IsNatTwenty) turnXp += 25;
if (rollResult.IsNatOne)    turnXp += 10;

_xpLedger.Record(source, turnXp);
```

On game end:
```csharp
if (outcome == GameOutcome.DateSecured)
    _xpLedger.Record("date-secured", 50);
else
    _xpLedger.Record("conversation-complete", 5);
```

In `RecoverAsync` on success:
```csharp
_xpLedger.Record("trap-recovery", 15);
```

`TurnResult.XpEarned` = turnXp for that turn.

---

## Behavioural Contract
- `XpLedger` is per-session, owned by GameSession
- XP events are append-only — no removal
- `Total` is always the sum of all recorded amounts
- GameSession exposes `public int TotalXpEarned => _xpLedger.Total` for end-of-session reporting
- XP does NOT trigger level-up during a session — level-up happens between sessions in the host

## Dependencies
- #78 (TurnResult.XpEarned field)

## Consumers
- GameSession
- Host (reads TotalXpEarned after session ends)
