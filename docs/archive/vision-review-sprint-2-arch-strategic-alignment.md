# Vision Review — Sprint 2 Architecture Strategic Alignment

**Sprint:** Player Agent + Sim Runner Fixes
**Reviewer:** Product Visionary (second pass)
**Date:** 2026-04-03
**Refs:** #354, PR #387 (architect output, merged)

## Alignment: ✅

This sprint is strategically sound. Automated playtesting infrastructure (player agents) is the highest-leverage work available right now — it directly enables the game balance iteration loop that unlocks all downstream value (fun gameplay, tunable difficulty, character distinctiveness). The bug fixes (#349, #352, #353, #354) are all correctness issues discovered during real playtesting, proving the feedback loop is already working. The architecture correctly confines all new abstractions to `session-runner/` (per #355), preserving Pinder.Core's zero-dependency contract.

## Maturity Fit Assessment

**Appropriate for prototype.** The architect's choices are correctly calibrated:

| Decision | Assessment |
|----------|-----------|
| IPlayerAgent in session-runner, not Core | ✅ Right boundary — this is tooling, not game engine |
| ScoringPlayerAgent uses EV approximation | ✅ Good enough for prototype — Monte Carlo would be over-engineering |
| LlmPlayerAgent falls back to ScoringPlayerAgent | ✅ Correct resilience for non-user-facing tooling |
| No plugin system, no configuration DSL | ✅ Plain classes — easy to delete or refactor |
| SYNC comments for duplicated constants | ✅ Acceptable at prototype — extracting shared constants is MVP work |

**No over-engineering detected.** All new types are concrete classes with no framework abstractions.

## Coupling Analysis

### ✅ No problematic coupling

1. **session-runner → Pinder.Core**: One-way dependency on stable public types (`TurnStart`, `GameStateSnapshot`, `StatBlock`, `InterestState`). These are unlikely to change shape.
2. **session-runner → Pinder.LlmAdapters**: Only `LlmPlayerAgent` touches `AnthropicClient` + `AnthropicOptions`. Acceptable — both are in the adapter layer.
3. **Pinder.Core changes are minimal**: `InterestChangeContext` gains optional param, `GameSession` gains private helper. No new public API surface.

### No roadmap conflicts

The architecture doesn't create coupling that would impede the next maturity level:
- Player agent types in session-runner can be extracted to a shared library if needed (e.g., for a Unity test harness) — but that's a future concern, not now.
- The `IPlayerAgent` interface takes engine output types, not session internals — this boundary is correct and won't need to change.

## Abstraction Reversibility

All new abstractions live in `session-runner/`, a standalone .NET 8 console app. Cost to revert:
- Delete 3-4 files, inline logic back into Program.cs: ~30 minutes
- No Core types affected
- No other consumers exist

**Risk: LOW.** This is the correct place to experiment at prototype maturity.

## Interface Design Evaluation

### ✅ IPlayerAgent boundary is correct
- Input: `TurnStart` (engine output) + `PlayerAgentContext` (supplementary state)
- Output: `PlayerDecision` (index + reasoning + scores)
- Does not expose GameSession internals or require knowledge of roll math

### ✅ ADR for #386 is sound
The architect's ADR correctly resolves the vision concern: `CallbackBonus.Compute()` is public static and must be called directly; `GetMomentumBonus()` is private static and must be duplicated with SYNC comments. This is the pragmatic prototype choice — extracting a shared bonus calculator would be premature abstraction.

## Data Flow Trace Verification

### Player Agent Decision → Turn Resolution
```
GameSession.StartTurnAsync() → TurnStart
  → Program builds PlayerAgentContext
  → IPlayerAgent.DecideAsync(turnStart, context) → PlayerDecision
  → GameSession.ResolveTurnAsync(decision.OptionIndex) → TurnResult
```
**All fields flow correctly.** `TurnStart.State` has Interest, InterestState, MomentumStreak, ActiveTrapNames, TurnNumber.

### Shadow Tracking
```
Program creates SessionShadowTracker(playerStatBlock)
  → GameSessionConfig(playerShadows: tracker)
  → GameSession uses tracker for shadow growth
  → TurnResult.ShadowGrowthEvents populated
  → Program displays per-turn + summary
```
**Fields flow correctly.** #360 correctly identified the constructor signature issue (takes `StatBlock`, not `Dictionary`).

### Interest Beat Voice
```
GameSession detects threshold crossing
  → InterestChangeContext(opponentPrompt: ...)
  → AnthropicLlmAdapter builds system blocks with character voice
```
**Minor implementation detail**: The source of `opponentPrompt` (likely `PromptBuilder.BuildSystemPrompt()`) is left to the implementer. Not blocking — this is a wiring question, not an architectural gap.

## Gaps

- **None blocking.**
- **Minor**: `PlayerAgentContext` overlaps `GameStateSnapshot` — the architect acknowledged this and deferred cleanup. Acceptable at prototype.
- **Minor**: 30 open vision-concern issues (many from sprints 5-12) create noise. A bulk triage/close pass would help — many are resolved by merged work. Not sprint-blocking.

## Requirements Compliance

No `REQUIREMENTS.md` exists in the repo. All Pinder.Core changes use backward-compatible optional parameters. Zero-dependency constraint maintained. All 1977 existing tests must continue to pass (implicit requirement from architecture doc).

## Insufficient Requirements Check

All sprint issues checked for body completeness (prototype threshold: ≥50 chars meaningful content):

| Issue | Status |
|-------|--------|
| #354 | ✅ Sufficient — clear root cause, fix, AC |
| #353 | ✅ Sufficient (closed/merged) |
| #352 | ✅ Sufficient (closed/merged) |
| #349 | ✅ Sufficient (closed/merged) |
| #346 | ✅ Sufficient — interface spec with context |
| #347 | ✅ Sufficient — scoring formula detailed |
| #348 | ✅ Sufficient — LLM agent spec with fallback |
| #350 | ✅ Sufficient — shadow tracking wiring |
| #351 | ✅ Sufficient — output format spec |
| #386 | ✅ Sufficient — concern with resolution |

No issues flagged as `INSUFFICIENT_CONTEXT`.

## Recommendations

1. **Proceed with implementation.** The architecture is well-scoped, correctly bounded, and appropriately calibrated for prototype maturity.
2. **Implementer of #352**: Verify opponent prompt source — likely `PromptBuilder.BuildSystemPrompt(opponent.Fragments, ...)` or similar. Check how session runner currently constructs the opponent profile.
3. **Future sprint**: Consider a bulk triage of the 30 open vision-concern issues. Many reference work that has since merged.

---

**VERDICT: CLEAN** — Architecture aligns with product vision. Proceed with implementation.
