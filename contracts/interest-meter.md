# Contract: InterestMeter (Issues #2, #6)

## Component
`Pinder.Core.Conversation.InterestMeter`

## File
`src/Pinder.Core/Conversation/InterestMeter.cs`

## Changes Required

### Issue #2 — Max 20 → 25

```csharp
// BEFORE:
public const int Max = 20;

// AFTER:
public const int Max = 25;
```

`StartingValue` remains 10. `Min` remains 0. `IsMaxed` and `IsZero` logic unchanged.

### Issue #6 — Add InterestState enum and GetState() method

Per rules v3.4 §6, there are exactly **6** interest states (NOT 7 — no "Lukewarm"):

```csharp
public enum InterestState
{
    Unmatched,    // 0
    Bored,        // 1–4
    Interested,   // 5–15
    VeryIntoIt,   // 16–20
    AlmostThere,  // 21–24
    DateSecured   // 25
}
```

Add to `InterestMeter`:

```csharp
/// <summary>Returns the current interest state based on rules v3.4 §6 boundaries.</summary>
public InterestState GetState()
{
    if (Current <= 0)  return InterestState.Unmatched;
    if (Current <= 4)  return InterestState.Bored;
    if (Current <= 15) return InterestState.Interested;
    if (Current <= 20) return InterestState.VeryIntoIt;
    if (Current <= 24) return InterestState.AlmostThere;
    return InterestState.DateSecured;
}

/// <summary>True if the current state grants the player advantage.</summary>
public bool HasAdvantage => GetState() == InterestState.VeryIntoIt || GetState() == InterestState.AlmostThere;

/// <summary>True if the current state imposes disadvantage.</summary>
public bool HasDisadvantage => GetState() == InterestState.Bored;
```

## Public Interface (post-change)

```csharp
public sealed class InterestMeter
{
    public const int Max = 25;           // CHANGED from 20
    public const int Min = 0;            // unchanged
    public const int StartingValue = 10; // unchanged

    public int Current { get; }
    public void Apply(int delta);
    public bool IsMaxed { get; }
    public bool IsZero { get; }

    // NEW:
    public InterestState GetState();
    public bool HasAdvantage { get; }
    public bool HasDisadvantage { get; }
}

public enum InterestState
{
    Unmatched, Bored, Interested, VeryIntoIt, AlmostThere, DateSecured
}
```

## Invariants
- Exactly 6 states. No gaps, no overlaps in ranges.
- `DateSecured` is only reachable at exactly `Max` (25).
- `Unmatched` is only reachable at exactly 0.
- State boundaries are inclusive on the lower bound per the ranges above.

## Side-effect on TimingProfile
`TimingProfile.ComputeDelay()` currently hardcodes `Math.Min(20, interestLevel)`. This must change to `Math.Min(InterestMeter.Max, interestLevel)` or `Math.Min(25, ...)`. File: `src/Pinder.Core/Conversation/TimingProfile.cs`, line with `Math.Min(20, interestLevel)`.

## Dependencies
- None

## Consumers
- `RollEngine` (indirectly — caller checks `HasAdvantage`/`HasDisadvantage` and passes to Resolve)
- `TimingProfile.ComputeDelay()` — clamps interest to [0, Max]
- `CharacterSystemTests` — assertions on Max
- Future: `RulesConstantsTests` (issue #7)
