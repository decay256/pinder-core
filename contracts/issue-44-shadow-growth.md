# Contract: Issue #44 — Shadow Growth Events

## Component
`Pinder.Core.Conversation.SessionShadowTracker` (new)
`Pinder.Core.Conversation.GameSession` (modified — wire growth events)

## Maturity
Prototype

---

## SessionShadowTracker (resolves VC-58)

**File**: `src/Pinder.Core/Conversation/SessionShadowTracker.cs`

```csharp
public sealed class SessionShadowTracker
{
    private readonly StatBlock _baseStats;
    private readonly Dictionary<ShadowStatType, int> _sessionGrowth;

    // Tracking counters for growth conditions
    private int _tropeTrapsThisSession;
    private int _consecutiveSameStatCount;
    private StatType? _lastStat;
    private int _consecutiveHighestPickCount;
    private bool _hasUsedChaos;
    private bool _hasHonestySuccess;
    private readonly HashSet<StatType> _statsUsedThisSession;

    public SessionShadowTracker(StatBlock baseStats)
    {
        _baseStats = baseStats ?? throw new ArgumentNullException(nameof(baseStats));
        _sessionGrowth = new Dictionary<ShadowStatType, int>();
        _statsUsedThisSession = new HashSet<StatType>();
        // ... initialize counters
    }

    /// <summary>
    /// Get effective shadow value = base + session growth.
    /// </summary>
    public int GetEffectiveShadow(ShadowStatType shadow)
    {
        int baseVal = _baseStats.GetShadow(shadow);
        _sessionGrowth.TryGetValue(shadow, out int growth);
        return baseVal + growth;
    }

    /// <summary>
    /// Grow a shadow stat by the given amount. Records the event description.
    /// Returns the description string (e.g., "Dread +1 (Wit nat 1)").
    /// </summary>
    public string Grow(ShadowStatType shadow, int amount, string reason)
    {
        if (!_sessionGrowth.ContainsKey(shadow))
            _sessionGrowth[shadow] = 0;
        _sessionGrowth[shadow] += amount;
        string desc = $"{shadow} +{amount} ({reason})";
        _growthLog.Add(desc);
        return desc;
    }

    /// <summary>All growth events this session (for TurnResult.ShadowGrowthEvents).</summary>
    public IReadOnlyList<string> GrowthLog => _growthLog;

    /// <summary>Record a stat usage for tracking growth conditions.</summary>
    public void RecordStatUsed(StatType stat, bool wasSuccess, bool wasNat1, FailureTier tier) { ... }

    /// <summary>Record a trope trap activation.</summary>
    public void RecordTropeTrap() { _tropeTrapsThisSession++; }

    /// <summary>Check if 3+ trope traps this session (Madness growth trigger).</summary>
    public bool HasThreeOrMoreTropeTraps => _tropeTrapsThisSession >= 3;

    /// <summary>Was the same stat used 3 turns in a row? (Fixation trigger)</summary>
    public bool IsThreeConsecutiveSameStat => _consecutiveSameStatCount >= 3;

    /// <summary>Has Chaos ever been used? (Fixation trigger on game end if false)</summary>
    public bool HasUsedChaos => _hasUsedChaos;

    /// <summary>Has any Honesty roll succeeded? (Denial trigger on DateSecured if false)</summary>
    public bool HasHonestySuccess => _hasHonestySuccess;

    /// <summary>How many distinct stats used this session (Fixation offset: 4+ → -1 Fixation)</summary>
    public int DistinctStatsUsed => _statsUsedThisSession.Count;

    private readonly List<string> _growthLog = new List<string>();
}
```

---

## Growth Events (wired into GameSession)

### Per-roll growth events (in ResolveTurnAsync):

| Condition | Shadow | Amount | Trigger point |
|-----------|--------|--------|---------------|
| Nat 1 on Charm | Madness | +1 | After roll |
| Nat 1 on Wit | Dread | +1 | After roll |
| Nat 1 on Honesty | Denial | +1 | After roll |
| Nat 1 on Chaos | Fixation | +1 | After roll |
| Catastrophic Wit fail (miss 10+) | Dread | +1 | After roll |
| TropeTrap tier roll | Track count | — | After roll |
| 3+ trope traps in session | Madness | +1 | When count reaches 3 |
| Same stat 3 turns in a row | Fixation | +1 | After 3rd consecutive |
| Highest-% option 3 turns in a row | Fixation | +1 | After 3rd consecutive |

### End-of-session growth events (when game ends):

| Condition | Shadow | Amount |
|-----------|--------|--------|
| Interest hits 0 (unmatch) | Dread | +2 |
| Ghosted | Dread | +1 |
| DateSecured without Honesty success | Denial | +1 |
| Never used Chaos in whole conversation | Fixation | +1 |
| 4+ different stats used | Fixation | -1 (offset) |

---

## Behavioural Contract
- `SessionShadowTracker` is per-session — created fresh for each `GameSession`
- `StatBlock` remains immutable — SessionShadowTracker wraps it
- Growth events are recorded in `_growthLog` and reported via `TurnResult.ShadowGrowthEvents`
- When GameSession needs to compute effective stats for rolls, it creates a temporary StatBlock with adjusted shadow values OR passes the shadow-adjusted effective modifier
- End-of-session events fire when `GameSession._ended` becomes true

## Dependencies
- #78 (TurnResult.ShadowGrowthEvents field)
- #58 resolution (this IS the resolution — SessionShadowTracker)

## Consumers
- GameSession (owns and queries the tracker)
- #45 (shadow thresholds check `GetEffectiveShadow`)
- #53 (OpponentTimingCalculator reads shadow values)
