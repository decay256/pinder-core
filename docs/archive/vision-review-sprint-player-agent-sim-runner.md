# Vision Review — Sprint: Player Agent + Sim Runner Fixes

## Alignment: ✅ Strong

This sprint serves two complementary goals: (1) fix real bugs found during playtesting (file counter, null traps, missing character voice, wrong shadow growth trigger), and (2) build proper simulation infrastructure (player agents with scoring/LLM decision-making, shadow tracking, reasoning output). Both are high-leverage at prototype maturity — the bug fixes make the simulation produce correct game data, and the player agent infrastructure turns the sim from "pick highest modifier" into a meaningful rules-stress-test tool. This is exactly the right sequence: the engine rules are mostly complete (sprints 8-12), and now building the tooling to exercise those rules at scale is how you find the next round of bugs.

## Data Flow Traces

### Player Agent Decision Flow (#346 → #347 → #348)
- `GameSession.StartTurnAsync()` → `TurnStart { Options[], State }` → `IPlayerAgent.DecideAsync(turn, context)` → `PlayerDecision { OptionIndex, Reasoning, Scores[] }` → `session.ResolveTurnAsync(index)` → `TurnResult`
- Required fields for scoring: StatBlock (player + opponent), DC per option, momentum streak, active traps, shadow values, tell/callback/combo flags on each option
- ⚠️ **Gap**: `PlayerAgentContext` duplicates data already in `TurnStart.State` (GameStateSnapshot) and `TurnStart.Options`. Context should be built FROM TurnStart, not maintained as a parallel model that can drift.
- ⚠️ **Architecture**: #346 places `IPlayerAgent` in `Pinder.Core/Interfaces/` — this is simulation infrastructure, not game engine. Filed as #355.

### Shadow Tracking in Session Runner (#350)
- Session runner creates `SessionShadowTracker(startingShadows)` → passes via `GameSessionConfig.PlayerShadows` → `GameSession` uses for shadow growth checks → `TurnResult.ShadowGrowthEvents` returned → session runner displays in output
- Required fields: character's starting shadow values (from stat block), shadow growth events from TurnResult
- ✅ Data flow is clean — `StatBlock` already carries shadow values via constructor, `SessionShadowTracker` wraps them.

### Fixation Shadow Growth Fix (#349)
- `ResolveTurnAsync(optionIndex)` → current code: `_highestPctOptionPicked.Add(optionIndex == 0)` → WRONG: assumes index 0 = highest %
- Fix: compute `successProbability = (21 - need) / 20` for each option → compare chosen vs max → `_highestPctOptionPicked.Add(isHighest)`
- Required fields: chosen option's stat, opponent StatBlock (for DC), all options' stats
- ✅ All data is available in `ResolveTurnAsync` — `_currentOptions` has all options, `_opponent.Stats` has the defense values.

### Interest Change Beat Voice (#352)
- `GameSession` detects interest threshold crossing → constructs `InterestChangeContext(name, before, after, state)` → `ILlmAdapter.GetInterestChangeBeatAsync(ctx)` → LLM generates beat text → **but no character prompt is passed** → LLM generates generic text
- Required fields: `OpponentPrompt` (the assembled system prompt for the opponent character)
- ⚠️ **Missing**: `InterestChangeContext` lacks `OpponentPrompt`. Must add as optional param for backward compat. Filed as #357.

### Trap Loading (#353)
- Session runner creates `JsonTrapRepository(trapsPath)` → **BUG**: constructor expects JSON string, not file path → `FormatException`
- Fix: `File.ReadAllText(path)` → `new JsonTrapRepository(jsonContent)`
- ⚠️ **Spec error**: Issue #353 shows wrong constructor usage. Filed as #356.

### File Counter (#354)
- `WritePlaytestLog()` → `Directory.GetFiles(dir, "session-???.md")` → glob doesn't match `session-005-sable-vs-brick.md` → `Substring(8, 3)` fails on hyphenated names → counter resets to 1
- Fix: split on `-`, parse `parts[1]` as the number
- ✅ Pure file I/O fix, no data flow issues.

## Unstated Requirements

