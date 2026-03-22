# Contract: Success Scale (Issue #8 concern, needed by #7)

## Context
Rules v3.4 §5 defines interest deltas on success. The current codebase has **no implementation** — `RollResult` only tracks `MissMargin` (0 on success). This contract defines the new properties.

## Component
`Pinder.Core.Rolls.RollResult`

## File
`src/Pinder.Core/Rolls/RollResult.cs`

## New Properties

```csharp
/// <summary>By how much the roll beat the DC. 0 on failure.</summary>
public int SuccessMargin => IsSuccess ? Total - DC : 0;

/// <summary>
/// Interest delta to apply on success, per rules v3.4 §5.
/// 0 on failure. Nat 20 always returns 4.
/// </summary>
public int InterestDelta
{
    get
    {
        if (!IsSuccess) return 0;
        if (IsNatTwenty) return 4;
        int margin = SuccessMargin;
        if (margin >= 10) return 3;  // Crit
        if (margin >= 5)  return 2;
        if (margin >= 1)  return 1;
        return 1;  // Beat DC by 0 (exact tie) still counts as +1
    }
}
```

## Success Scale Table (rules v3.4 §5)

| Condition | Interest Delta |
|---|---|
| Beat DC by 1–4 | +1 |
| Beat DC by 5–9 | +2 |
| Beat DC by 10+ | +3 (Crit) |
| Nat 20 | +4 |

## Edge Cases
- **Nat 20 with high margin**: `InterestDelta` is 4 (Nat 20 overrides margin-based calculation)
- **Exact tie (Total == DC)**: This is a success (`Total >= DC`), `SuccessMargin == 0`, returns +1
- **Nat 1**: Always failure regardless of modifiers, `InterestDelta == 0`

## Dependencies
- None (computed properties on existing fields)

## Consumers
- Caller code that applies `result.InterestDelta` to `InterestMeter.Apply()`
- `RulesConstantsTests` (issue #7) — asserts scale values

## Implementation Note
These are **read-only computed properties** — no constructor changes needed. They derive from existing `Total`, `DC`, `IsSuccess`, `IsNatTwenty` fields.
