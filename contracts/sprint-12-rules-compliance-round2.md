# Contract: Sprint 12 — Rules Compliance Round 2

## Architecture Overview

**This sprint continues the existing architecture with no structural changes.** Ten issues fix or complete game-rules logic within the existing module boundaries. Changes span `Pinder.Core` (InterestState enum, InterestMeter, RollEngine, RollResult, SuccessScale, GameSession, DialogueOption, JsonTrapRepository data) and `Pinder.LlmAdapters` (SessionDocumentBuilder, PromptTemplates). No new components. No new projects. No new dependencies.

**Existing architecture summary**: Pinder.Core is a zero-dependency .NET Standard 2.0 RPG engine. `GameSession` orchestrates single-conversation turns, delegating to `RollEngine` (stateless roll resolution), `InterestMeter`, `TrapState`, `SessionShadowTracker`, `ComboTracker`, and `XpLedger`. `Pinder.LlmAdapters` depends on `Pinder.Core` and implements `ILlmAdapter` via `AnthropicLlmAdapter`, using `SessionDocumentBuilder` for prompt assembly and `PromptTemplates` for instruction text constants. The dependency is strictly one-way: `LlmAdapters → Core`.

**Test baseline**: 1275 Core tests + 443 LlmAdapter tests = 1718 total. All must continue to pass.

---

## Separation of Concerns Map

- Rolls (Pinder.Core.Rolls)
  - Responsibility:
    - d20 roll resolution
    - Failure tier determination using FinalTotal
    - Success scale calculation using FinalTotal
    - Risk tier classification
  - Interface:
    - RollEngine.Resolve()
    - RollEngine.ResolveFixedDC()
    - RollResult (Total, FinalTotal, ExternalBonus, Tier)
    - SuccessScale.GetInterestDelta()
  - Must NOT know:
    - Interest tracking
    - Game session orchestration
    - LLM communication

- Conversation (Pinder.Core.Conversation)
  - Responsibility:
    - Interest meter with Lukewarm state
    - GameSession turn orchestration
    - Shadow threshold wiring to contexts
    - XP recording with risk-tier multiplier
    - Madness T3 option replacement
    - Triple combo bonus on Read/Recover
  - Interface:
    - InterestState enum (with Lukewarm)
    - InterestMeter.GetState()
    - GameSession.StartTurnAsync()
    - GameSession.ResolveTurnAsync()
    - GameSession.ReadAsync()
    - GameSession.RecoverAsync()
    - DialogueOption (with IsUnhingedReplacement)
    - DeliveryContext / OpponentContext (shadowThresholds)
  - Must NOT know:
    - Roll math internals
    - LLM HTTP transport
    - Prompt template content

- Data (Pinder.Core.Data)
  - Responsibility:
    - JSON parsing for traps
  - Interface:
    - JsonTrapRepository(string json)
    - ITrapRegistry
  - Must NOT know:
    - File I/O
    - Game session state

- LlmAdapters (Pinder.LlmAdapters)
  - Responsibility:
    - Prompt template content
    - Tell category reference table
    - Session document assembly
    - Shadow taint block rendering
  - Interface:
    - PromptTemplates.OpponentResponseInstruction
    - SessionDocumentBuilder.BuildShadowTaintBlock()
  - Must NOT know:
    - Game rules / roll math
    - Interest state transitions
    - Session lifecycle

---

## Per-Issue Interface Definitions

### #306 — traps.json schema mismatch

**Component**: `data/traps/traps.json`
**What changes**: Rewrite JSON to match `JsonTrapRepository` parser expectations (verified in source).

**Parser contract** (from `JsonTrapRepository.cs:84-104`):
```
Required fields per trap object:
  id:              string (e.g. "cringe")
  stat:            string, lowercase (charm|rizz|honesty|chaos|wit|self_awareness)
  effect:          string (disadvantage|stat_penalty|opponent_dc_increase)
  effect_value:    int (0 for disadvantage, penalty amount for others)
  duration_turns:  int (default 3 if missing)
  llm_instruction: string (required, non-empty)
Optional fields:
  clear_method:    string (default "")
  nat1_bonus:      string (default "")
```