- **ScoringPlayerAgent must account for external bonuses in probability calculation**: The scoring formula in #347 computes `need = DC - (statMod + momentum + callback + tell)` but these hidden bonuses are ON the `DialogueOption` as boolean flags, not numeric values. The agent needs to know tell = +2, callback = +1/+2/+3 etc. These magic numbers should come from the engine, not be hardcoded in the agent.
- **LlmPlayerAgent fallback must be seamless**: If API fails mid-session (not just first call), the fallback to ScoringPlayerAgent should produce a consistent `PlayerDecision` format so the session runner output doesn't break.
- **Session runner output should distinguish agent type per session**: The playtest file header should note which agent was used (Scoring vs LLM) so readers know how to interpret the reasoning quality.

## Domain Invariants

- **Simulation agents must not mutate game state** — they receive `TurnStart` (read-only) and return a decision index. All state mutation flows through `GameSession`.
- **Scoring agent determinism** — identical inputs must always produce identical outputs. No randomness in scoring.
- **Shadow growth correctness** — Fixation triggers on 3 consecutive highest-probability picks, not 3 consecutive index-0 picks.
- **Trap activation correctness** — TropeTrap (miss 6-9) must fire with real trap definitions, not null stubs.
- **Backward compatibility** — all Pinder.Core DTO changes use optional params with defaults. Existing 1718+ tests pass unchanged.

## Gaps

### Missing: Nothing critical beyond filed concerns
The sprint scope is well-targeted. Bug fixes address real issues found in playtesting, and the player agent work is a natural next step.

### Unnecessary: Nothing
All 9 issues serve the simulation infrastructure or fix real bugs. No gold-plating.

### Assumptions to validate:
- **#346 interface location**: Issue says `Pinder.Core/Interfaces/` — concern filed (#355) recommending session-runner instead.
- **#347 bonus values hardcoded**: The scoring formula references specific bonus values (+2 for tell, +1/+2/+3 for callback) that are hardcoded in GameSession. If these values change in the engine, the scoring agent will diverge silently.
- **#348 API key sharing**: LlmPlayerAgent uses `ANTHROPIC_API_KEY` — same key as the game's LLM adapter. In a sim run, this means two concurrent Claude callers (game dialogue + player reasoning). Rate limits could be hit.
- **#353 traps.json path**: The issue hardcodes `/root/.openclaw/agents-extra/pinder/data/traps/traps.json`. This path is environment-specific. The session runner should accept it as a command-line arg or env var, with fallback.

## Requirements Compliance Check

No `REQUIREMENTS.md` file exists. Checking against architecture doc constraints:
- ✅ netstandard2.0 + C# 8.0 for Pinder.Core changes (#349, #352)
- ✅ Zero NuGet dependencies for Pinder.Core (no new deps added)
- ⚠️ #346 adds simulation types to Pinder.Core — concern filed (#355)
- ✅ Session runner targets net8.0 — can use any .NET APIs
- ✅ All DTO changes use optional params with backward-compatible defaults

## Vision Concerns Filed

| # | Concern | Severity |
|---|---------|----------|
| #355 | IPlayerAgent should not live in Pinder.Core — simulation infrastructure pollutes game engine | Medium |
| #356 | JsonTrapRepository takes JSON string, not file path — issue #353 spec has wrong usage | Low (spec clarification) |
| #357 | InterestChangeContext needs backward-compatible optional param for OpponentPrompt | Low (pattern reminder) |

## Wave Plan

Based on dependency analysis:

**Wave 1:** #354, #353, #349, #352, #350
- #354: File counter fix (pure session-runner, no deps)
- #353: NullTrapRegistry → JsonTrapRepository (session-runner, no deps)
- #349: Fixation shadow growth fix (Pinder.Core/GameSession, no deps)
- #352: InterestChangeBeat character voice (Pinder.Core + LlmAdapters, no deps)
- #350: Shadow tracking in session runner (session-runner, no code deps — uses existing GameSessionConfig)

**Wave 2:** #346
- #346: IPlayerAgent interface + types (foundation for agents — no deps on Wave 1, but placed here to allow concern #355 to be addressed first)

**Wave 3:** #347
- #347: ScoringPlayerAgent (depends on #346)

**Wave 4:** #348, #351
- #348: LlmPlayerAgent (depends on #346, #347)
- #351: Show pick reasoning in output (depends on #347, #348)

## Verdict: ADVISORY

Three concerns filed. None are blocking — the sprint can proceed. #355 (IPlayerAgent location) is the most architecturally significant but is a prototype-maturity judgment call, not a correctness issue. #356 and #357 are spec clarifications that a competent implementer will figure out, but filing them reduces wasted cycles.
