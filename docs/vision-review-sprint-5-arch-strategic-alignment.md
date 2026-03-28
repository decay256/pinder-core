# Vision Review — Sprint 5 Architecture Strategic Alignment

## Alignment: ⚠️

The architecture is **well-designed for prototype maturity** — the stateless evaluator pattern, Wave 0 prerequisites, and tiered implementation order all demonstrate solid engineering discipline. The 13-issue sprint is the right work: completing §5–§15 mechanics fills out the RPG engine and makes the game playable end-to-end. However, three structural concerns warrant advisory flags: (1) the `IsSuccess` invariant is still broken on main, (2) `GameSession` is becoming a god object with no extraction plan in the contracts, and (3) `ConversationRegistry` introduces host-level orchestration complexity that may be premature for prototype.

---

## Evaluation: Architecture vs. Maturity Level

### 1. Is it over-engineered for prototype?

**Mostly no.** The stateless evaluator pattern is lightweight and appropriate — each evaluator is a static class with pure functions. No dependency injection frameworks, no event buses, no plugin systems. The one area of concern is `ConversationRegistry` (#56), which introduces multi-session lifecycle management, cross-chat shadow bleed, and scheduled event processing. This is a production-grade orchestration layer sitting on top of a prototype game session. For a prototype, a simpler "list of sessions" with manual clock advancement would suffice. The cross-chat events (DateSecured, ThreeDeadToday, DoubleDateToday) are content-rich mechanics that assume the core session loop is rock-solid — which it won't be until after this sprint ships.

**Verdict: ConversationRegistry could be descoped to a simpler version** (register + fast-forward only, no cross-chat events) without losing the ability to validate the async-time design.

### 2. Does it create coupling that conflicts with the roadmap?

**Two coupling concerns:**

a. **GameSession as god object (#87):** The contracts add 3 new public async methods (ReadAsync, RecoverAsync, WaitAsync), integration hooks for 8 evaluators (shadow growth, shadow thresholds, combo, callback, XP, weakness windows, tells, horniness), plus GameSessionConfig injection. The architecture briefing acknowledges this but offers no extraction pattern in the contracts. At the next maturity level, decomposing GameSession into a phase-based pipeline will require touching every evaluator's integration point. The contracts should at minimum define the evaluator call order as a documented sequence (they do in `architecture.md` but not in the GameSession-touching contracts).

b. **SessionShadowTracker couples shadow reads to shadow writes:** Every evaluator that reads effective stats must go through `SessionShadowTracker.GetEffectiveStat()`, which combines base stats + session deltas. If a future sprint adds cross-session shadow persistence, the tracker's wrapping pattern works fine. But if shadow calculations become more complex (e.g., temporary buffs, shadow resistance), the tracker will need to evolve. **This is acceptable for prototype.**

### 3. Abstraction choices that will be painful to undo?

**One concern: `SessionCounters` is a kitchen-sink tracking bag.**

`SessionCounters` has 10+ mutable properties tracking disparate things (trap count, honesty success, opener text, SA usage, chaos ever picked, consecutive highest picks, read/recover fail counts). This is a "struct of bools and ints" pattern that works for prototype but makes it hard to:
- Know which evaluator owns which counter
- Add new counters without touching the class
- Test counter updates in isolation

At the next maturity level, each evaluator should own its own tracking state. The current design isn't blocking but should be flagged as tech debt.

### 4. Interface design: right user-facing boundaries?

**Yes, with one gap.** The contracts correctly separate:
- Stateless evaluators (static, no state) from GameSession (orchestrator, all state)
- IGameClock (interface) from GameClock (implementation) — testability is preserved
- Result types (ReadResult, RecoverResult, WaitResult) are purpose-built, not generic

**Gap:** The `ReadResult` exposes `Dictionary<StatType, int>? RevealedModifiers` on success. This leaks the internal stat representation to the host. At production maturity, this should be a purpose-built `OpponentProfile` or similar. Acceptable for prototype.

---

## Data Flow Traces

### Wave 0 → Feature Chain
- `SessionShadowTracker(StatBlock)` → stores base + delta → consumed by `ShadowGrowthEvaluator`, `ShadowThresholdEvaluator`, `ComboDetector` (indirectly via session), `GameSession.StartTurnAsync` (horniness calc)
- `IGameClock.Now` → `GetTimeOfDay()` → `GetHorninessModifier()` → added to shadow Horniness → horniness threshold check → option post-processing
- `RollEngine.ResolveFixedDC(SA, player, 12, ...)` → `RollResult` → Read/Recover success/failure → interest/shadow/XP effects
- Required fields at each hop: ✅ All traced fields exist in the contracts

### ⚠️ CRITICAL: `RollResult.IsSuccess` still uses `Total`, not `FinalTotal`
- **Current code (main, line 101):** `IsSuccess = IsNatTwenty || (!IsNatOne && Total >= dc);`
- **Contract (#139 Wave 0) says:** `IsSuccess = IsNatTwenty || (!IsNatOne && FinalTotal >= dc)`
- **Impact:** Tell bonus (+2), callback bonus (+1/+2/+3), and triple combo bonus all flow through `ExternalBonus` → `FinalTotal`. If `IsSuccess` doesn't check `FinalTotal`, these bonuses **cannot change a miss into a hit**. This means: a player gets a tell (+2) but it doesn't actually help them succeed.
- **Status:** Previously identified in vision concern #136, and specified as a Wave 0 fix in #139 contract. **Must be implemented in Wave 0 — this is the most important correctness fix in the sprint.**

### Horniness → Forced Rizz
- `SessionShadowTracker.GetEffectiveShadow(Horniness)` + `IGameClock.GetHorninessModifier()` → `horninessLevel` → threshold check (6/12/18) → option post-processing in `StartTurnAsync`
- ⚠️ At ≥18, contract says "replace all non-Rizz options" and "duplicate if needed." Duplicating a Rizz option with identical text gives the player 4 identical choices — this is poor UX even for prototype. The LLM should be asked for multiple distinct Rizz options. **Advisory only — LLM integration can compensate.**

### Shadow Growth → End of Game
- `ShadowGrowthEvaluator.EvaluateEndOfGame(outcome, counters)` → list of growth events → applied via `SessionShadowTracker.ApplyGrowth()`
- **Question:** Who calls `EvaluateEndOfGame`? The contract says `GameSession` but GameSession ends when interest hits 0 or 25. The end-of-game evaluation must happen AFTER the final turn's interest update but BEFORE the session is considered closed. The contract doesn't specify the exact call site within GameSession's end-game flow. **Advisory — implementer should clarify.**

---

## Unstated Requirements

1. **Players expect Read to be tactically useful.** The contract gives Read "reveal exact interest + opponent stat modifiers" on success. At prototype, this means the UI must display these numbers — but no UI contract exists for rendering ReadResult's revealed data. The host will need to handle this.

2. **Shadow growth must be visible to the player.** The contracts populate `TurnResult.ShadowGrowthEvents` and `ReadResult.ShadowGrowthEvents` as `IReadOnlyList<string>`. The host needs to display these. The format ("Dread +2: Interest hit 0") is human-readable, which is good.

3. **Combo names should be meaningful to the player.** `ComboDetector.PreviewCombos()` annotates options with combo names — the player sees "🔥 The Smooth Criminal" on an option. This is a core fun mechanic and the LLM should incorporate combo names into dialogue flavor. No contract specifies this LLM integration.

4. **Energy system needs a clear player-facing contract.** `IGameClock.RemainingEnergy` and `ConsumeEnergy()` exist but no contract specifies what costs energy, how the player sees their energy, or what happens when energy runs out. Is each turn 1 energy? Each conversation? This is undefined.

---

## Domain Invariants

1. **`RollResult.IsSuccess` must use `FinalTotal`** — external bonuses (tell, callback, triple) must be able to turn misses into hits. (Currently violated on main.)
2. **Shadow growth is post-roll only** — growth events from a roll MUST NOT affect that same roll's resolution. Contracts correctly specify this.
3. **Interest delta composition is additive**: SuccessScale/FailureScale + RiskTierBonus + momentum + combo = total delta. No multiplicative effects. Contracts maintain this.
4. **Turn sequencing: StartTurn → (Speak|Read|Recover|Wait) → StartTurn** — exactly one action per turn. Contracts correctly enforce this.
5. **SessionShadowTracker never mutates StatBlock** — all growth is stored as deltas. Contract is correct.
6. **XP events: Nat 20 replaces success XP, Nat 1 replaces failure XP** — not additive. Contract specifies this correctly.

---

## Gaps

### Missing (should be in this sprint)
- **Energy cost definition:** The IGameClock and ConversationRegistry contracts define energy infrastructure but never specify what costs energy. This should be defined even as a simple constant (1 energy per turn).

### Could be deferred
- **ConversationRegistry cross-chat events (#56 partial):** The `CrossChatEvent` enum and its effects (DateSecured buff, ThreeDeadToday penalty, DoubleDateToday) are complex content mechanics. For prototype, `Register + FastForward + ghost/fizzle` is sufficient. Cross-chat events could be a Sprint 6 addition.
- **Fixation ≥18 "must repeat last stat" (#45):** Throwing `InvalidOperationException` when the player picks a different stat than last turn is a harsh UX for prototype. Consider: suppress non-matching options from the list instead, similar to Denial ≥18.

### Assumptions to validate
- **Opener text tracking (#44):** `SessionCounters.FirstOpenerText` and `SecondOpenerText` for "same opener twice" detection — how is "opener text" defined? Is it the player's first message text? The chosen `DialogueOption.IntendedText`? The delivered (post-degradation) text? The contract doesn't specify.
- **"Highest-% option" (#44):** `ConsecutiveHighestPickCount` for Fixation growth — "highest-%" is undefined. Presumably the option with the highest stat modifier? The contract doesn't define the comparison metric.

---

## Recommendations

1. **Wave 0 `IsSuccess` fix is non-negotiable** — verify the implementer changes line 101 of `RollResult.cs` to use `FinalTotal`. This is the single most impactful correctness fix in the sprint. Already tracked in #136 and specified in #139 contract.

2. **Consider descoping ConversationRegistry cross-chat events** — keep Register, FastForward, ghost/fizzle. Defer CrossChatEvent propagation to next sprint. Reduces the most complex component's scope by ~40%.

3. **Define energy costs** — add a single sentence to the GameClock or GameSession contract: "Each turn costs 1 energy" (or whatever the rule is). Without this, the energy system is infrastructure with no consumer.

4. **Clarify "opener text" and "highest-%" in SessionCounters** — these are ambiguous enough that two implementers could interpret them differently. One sentence of clarification in the #44 contract prevents bugs.

5. **GameSession god object trajectory (#87) remains valid** — after this sprint, GameSession will have 4 public async methods + ~10 evaluator integrations. The next sprint should include a decomposition issue. Not blocking for prototype.

---

**VERDICT: ADVISORY**

The architecture is sound for prototype maturity. The stateless evaluator pattern is the right call, Wave 0 as a prerequisite is correctly identified, and the tiered implementation order respects dependency chains. Two concerns filed below as arch-concern issues. The `IsSuccess` invariant fix is the highest-priority correctness item — it's already in the Wave 0 contract but warrants explicit attention. ConversationRegistry's cross-chat events are the only scope item I'd recommend descoping.