**Verification**: Load file → `new JsonTrapRepository(json)` → `GetAll()` returns 6 traps.

**NOTE**: The current `data/traps/traps.json` already has the correct flat schema matching the parser. The fields are: `id`, `stat` (lowercase), `effect` (lowercase), `effect_value`, `duration_turns`, `llm_instruction`, `clear_method`, `nat1_bonus`. **This file appears to already be fixed.** The implementer should verify by writing a test that loads the file and asserts 6 traps parse successfully. If the test passes, the fix is confirmed.

**Dependencies**: None
**Consumers**: `JsonTrapRepository`, all trap-dependent tests

---

### #307 — Shadow taint tier vs raw value mismatch

**Component**: `src/Pinder.Core/Conversation/GameSession.cs` lines 248-260
**What changes**: Store raw shadow value instead of tier in `shadowThresholds` dictionary.

**Before** (line 259-260):
```csharp
int tier = ShadowThresholdEvaluator.GetThresholdLevel(effectiveVal);
shadowThresholds[shadow] = tier;
```

**After**:
```csharp
shadowThresholds[shadow] = effectiveVal;
```

**Interface impact**: `Dictionary<ShadowStatType, int>` values change from tier (0-3) to raw values (0-30+). All consumers must be checked:

1. `SessionDocumentBuilder.BuildShadowTaintBlock()` — already checks `> 5`, `> 6` → works correctly with raw values ✅
2. `DialogueContext.ShadowThresholds` — passed through, no interpretation in Core ✅
3. GameSession T3 checks (lines 321, 335) — currently check `tier >= 3`. Must be updated to check raw value `>= 18` (since T3 threshold = 18).

**Critical side effect**: Lines 321-335 in `StartTurnAsync()` use `shadowThresholds` for Fixation T3 and Denial T3 checks. These currently check `fixTier >= 3` and `denTier >= 3`. After this change, the dictionary holds raw values, so the checks must become `fixVal >= 18` and `denVal >= 18` (since `ShadowThresholdEvaluator` returns T3 for values ≥ 18).

**Corrected check values** (from `ShadowThresholdEvaluator`):
- T1: ≥ 6
- T2: ≥ 12
- T3: ≥ 18

So the T3 checks in StartTurnAsync become:
```csharp
if (shadowThresholds.TryGetValue(ShadowStatType.Fixation, out int fixVal) && fixVal >= 18 ...)
if (shadowThresholds.TryGetValue(ShadowStatType.Denial, out int denVal) && denVal >= 18 ...)
```

And the Horniness T3 line that reads `_sessionHorniness >= 18` is already using raw value and is unaffected.

**Dependencies**: None
**Consumers**: #308 (DeliveryContext/OpponentContext), #310 (Madness T3)

---

### #308 — DeliveryContext and OpponentContext missing shadowThresholds

**Component**: `src/Pinder.Core/Conversation/GameSession.cs` lines 575-630
**What changes**: Pass `shadowThresholds` to both context constructors.

For DeliveryContext (player perspective):
```csharp
var deliveryContext = new DeliveryContext(
    // ... existing params ...
    shadowThresholds: shadowThresholds);  // ADD
```

For OpponentContext (opponent perspective): Compute opponent shadow thresholds if `_opponentShadows` is available:
```csharp
Dictionary<ShadowStatType, int>? opponentShadowThresholds = null;
if (_opponentShadows != null)
{
    opponentShadowThresholds = new Dictionary<ShadowStatType, int>();
    foreach (ShadowStatType shadow in Enum.GetValues(typeof(ShadowStatType)))
    {
        int val = _opponentShadows.GetEffectiveShadow(shadow);
        if (val > 0) opponentShadowThresholds[shadow] = val;
    }
}
var opponentContext = new OpponentContext(
    // ... existing params ...
    shadowThresholds: opponentShadowThresholds);  // ADD
```

