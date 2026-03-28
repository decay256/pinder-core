# Contract: Issue #45 — Shadow Threshold Effects

## Component
`Pinder.Core.Conversation.GameSession` (modified — threshold checks in StartTurnAsync)
`Pinder.Core.Conversation.InterestMeter` (modified — configurable starting value, resolves VC-79)

## Maturity
Prototype

---

## InterestMeter Change (resolves VC-79)

Add constructor overload:

```csharp
public InterestMeter(int startingValue)
{
    Current = Math.Max(Min, Math.Min(Max, startingValue));
}
```

GameSession uses this when Dread ≥ 18:
```csharp
int startingInterest = shadowTracker.GetEffectiveShadow(ShadowStatType.Dread) >= 18
    ? 8
    : InterestMeter.StartingValue;
_interest = new InterestMeter(startingInterest);
```

---

## Threshold Effects

Checked in `GameSession.StartTurnAsync()` and applied to the turn:

| Shadow | At 6 | At 12 | At 18+ |
|--------|------|-------|--------|
| Dread | Add existential flavor flag to DialogueContext | Wit rolls at disadvantage | Starting Interest 8 (applied at session start) |
| Madness | Add glitch flag to DialogueContext (cosmetic) | Charm rolls at disadvantage | One option replaced with unhinged text (flag to LLM) |
| Denial | Add "I'm fine" leak flag | Honesty rolls at disadvantage | Honesty options stop appearing (filter from LLM results) |
| Fixation | Add pattern-repeat flag | Chaos rolls at disadvantage | Must pick same stat as last turn (enforce in ResolveTurnAsync) |
| Overthinking | Always show Interest number (flag in TurnStart) | SA rolls at disadvantage | Show opponent inner monologue (flag in OpponentContext) |
| Horniness | Rizz options appear more (flag) | One option always unwanted Rizz | ALL options become Rizz (handled by #51) |

### Implementation approach

**Threshold 1 (≥ 6)**: Add boolean flags to `DialogueContext` (e.g., `HasDreadFlavor`, `HasMadnessGlitch`). These are hints for the LLM — the engine sets them, the LLM adapter decides what to do.

**Threshold 2 (≥ 12)**: Concrete mechanical effects — force disadvantage on specific stats.

In `GameSession.StartTurnAsync()`:
```csharp
// Shadow threshold disadvantage
if (shadowTracker.GetEffectiveShadow(ShadowStatType.Dread) >= 12)
    _shadowDisadvantageStats.Add(StatType.Wit);
if (shadowTracker.GetEffectiveShadow(ShadowStatType.Madness) >= 12)
    _shadowDisadvantageStats.Add(StatType.Charm);
// etc.
```

Then in `ResolveTurnAsync`, when computing advantage/disadvantage:
```csharp
bool hasDisadvantage = _interest.GrantsDisadvantage || _shadowDisadvantageStats.Contains(chosenOption.Stat);
```

**Threshold 3 (≥ 18)**: Complex behavioral changes. For prototype:
- Dread ≥ 18: starting interest 8 (handled at construction)
- Fixation ≥ 18: enforce same stat as last turn in ResolveTurnAsync (throw or auto-select)
- Others: flags passed to LLM adapter

---

## Behavioural Contract
- Shadow thresholds are checked against `SessionShadowTracker.GetEffectiveShadow()` (base + session growth)
- Threshold 1 (≥ 6): LLM flavor hints only — no mechanical effect except Horniness (handled by #51)
- Threshold 2 (≥ 12): Forced disadvantage on specific stat — mechanical
- Threshold 3 (≥ 18): Behavioral changes — mix of mechanical and LLM-driven
- `InterestMeter` now accepts optional starting value
- Thresholds are re-evaluated every turn (shadow can grow mid-session)

## Dependencies
- #44 (SessionShadowTracker must exist for GetEffectiveShadow)
- #79 resolution (InterestMeter configurable start — resolved by this issue)

## Consumers
- GameSession (applies threshold effects)
- #51 (Horniness thresholds)
