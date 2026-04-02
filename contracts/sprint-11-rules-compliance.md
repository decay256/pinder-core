# Contract: Sprint 11 — Rules Compliance Fixes

## Architecture Overview

This sprint continues the existing architecture with **no structural changes**. All 10 issues fix or complete game-rules logic within the existing module boundaries. The dependency graph is strictly `Pinder.Core` internal — no `Pinder.LlmAdapters` changes.

**Existing architecture summary**: Pinder.Core is a zero-dependency .NET Standard 2.0 RPG engine. `GameSession` orchestrates single-conversation turns, delegating to `RollEngine` (stateless roll resolution), `InterestMeter` (interest tracking), `TrapState` (trap lifecycle), `SessionShadowTracker` (mutable shadow stats), `ComboTracker` (combo sequences), `XpLedger` (XP accumulation), and `ILlmAdapter` (LLM abstraction). State flows in via constructor params; per-turn state is owned by `GameSession`. All data models are immutable value objects. Data loading is via hand-rolled `JsonParser` → repository classes.

**Components being extended:**
- `Data/` — new trap JSON data file (#265)
- `Rolls/FailureScale` — fix interest deltas (#266)
- `Rolls/RollEngine` — add trap activation for Catastrophe + Legendary (#267)
- `Conversation/GameSession` — 7 issues touch this file (#268, #269, #260, #270, #271, #272, #273)

**Implicit assumptions for all implementers:**
- netstandard2.0, LangVersion 8.0 — no `record` types, no generic `Enum.Parse<T>`
- Zero NuGet dependencies in Pinder.Core
- Nullable reference types enabled
- All 1146 existing tests must continue to pass
- `ApplyGrowth()` throws on negative amounts — use `ApplyOffset()` for shadow reductions (#279)
- `AddExternalBonus()` is DEPRECATED — use `externalBonus` param on `RollEngine.Resolve()` (#276)
- Read/Recover are self-contained — they do NOT call `StartTurnAsync()`

---

## Separation of Concerns Map

- Data (Pinder.Core.Data)
  - Responsibility:
    - JSON parsing and deserialization
    - Trap definition loading from traps.json
  - Interface:
    - `JsonTrapRepository(string json) : ITrapRegistry`
    - `GetTrap(StatType) → TrapDefinition?`
  - Must NOT know:
    - Roll resolution logic
    - Game session orchestration
    - Interest tracking

- Rolls (Pinder.Core.Rolls)
  - Responsibility:
    - d20 roll resolution
    - Failure tier determination
    - Trap activation during rolls
    - Success/failure interest scale
  - Interface:
    - `RollEngine.Resolve()`
    - `RollEngine.ResolveFixedDC()`
    - `FailureScale.GetInterestDelta()`
    - `RollResult` (value object)
  - Must NOT know:
    - Interest meter state
    - Game session orchestration
    - Shadow tracking
    - Momentum logic

- Conversation/GameSession
  - Responsibility:
    - Turn orchestration (Speak/Read/Recover/Wait)
    - Momentum tracking and bonus application
    - Horniness roll and time-of-day modifier
    - Shadow growth/reduction trigger evaluation
    - Crit advantage tracking
    - Denial growth on skipped Honesty
    - Madness T3 option replacement
    - Shadow-based disadvantage on all roll paths
  - Interface:
    - `StartTurnAsync() → TurnStart`
    - `ResolveTurnAsync(int) → TurnResult`
    - `ReadAsync() → ReadResult`
    - `RecoverAsync() → RecoverResult`
    - `Wait()`
  - Must NOT know:
    - LLM implementation details
    - Roll math internals
    - Trap JSON format

- Stats/SessionShadowTracker
  - Responsibility:
    - Mutable shadow delta tracking
    - Growth event logging
  - Interface:
    - `ApplyGrowth(shadow, amount, reason)`
    - `ApplyOffset(shadow, delta, reason)`
    - `GetEffectiveShadow(shadow) → int`
    - `DrainGrowthEvents() → IReadOnlyList<string>`
  - Must NOT know:
    - When to apply growth (GameSession decides)
    - Roll resolution
    - Interest state

---

## Per-Issue Interface Definitions

### #265 — Create data/traps/traps.json

**Component:** `data/traps/traps.json` (new file) + `data/traps/trap-schema.json` (new file)

**Critical: JSON schema must match `JsonTrapRepository.ParseTrap()` parser** (vision concern #274).

The issue's JSON schema uses nested objects (`mechanical_effect.type`, `prompt_taint.llm_instruction`) and PascalCase stat names (`"Charm"`). The parser expects flat fields and lowercase:

```
Parser expects:
  "id": string (required)
  "stat": string (required, lowercase: "charm"|"rizz"|"honesty"|"chaos"|"wit"|"self_awareness")
  "effect": string (required: "disadvantage"|"stat_penalty"|"opponent_dc_increase")
  "effect_value": int (required, 0 for Disadvantage)
  "duration_turns": int (default 3)
  "llm_instruction": string (required)
  "clear_method": string (optional, default "")
  "nat1_bonus": string (optional, default "")
```

**Correct JSON format** (example for one trap):
```json
{
  "id": "cringe",
  "stat": "charm",
  "effect": "disadvantage",
  "effect_value": 0,
  "duration_turns": 1,
  "llm_instruction": "You are aware of how you're coming across...",
  "clear_method": "SA vs DC 12",
  "nat1_bonus": ""
}
```

**Mapping from issue #265 schema to parser-compatible schema:**

| Trap ID | stat | effect | effect_value | duration_turns |
|---------|------|--------|-------------|----------------|
| cringe | charm | disadvantage | 0 | 1 |
| creep | rizz | stat_penalty | 2 | 2 |
| overshare | honesty | opponent_dc_increase | 2 | 1 |
| unhinged | chaos | disadvantage | 0 | 1 |
| pretentious | wit | opponent_dc_increase | 3 | 1 |
| spiral | self_awareness | disadvantage | 0 | 2 |

**Test contract:**
- Load `traps.json` via `new JsonTrapRepository(jsonString)`
- Assert 6 traps loaded, one per `StatType`
- Assert each trap's `Id`, `Stat`, `Effect`, `EffectValue`, `DurationTurns`, `LlmInstruction` are correct

**Dependencies:** None
**Consumers:** All GameSession tests that need a real trap registry

---

### #266 — FailureScale interest deltas fix

**Component:** `Rolls/FailureScale.cs`

**Change:** Update `GetInterestDelta()` return values:

```
Current → Correct (rules-v3.4 §5):
  Fumble:      -1 → -1 (no change)
  Misfire:     -2 → -1
  TropeTrap:   -3 → -2
  Catastrophe: -4 → -3
  Legendary:   -5 → -4
```

**Behavioral contract:**
- `GetInterestDelta(result with Tier=Misfire)` returns `-1`
- `GetInterestDelta(result with Tier=TropeTrap)` returns `-2`
- `GetInterestDelta(result with Tier=Catastrophe)` returns `-3`
- `GetInterestDelta(result with Tier=Legendary)` returns `-4`
- Success (Tier=None) still returns `0`

**Test contract:** Update `RulesConstantsTests` to assert new values. Check no other tests depend on the old values (many likely do — grep for expected deltas).

**Dependencies:** None
**Consumers:** `GameSession.ResolveTurnAsync()`

---

### #267 — Catastrophe + Legendary trap activation

**Component:** `Rolls/RollEngine.cs` → `ResolveFromComponents()` (private method)

**Change:** Add trap activation for Catastrophe (miss ≥10) AND Legendary (Nat 1) tiers. Currently only TropeTrap (miss 6-9) activates traps.

**Behavioral contract (verified against source):**

Current `ResolveFromComponents` flow:
```
if usedRoll == 1 → Legendary (NO trap activation)
else if success  → None
else:
  miss ≤ 2  → Fumble
  miss ≤ 5  → Misfire
  miss ≤ 9  → TropeTrap + activate trap
  miss ≥ 10 → Catastrophe (NO trap activation)
```

New flow:
```
if usedRoll == 1 → Legendary + activate trap (per vision #275)
else if success  → None
else:
  miss ≤ 2  → Fumble
  miss ≤ 5  → Misfire
  miss ≤ 9  → TropeTrap + activate trap
  miss ≥ 10 → Catastrophe + activate trap
```

Trap activation logic (same for all three tiers):
```csharp
if (!attackerTraps.IsActive(stat))
{
    newTrap = trapRegistry.GetTrap(stat);
    if (newTrap != null) attackerTraps.Activate(newTrap);
}
```

**Vision concern #275:** Legendary (Nat 1) MUST also activate traps. The Nat 1 check (`usedRoll == 1`) happens before the miss-margin calculation, so trap activation must be added to the Legendary branch directly.

**Test contract:**
- Roll miss by 12 → Catastrophe tier, trap activated
- Roll Nat 1 → Legendary tier, trap activated
- Existing TropeTrap test (miss 6-9) unchanged
- Trap not activated if already active on stat (all three tiers)

**Dependencies:** None
**Consumers:** `GameSession.ResolveTurnAsync()`

---

### #268 — Momentum as roll bonus (not interest delta)

**Component:** `Conversation/GameSession.cs`

**Change:** Momentum bonus applies to the d20 roll (via `externalBonus`), not directly to interest delta.

**Current (incorrect) flow:**
```
ResolveTurnAsync:
  rollResult = RollEngine.Resolve(..., externalBonus: tellBonus + callbackBonus + tripleCombo)
  interestDelta = SuccessScale/FailureScale(rollResult)
  if success: _momentumStreak++; interestDelta += GetMomentumBonus(streak)  ← WRONG
```

**New flow:**
```
StartTurnAsync:
  _pendingMomentumBonus = GetMomentumBonus(_momentumStreak)  // compute from current streak

ResolveTurnAsync:
  externalBonus = tellBonus + callbackBonus + tripleCombo + _pendingMomentumBonus
  rollResult = RollEngine.Resolve(..., externalBonus)  // momentum affects the roll
  interestDelta = SuccessScale/FailureScale(rollResult)
  // NO interestDelta += momentum — it's already in the roll
  if success: _momentumStreak++
  else: _momentumStreak = 0
```

**New field:** `private int _pendingMomentumBonus;`

**Vision concern #276:** Must NOT use `AddExternalBonus()` (deprecated). Must pass through `RollEngine.Resolve(externalBonus)` parameter.

**Behavioral contract:**
- 3-win streak gives +2 to d20 total (not +2 interest)
- Momentum bonus computed from streak BEFORE the roll
- Streak increments on success, resets on failure (after the roll)
- `GetMomentumBonus()` unchanged: 3-4→+2, 5+→+3

**Dependencies:** None (but affects same file as #269, #260, #270-273)
**Consumers:** N/A (internal to GameSession)

---

### #269 — Horniness always rolled

**Component:** `Conversation/GameSession.cs` → constructor

**Change:** Roll Horniness (1d10) in every session. Time-of-day modifier only when clock is available.

**Current (incorrect):**
```csharp
if (_clock != null)
{
    int horninessRoll = _dice.Roll(10);
    int todModifier = _clock.GetHorninessModifier();
    _sessionHorniness = Math.Max(0, horninessRoll + todModifier);
}
// else: _sessionHorniness = 0 (field default)
```

**New:**
```csharp
int horninessRoll = _dice.Roll(10);
int todModifier = _clock?.GetHorninessModifier() ?? 0;
_sessionHorniness = Math.Max(0, horninessRoll + todModifier);
```

**Behavioral contract:**
- Session without clock: `_sessionHorniness` = `dice.Roll(10)` (clamped to ≥0, always true for 1d10)
- Session with clock: `_sessionHorniness` = `Max(0, dice.Roll(10) + clock.GetHorninessModifier())`
- Downstream mechanics (≥6, ≥12, ≥18 thresholds) unaffected

**Test contract:**
- Session without clock, dice returns 7 → `_sessionHorniness` = 7
- Session with clock returning -5 modifier, dice returns 3 → `_sessionHorniness` = 0

**Dependencies:** None
**Consumers:** N/A (internal to GameSession)

---

### #260 — Read/Recover shadow disadvantage

**Component:** `Conversation/GameSession.cs` → `ReadAsync()`, `RecoverAsync()`

**Change:** Both methods currently only check `_interest.GrantsDisadvantage` for disadvantage. They must also check `_shadowDisadvantagedStats` for SA-specific shadow disadvantage (Overthinking ≥T2).

**Current (incomplete):**
```csharp
bool hasDisadvantage = _interest.GrantsDisadvantage;
// Shadow disadvantage from Overthinking NOT applied
```

**New:**
```csharp
bool hasDisadvantage = _interest.GrantsDisadvantage;
if (_shadowDisadvantagedStats != null
    && _shadowDisadvantagedStats.Contains(StatType.SelfAwareness))
{
    hasDisadvantage = true;
}
```

**Important note:** `_shadowDisadvantagedStats` is computed in `StartTurnAsync()`. Read/Recover are self-contained and may not call `StartTurnAsync()` first. The implementer must either:
1. Recompute shadow thresholds at the start of Read/Recover (preferred — guarantees fresh data), OR
2. Also compute `_shadowDisadvantagedStats` in the constructor (weaker — misses mid-session shadow growth)

**Recommended approach:** Extract shadow threshold computation into a private helper method called from both `StartTurnAsync()` and the start of `ReadAsync()`/`RecoverAsync()`.

**Behavioral contract:**
- Player with Overthinking ≥12 (T2+) → SA rolls in Read/Recover have disadvantage
- Player with Overthinking <12 → no shadow disadvantage on Read/Recover
- Interest-based disadvantage still applies independently

**Test contract:**
- Session with Overthinking=12 → ReadAsync rolls with disadvantage (2 dice rolled, lower used)
- Session with Overthinking=11 → ReadAsync rolls normally

**Dependencies:** None
**Consumers:** N/A (internal to GameSession)

---

### #270 — 5 missing shadow reduction events

**Component:** `Conversation/GameSession.cs`

**Critical: Use `ApplyOffset()`, NOT `ApplyGrowth()`** (vision concern #279). `ApplyGrowth` throws on negative amounts.

**5 reductions to implement:**

1. **Date secured → Dread −1**
   - Location: `EvaluateEndOfGameShadowGrowth()`, when `outcome == GameOutcome.DateSecured`
   - Code: `_playerShadows.ApplyOffset(ShadowStatType.Dread, -1, "Date secured")`

2. **Honesty success at Interest ≥15 → Denial −1**
   - Location: `EvaluatePerTurnShadowGrowth()`, after trigger 6 (Honesty success tracking)
   - Condition: `chosenOption.Stat == StatType.Honesty && rollResult.IsSuccess && interestAfter >= 15`
   - Code: `_playerShadows.ApplyOffset(ShadowStatType.Denial, -1, "Honesty success at high interest")`

3. **Recovering from trope trap → Madness −1**
   - Location: `RecoverAsync()`, on successful recovery
   - Code: `_playerShadows?.ApplyOffset(ShadowStatType.Madness, -1, "Recovered from trope trap")`

4. **Winning despite Overthinking disadvantage → Overthinking −1**
   - Location: `ResolveTurnAsync()`, after roll resolution
   - Condition: roll used SA AND Overthinking gave disadvantage AND roll succeeded
   - More precisely: `resolveHasDisadvantage == true` due to `_shadowDisadvantagedStats` containing `chosenOption.Stat` AND the stat's paired shadow is Overthinking AND `rollResult.IsSuccess`
   - Simpler check: chosen stat is SA, `_shadowDisadvantagedStats?.Contains(StatType.SelfAwareness) == true`, and success
   - Code: `_playerShadows.ApplyOffset(ShadowStatType.Overthinking, -1, "Succeeded despite Overthinking disadvantage")`

5. **4+ different stats used → Fixation −1** — Already implemented (trigger 13 in `EvaluateEndOfGameShadowGrowth`)

**Behavioral contract per reduction:**
- Each calls `ApplyOffset(shadow, -1, reason)` not `ApplyGrowth`
- Each produces a growth event string via `ApplyOffset` return value
- Delta can go negative (e.g., Dread starts at 0, gets -1 → delta becomes -1)

**Test contract (per reduction):**
- Setup shadow tracker with known values
- Trigger the condition
- Assert `GetDelta(shadow)` decreased by 1
- Assert no `ArgumentOutOfRangeException`

**Dependencies:** None
**Consumers:** N/A (internal to GameSession)

---

### #271 — Nat 20 grants advantage on next roll

**Component:** `Conversation/GameSession.cs`

**New field:** `private bool _pendingCritAdvantage;`

**Changes (including vision concern #280 — Read/Recover must participate):**

1. **ResolveTurnAsync:** After roll, if `rollResult.IsNatTwenty`, set `_pendingCritAdvantage = true`
2. **StartTurnAsync:** Before computing advantage, if `_pendingCritAdvantage`, set `hasAdvantage = true`, then `_pendingCritAdvantage = false`
3. **ReadAsync:** Before rolling, if `_pendingCritAdvantage`, set `hasAdvantage = true`, then `_pendingCritAdvantage = false`. After roll, if Nat 20, set `_pendingCritAdvantage = true`
4. **RecoverAsync:** Same as ReadAsync

**Behavioral contract:**
- Nat 20 on any action → advantage on the very next roll
- Advantage is consumed after one roll (cleared)
- Stacks with interest-based advantage (both → still advantage)
- Crit advantage from Speak carries into Read/Recover
- Crit advantage from Read/Recover carries into Speak

**Test contract:**
- Speak Nat 20 → next Speak has advantage → following Speak does not
- Speak Nat 20 → next Read has advantage
- Read Nat 20 → next Speak has advantage
- Crit advantage + interest advantage → still advantage (no double-roll)

**Dependencies:** None
**Consumers:** N/A (internal to GameSession)

---

### #272 — Denial +1 when skipping Honesty

**Component:** `Conversation/GameSession.cs` → `ResolveTurnAsync()`

**Change:** After option is chosen, check if Honesty was available but not selected.

**Implementation location:** In `ResolveTurnAsync`, after `chosenOption` is determined but before the roll (order doesn't matter for shadow growth — it's tracked, not applied to the roll):

```csharp
bool honestyAvailable = _currentOptions.Any(o => o.Stat == StatType.Honesty);
bool choseHonesty = chosenOption.Stat == StatType.Honesty;
if (honestyAvailable && !choseHonesty && _playerShadows != null)
{
    _playerShadows.ApplyGrowth(ShadowStatType.Denial, 1,
        "Skipped Honesty option");
}
```

**Behavioral contract:**
- Options include Honesty, player picks non-Honesty → Denial +1
- Options include Honesty, player picks Honesty → no Denial growth
- Options don't include Honesty (e.g., Denial T3 removed it) → no Denial growth
- Uses `ApplyGrowth` (positive amount) — NOT `ApplyOffset`

**Test contract:**
- Options [Charm, Honesty, Wit], player picks index 0 (Charm) → Denial +1
- Options [Charm, Honesty, Wit], player picks index 1 (Honesty) → no Denial change
- Options [Charm, Wit, Rizz] (no Honesty) → no Denial change

**Dependencies:** Requires `_currentOptions` to be stored (already is — set in `StartTurnAsync`)
**Consumers:** N/A (internal to GameSession)

---

### #273 — Madness T3 option replacement

**Component:** `Conversation/GameSession.cs` → `StartTurnAsync()`

**Change:** When Madness shadow ≥18 (T3), replace one option's text with an unhinged marker. This instructs the LLM to generate unhinged text for that option.

**Implementation location:** After the existing Denial T3 / Fixation T3 blocks in `StartTurnAsync`.

**Design decision:** The option keeps its original `Stat` (mechanically unchanged), but is marked as unhinged. Two approaches:

**Option A (minimal — recommended for prototype):** Add `bool IsUnhinged` property to `DialogueOption`. GameSession sets it on one random option when Madness T3 is active. The LLM adapter reads this flag.

**Option B (simpler — even more minimal):** Prepend `[UNHINGED] ` to the `IntendedText` of one random option. No DTO change needed. LLM adapter sees the marker in text.

**Recommended:** Option A. The `DialogueOption` already has rich metadata (ComboName, HasTellBonus, HasWeaknessWindow). Adding `IsUnhinged` is consistent.

**DialogueOption change:**
```csharp
public bool IsUnhinged { get; }

// Constructor gains: bool isUnhinged = false
public DialogueOption(
    StatType stat,
    string intendedText,
    int? callbackTurnNumber = null,
    string? comboName = null,
    bool hasTellBonus = false,
    bool hasWeaknessWindow = false,
    bool isUnhinged = false)
```

**GameSession change (in StartTurnAsync after Denial T3 block):**
```csharp
if (shadowThresholds.TryGetValue(ShadowStatType.Madness, out int madTier) && madTier >= 3)
{
    int idx = _dice.Roll(options.Length) - 1; // random index
    var o = options[idx];
    options[idx] = new DialogueOption(o.Stat, o.IntendedText, o.CallbackTurnNumber,
        o.ComboName, o.HasTellBonus, o.HasWeaknessWindow, isUnhinged: true);
}
```

**Behavioral contract:**
- Madness ≥18 → exactly one option has `IsUnhinged = true`
- The unhinged option retains its original Stat (mechanical roll unchanged)
- If only 1 option exists, that option becomes unhinged
- Madness <18 → no options have `IsUnhinged`

**Test contract:**
- Session with Madness=18 → one option in TurnStart.Options has `IsUnhinged == true`
- Session with Madness=17 → no options have `IsUnhinged`
- Verify the unhinged option's Stat is unchanged

**Dependencies:** DialogueOption DTO change needed first (or simultaneously)
**Consumers:** `ILlmAdapter` implementations (read `IsUnhinged` flag)

---

## Implementation Strategy

### Recommended Order

**Wave 1 — Independent, zero cross-dependency:**
1. **#265** (traps.json) — data file only, no code changes
2. **#266** (FailureScale fix) — isolated static class
3. **#267** (Catastrophe/Legendary trap activation) — isolated in RollEngine

**Wave 2 — GameSession constructor/field changes:**
4. **#269** (Horniness always rolled) — constructor-only change
5. **#271** (Nat 20 crit advantage) — new field + touches StartTurn/ResolveTurn/Read/Recover

**Wave 3 — GameSession logic changes (depend on understanding from Wave 2):**
6. **#268** (Momentum as roll bonus) — ResolveTurnAsync rewrite of momentum flow
7. **#260** (Read/Recover shadow disadvantage) — touches ReadAsync/RecoverAsync
8. **#272** (Denial +1 skip Honesty) — small addition to ResolveTurnAsync
9. **#270** (Shadow reductions) — scattered across multiple methods
10. **#273** (Madness T3) — DialogueOption DTO change + StartTurnAsync

### Merge Conflict Risk (vision concern #277)

7 issues touch `GameSession.cs`. Sequential implementation is **mandatory**. The implementation order above minimizes conflict: each wave's changes are in different regions of the file.

**Recommended approach:** Implement all GameSession issues on a single branch if possible, or use strict sequential merging with rebasing.

### Tradeoffs

- **Prototype maturity**: No NFR requirements. Focus on correctness over performance.
- **GameSession size**: This file is already ~700 lines and growing. Sprint 11 adds ~100 more lines. This is acceptable for prototype but must be addressed before MVP (extract shadow evaluation, momentum, and crit tracking into helper classes).
- **DialogueOption DTO change (#273)**: Adding `IsUnhinged` is a minor breaking change to the constructor signature. The default value (`false`) ensures backward compatibility for all existing callers.

### Risk Mitigation

- **If tests break from FailureScale changes (#266):** Many tests may hardcode old interest deltas. Grep for `-2`, `-3`, `-4`, `-5` in test assertions. Update all affected tests.
- **If trap activation changes (#267) cause cascading test failures:** Tests that assert "no trap on Catastrophe" will need updating. These are correctness fixes, not regressions.
- **If momentum change (#268) is complex to implement:** The externalBonus pipeline already works for tell/callback/triple combo. Momentum is just another addend.

---

## NFR Checklist (Prototype)

- **Latency**: N/A (no network calls, pure computation)
- All other NFRs: deferred per prototype maturity

---

## Sprint Plan Changes

**SPRINT PLAN CHANGES:**

1. **Issue #265 JSON schema must be corrected** per vision concern #274. The implementer MUST use the flat schema that matches `JsonTrapRepository.ParseTrap()`, NOT the nested schema in the issue body. This contract file documents the correct schema above.

2. **Issue #267 must also cover Legendary tier** per vision concern #275. Trap activation must be added to the Nat 1 (Legendary) branch, not just Catastrophe.

3. **Issue #268 must NOT use `AddExternalBonus()`** per vision concern #276. Momentum flows through `RollEngine.Resolve(externalBonus)` parameter.

4. **Issue #270 must use `ApplyOffset()` for all reductions** per vision concern #279. `ApplyGrowth` throws on negative amounts.

5. **Issue #271 must also apply to Read/Recover** per vision concern #280. Crit advantage must be consumed and set by all action types.

6. **Sequential implementation is mandatory** per vision concern #277. Do not attempt parallel branches on GameSession issues.

No new issues needed — all vision concerns are addressed via implementation guidance in this contract.

---

## VERDICT: PROCEED WITH CHANGES

The sprint plan is sound. All 10 issues are well-scoped for single-agent implementation. The 6 vision concerns (#274, #275, #276, #277, #279, #280) are all addressed in this contract's per-issue guidance. The key constraint is sequential implementation of GameSession issues to avoid merge conflicts.