**Note**: `shadowThresholds` is a variable already computed earlier in `ResolveTurnAsync`. The variable is in scope at line 575. Verify the variable is available (it's computed in `StartTurnAsync` and stored in `_lastShadowThresholds` or recomputed — implementer must check scoping).

**SCOPING CONCERN**: `shadowThresholds` is computed in `StartTurnAsync()` but `DeliveryContext` is constructed in `ResolveTurnAsync()`. The implementer must either:
- (a) Store `shadowThresholds` as a field `_lastShadowThresholds` in StartTurnAsync, read it in ResolveTurnAsync, OR
- (b) Recompute shadow thresholds in ResolveTurnAsync

Option (a) is cleaner — compute once, use everywhere.

**Dependencies**: #307 must land first (so values are raw, not tier)
**Consumers**: `SessionDocumentBuilder.BuildShadowTaintBlock()` in LlmAdapters

---

### #309 — SuccessScale/failure tier/beatDcBy use Total not FinalTotal

**Component**: Three files
**What changes**:

1. **`RollEngine.cs:169`** — Change `int miss = dc - total;` to `int miss = dc - finalTotal;`
   - This means external bonuses reduce failure severity
   - `finalTotal = total + externalBonus` is already computed at line 165

2. **`SuccessScale.cs:25`** — Change margin calculation:
   ```csharp
   int margin = result.FinalTotal - result.DC;
   ```
   - Nat20 path is unchanged (already returns 4)

3. **`GameSession.cs:572`** — Change beatDcBy:
   ```csharp
   int beatDcBy = rollResult.IsSuccess
       ? rollResult.FinalTotal - rollResult.DC : 0;
   ```

4. **`RollResult.MissMargin`** (line 80) — Update for consistency:
   ```csharp
   public int MissMargin => IsSuccess ? 0 : DC - FinalTotal;
   ```

**Behavioral contract**:
- Pre: external bonus affects success/fail determination (already true)
- Post: external bonus also affects failure tier severity, success scale margin, and beatDcBy
- Invariant: when externalBonus = 0, all behavior is identical to current

**Dependencies**: None (but implementers of #312 depend on this)
**Consumers**: GameSession, PromptTemplates (delivery quality)

---

### #310 — Madness T3 unhinged option replacement

**Component**: `DialogueOption.cs` + `GameSession.cs` (StartTurnAsync)
**What changes**:

1. **DialogueOption** — Add `IsUnhingedReplacement` property:
   ```csharp
   public bool IsUnhingedReplacement { get; }
   ```
   Constructor gains optional parameter `bool isUnhingedReplacement = false` (backward-compatible).

2. **GameSession.StartTurnAsync()** — After existing Denial T3 block (~line 345), before Horniness T3 block:
   ```csharp
   // Madness T3: replace one random option with unhinged marker
   if (shadowThresholds != null
       && shadowThresholds.TryGetValue(ShadowStatType.Madness, out int madVal)
       && madVal >= 18
       && options.Length > 0)
   {
       int idx = _dice.Roll(options.Length) - 1;
       var o = options[idx];
       options[idx] = new DialogueOption(
           o.Stat, o.IntendedText, o.CallbackTurnNumber,
           o.ComboName, o.HasTellBonus, o.HasWeaknessWindow,
           isUnhingedReplacement: true);
   }
   ```

**Note on threshold check**: After #307 lands, `shadowThresholds` contains raw values. Madness T3 = raw value ≥ 18. This is consistent with the Fixation/Denial T3 checks updated in #307.

**Dependencies**: #307 (raw values in shadowThresholds)
**Consumers**: LLM adapter (reads `IsUnhingedReplacement` to generate unhinged text)

---

### #311 — Tell categories in opponent response prompt

**Component**: `src/Pinder.LlmAdapters/PromptTemplates.cs`
**What changes**: Add tell category reference table to `OpponentResponseInstruction`.

Insert after the "Rules for signals:" section:
```
Tell category reference (use ONLY these mappings):
- Opponent compliments player → TELL: HONESTY
- Opponent asks personal question → TELL: HONESTY or SELF_AWARENESS
- Opponent makes joke → TELL: WIT or CHAOS
- Opponent shares vulnerability → TELL: HONESTY
- Opponent pulls back/guards → TELL: SELF_AWARENESS
- Opponent tests/challenges → TELL: WIT or CHAOS
- Opponent sends short reply → TELL: CHARM or CHAOS
- Opponent flirts → TELL: RIZZ or CHARM
- Opponent changes subject → TELL: CHAOS
- Opponent goes quiet/silent → TELL: SELF_AWARENESS
```

**Dependencies**: None
**Consumers**: AnthropicLlmAdapter (uses the constant)

---

### #312 — Triple combo bonus on Read/Recover

**Component**: `src/Pinder.Core/Conversation/GameSession.cs` (ReadAsync + RecoverAsync)
**What changes**: Pass triple bonus as `externalBonus` parameter (per #315 concern).

In `ReadAsync()` (~line 971), change:
```csharp
// BEFORE:
_comboTracker.ConsumeTripleBonus();
// ... later ...
var roll = RollEngine.ResolveFixedDC(
    StatType.SelfAwareness, _player.Stats, 12,
    _traps, _player.Level, _trapRegistry, _dice,
    hasAdvantage, hasDisadvantage);
```

```csharp
// AFTER:
int tripleBonus = _comboTracker.ConsumeTripleBonus();
var roll = RollEngine.ResolveFixedDC(
    StatType.SelfAwareness, _player.Stats, 12,
    _traps, _player.Level, _trapRegistry, _dice,
    hasAdvantage, hasDisadvantage,
    externalBonus: tripleBonus);
```

Same pattern in `RecoverAsync()` (~line 1083).

**Critical**: Must use `externalBonus` parameter, NOT `AddExternalBonus()`. The parameter flows through `ResolveFromComponents` and affects failure tier determination. `AddExternalBonus()` is deprecated and doesn't retroactively change the tier.

**Dependencies**: #309 should land first (so FinalTotal is used for tier determination)
**Consumers**: ReadResult, RecoverResult

---

### #313 — Lukewarm InterestState

**Component**: `InterestState.cs` + `InterestMeter.cs` + `SessionDocumentBuilder.cs`
**What changes**:

1. **InterestState.cs** — Add `Lukewarm` between `Bored` and `Interested`:
   ```csharp
   public enum InterestState
   {
       Unmatched,    // 0
       Bored,        // 1-4
       Lukewarm,     // 5-9
       Interested,   // 10-15
       VeryIntoIt,   // 16-20
       AlmostThere,  // 21-24
       DateSecured   // 25
   }
   ```

2. **InterestMeter.GetState()** — Split the 5-15 range:
   ```csharp
   if (Current <= 4)  return InterestState.Bored;
   if (Current <= 9)  return InterestState.Lukewarm;
   if (Current <= 15) return InterestState.Interested;
   ```

3. **InterestMeter.GrantsAdvantage** — No change needed (VeryIntoIt/AlmostThere only).
4. **InterestMeter.GrantsDisadvantage** — No change needed (Bored only).

5. **SessionDocumentBuilder** — Line 43 already shows "Lukewarm 🤔" text. Verify the interest label logic handles the new state. The `GetInterestBehaviourBlock` uses raw interest values (not enum), so it's unaffected.

6. **Test impact**: Any test asserting `InterestState.Interested` for values 5-9 must be updated to assert `InterestState.Lukewarm`.

**Dependencies**: None
**Consumers**: GameSession (ghost trigger), LlmAdapters (interest label), UI

---

### #314 — XP risk-tier multiplier

**Component**: `src/Pinder.Core/Conversation/GameSession.cs` (RecordRollXp)
**What changes**: Apply multiplier based on `rollResult.RiskTier`:

```csharp
private void RecordRollXp(RollResult rollResult)
{
    if (rollResult.IsNatTwenty)
    {
        _xpLedger.Record("Nat20", 25);
    }
    else if (rollResult.IsNatOne)
    {
        _xpLedger.Record("Nat1", 10);
    }
    else if (rollResult.IsSuccess)
    {
        int baseXp;
        if (rollResult.DC <= 13) baseXp = 5;
        else if (rollResult.DC <= 17) baseXp = 10;
        else baseXp = 15;

        float multiplier;
        switch (rollResult.RiskTier)
        {
            case RiskTier.Medium: multiplier = 1.5f; break;
            case RiskTier.Hard:   multiplier = 2.0f; break;
            case RiskTier.Bold:   multiplier = 3.0f; break;
            default:              multiplier = 1.0f; break;  // Safe
        }
        int xp = (int)System.Math.Round(baseXp * multiplier);
        _xpLedger.Record("Success_" + rollResult.RiskTier, xp);
    }
    else
    {
        _xpLedger.Record("Failure", 2);
    }
}
```

**Note**: C# 8.0 on netstandard2.0 supports switch expressions, but a switch statement is safer for clarity. `Math.Round` uses MidpointRounding.ToEven by default — `(int)Math.Round(7.5f)` = 8. This is acceptable for XP.

**Dependencies**: None
**Consumers**: XpLedger, host (reads XP total)

---

### #315 — Vision concern (advisory for #312)

**Component**: No code changes — this is an advisory that #312 must use `externalBonus` parameter, not `AddExternalBonus()`.

**Resolution**: Folded into #312 contract above. The #312 implementer MUST follow the `externalBonus` parameter approach.

**Dependencies**: None
**Consumers**: #312 implementer

---

## Implementation Strategy

### Wave Plan (respects dependencies)

**Wave 1** — Independent, no GameSession conflicts:
- #306 (traps.json data fix — or verification if already correct)
- #311 (PromptTemplates tell categories — LlmAdapters only)
- #313 (Lukewarm InterestState — enum + meter + test updates)

**Wave 2** — Core roll mechanics + GameSession (different methods):
- #309 (FinalTotal for tier/scale/beatDcBy — Rolls + GameSession)
- #314 (XP risk-tier multiplier — GameSession.RecordRollXp only)

**Wave 3** — Shadow system fixes (dependency chain):
- #307 (raw value in shadowThresholds — GameSession.StartTurnAsync)
  → then #308 (wire shadowThresholds to DeliveryContext/OpponentContext)
  → then #310 (Madness T3 unhinged replacement — uses raw value ≥ 18)

**Wave 4** — Depends on #309:
- #312 (Triple bonus on Read/Recover — uses externalBonus param)

**#315** — No code change; advisory consumed by #312.

### Tradeoffs

- **Storing raw shadow values instead of tiers (#307)**: This is the right call. Raw values give future flexibility for graduated taint intensity. The cost is updating 2-3 threshold checks from `>= 3` to `>= 18`, which is a small price.

- **XP record label change (#314)**: Changing from `Success_DC_Low` to `Success_Safe` etc. could break host-side XP parsing if anyone reads labels. Since this is prototype maturity, acceptable.

- **`MissMargin` property change (#309)**: Changing from `DC - Total` to `DC - FinalTotal` is a public API change. Any test or consumer relying on the old behavior will break. This is intentional — the old behavior was incorrect.

### Risk Mitigation

- **#307 side effects**: The threshold check changes in StartTurnAsync are the highest risk. If missed, Fixation/Denial T3 would never trigger (same bug pattern as the original taint issue). The contract explicitly documents the required changes.

- **#313 enum insertion**: Adding `Lukewarm` changes enum ordinal values for `Interested` through `DateSecured`. If anyone persists enum ints (not names), this is a breaking change. At prototype maturity, acceptable.

- **#309 test breakage**: Existing tests that assert specific failure tiers with externalBonus=0 are unaffected (behavior identical when bonus is 0). Tests that use AddExternalBonus() post-construction won't see tier changes — this is the correct (deprecated) behavior.

---

## Sprint Plan Changes

**SPRINT PLAN CHANGES**: None required. The 10 issues are well-scoped. The wave ordering handles dependencies correctly. #315 is an advisory consumed by #312 — no separate implementation needed.

**Implicit requirement**: #312 implementer MUST read #315 before implementing. The wave plan ensures #309 lands before #312.
