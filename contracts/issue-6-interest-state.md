# Contract: Issue #6 — InterestState Enum and Boundaries

## Component
`Pinder.Core.Conversation.InterestMeter` (existing file, extended)

## Maturity
Prototype

## NFR
- Latency: O(1) — simple range checks, no allocation

## New Types

```csharp
// New file: src/Pinder.Core/Conversation/InterestState.cs
namespace Pinder.Core.Conversation
{
    public enum InterestState
    {
        Unmatched,    // Current == 0
        Bored,        // Current 1–4
        Interested,   // Current 5–15
        VeryIntoIt,   // Current 16–20
        AlmostThere,  // Current 21–24
        DateSecured   // Current == 25 (== Max)
    }
}
```

**Note**: The issue AC lists `Lukewarm` but rules v3.4 §6 defines no range for it. Per vision concern #18 safe default: **omit Lukewarm**. If PO later defines a range, add it between Bored and Interested.

## New Methods on InterestMeter

```csharp
// Added to existing InterestMeter class

/// <summary>
/// Returns the InterestState for the current interest value.
/// Boundaries per rules v3.4 §6:
///   0       → Unmatched
///   1–4     → Bored
///   5–15    → Interested
///   16–20   → VeryIntoIt
///   21–24   → AlmostThere
///   25      → DateSecured
/// </summary>
public InterestState GetState();

/// <summary>True when state is VeryIntoIt or AlmostThere.</summary>
public bool GrantsAdvantage { get; }

/// <summary>True when state is Bored.</summary>
public bool GrantsDisadvantage { get; }
```

## Behavioural Contract

- `GetState()` must be a pure function of `Current` — no side effects
- Ranges must be exhaustive and non-overlapping over [0, 25]
- `GrantsAdvantage` and `GrantsDisadvantage` are never both true simultaneously
- When `Current == 0`: `GrantsDisadvantage` is false (game is over, not disadvantage)
- `GrantsAdvantage` = `GetState() == VeryIntoIt || GetState() == AlmostThere`
- `GrantsDisadvantage` = `GetState() == Bored`

## Files Changed
- `src/Pinder.Core/Conversation/InterestState.cs` (new)
- `src/Pinder.Core/Conversation/InterestMeter.cs` (add GetState, GrantsAdvantage, GrantsDisadvantage)

## Test Requirements
- Every boundary value tested: 0, 1, 4, 5, 15, 16, 20, 21, 24, 25
- GrantsAdvantage true only for 16–24
- GrantsDisadvantage true only for 1–4
- No value produces both advantage and disadvantage

## Dependencies
- None (InterestMeter.Max=25 already on main from #2)

## Consumers
- Host game loop (reads GrantsAdvantage/GrantsDisadvantage before calling RollEngine)
- Future: could be used by PromptBuilder for interest-aware prompts
