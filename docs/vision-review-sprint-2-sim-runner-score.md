# Vision Review — Sprint 2 (Sim Runner + Scorer Improvements)

## Alignment: ✅ Strong

This sprint is well-sequenced and high-leverage. The RPG engine is rules-complete (sprints 8–12), the LLM adapter works (sprints 9–10), and sprint 2 (previous) built the player agent infrastructure. This follow-up sprint focuses on two things: (1) making the session runner production-ready by removing hardcoded characters and fixing the file counter, and (2) making the scoring agent smarter by accounting for shadow growth risk. Both increase the simulation's value as a balance-testing tool, which is exactly what prototype maturity needs — exercising the game rules at scale to find mechanical bugs before building the Unity host.

## Insufficient Requirements

None. All 5 issues have substantive bodies (>50 chars) with clear acceptance criteria. Appropriate for prototype maturity.

## Data Flow Traces

### Character Loading from CLI (#414)
- User runs `--player gerald --opponent velvet` → `CharacterLoader` parses `design/examples/{name}-prompt.md` → extracts Level, effective stats (6 values), shadow starting values, system prompt → constructs `StatBlock` + `CharacterProfile` → passed to `GameSession`
- Required fields: Level, all 6 stat modifiers, shadow values (5), system prompt text, display name
- ✅ Path is clear. Prompt files exist at `/root/.openclaw/agents-extra/pinder/design/examples/`. The parser extracts from known file format.
- ⚠️ Note: Path is currently hardcoded to `/root/.openclaw/agents-extra/pinder/design/examples`. The `--player` shorthand must resolve this correctly relative to the build output.

### CharacterAssembler Integration (#415)
- User runs `--player-def player-gerald.json` → load JSON definition → `JsonItemRepository(File.ReadAllText("data/items/starter-items.json"))` → `JsonAnatomyRepository(File.ReadAllText("data/anatomy/anatomy-parameters.json"))` → `CharacterAssembler.Assemble(itemIds, anatomySelections, baseStats, shadows)` → `FragmentCollection` → ???  → `GameSession`
- Required fields: item IDs, anatomy selections, build points (6 stats), shadow starting values, display name, level, bio, gender identity
- ⚠️ **Gap filed as #419**: `CharacterAssembler.Assemble()` returns `FragmentCollection`, NOT `CharacterProfile`. The spec shows `var profile = assembler.Assemble(definition)` which won't compile. Implementer must bridge via `PromptBuilder.BuildSystemPrompt()` → `CharacterProfile` constructor.
- ⚠️ **Data dependency**: `data/items/starter-items.json` and `data/anatomy/anatomy-parameters.json` don't exist in this repo. They exist in the external `pinder` repo. #415 must either copy them in or reference the external path. The issue doesn't specify which approach.

### Shadow Growth Risk Scoring (#416)
- `ScoringPlayerAgent.DecideAsync(turn, context)` → for each option, compute `shadowPenalty` from stat repetition history + Denial risk + Fixation threshold effects → adjust EV score → pick highest
- Required fields: last 2 stats used, current shadow levels (all 5), whether Honesty is in current options
- ⚠️ `PlayerAgentContext` currently lacks `lastStatsUsed` history. #416 AC correctly requires adding this. The session runner must track stat picks across turns and pass them in the context.

### Max Turns + Projected Outcome (#417)
- Session loop runs up to `maxTurns` → on cutoff, read final `InterestMeter` value + momentum streak → compute projected outcome text → append to session markdown
- Required fields: current interest, momentum streak, success rate (derived from turn history)
- ✅ All data available from `GameStateSnapshot` at session end.

### File Counter Fix (#418)
- `SessionFileCounter.GetNextSessionNumber(dir)` → `Directory.GetFiles(dir, "session-*.md")` → split filename on `-` → parse `parts[1]` as int → return max + 1
- ✅ The code in `SessionFileCounter.cs` already looks correct (uses `session-*.md` glob and `Split('-')[1]` parsing). The issue suggests the bug is in path resolution — the playtest directory may not resolve correctly from the build output directory. Pure debugging task.

## Unstated Requirements

- **Character definition files need validation against the real assembler pipeline**: If #415 creates `data/characters/gerald.json` with item IDs, those IDs must match entries in `starter-items.json`. No spec validates this cross-reference.
- **The `--player gerald` shorthand in #414 becomes partially obsolete when #415 ships**: #414 parses pre-assembled prompt files, #415 uses the real assembler pipeline. The sprint should clarify which path is canonical after both ship — otherwise two loading mechanisms coexist and drift.
- **Shadow growth penalty constants in #416 (0.5, 0.3, 0.1) are tuning values**: These should be easily adjustable. The scorer's shadow awareness is only useful if the penalties meaningfully change its decisions vs. the current pure-EV scorer.

## Domain Invariants

- `ScoringPlayerAgent` must remain deterministic: identical inputs → identical outputs
- `CharacterAssembler` output + `PromptBuilder` must produce the same `CharacterProfile.AssembledSystemPrompt` as the hand-written prompt files in `design/examples/` — otherwise sim results diverge between #414 and #415 loading paths
- File counter must be monotonically increasing: N sequential runs → N correctly-numbered files
- Shadow growth penalty must not make the scorer strictly worse — the penalty tuning should improve session outcomes (more DateSecured, fewer runaway shadow accumulations)

## Gaps

### Missing
- **Item/anatomy data files**: `data/items/starter-items.json` and `data/anatomy/anatomy-parameters.json` are not in this repo. #415 cannot ship without them. The implementer needs to either copy from the external pinder repo or add a path resolution mechanism.
- **#414 → #415 migration path**: After #415 ships, does `--player gerald` use the assembler pipeline or the prompt file parser? Both #414 and #415 define `--player` behavior differently. #415 says "Keep `--player gerald` as shorthand that loads from a definition file in `data/characters/gerald.json`" — this should supersede #414's prompt-file parsing, but the transition isn't explicit.

### Unnecessary
- Nothing — all 5 issues are well-scoped for the sprint goal.

### Assumptions to validate
- The 5 starter characters' pre-assembled prompts (`design/examples/`) produce mechanically identical results to running them through `CharacterAssembler` + `PromptBuilder`. If they don't, sim comparisons across loading methods are invalid.

## Requirements Compliance Check

No `REQUIREMENTS.md` exists in the repo. No formal FR/NFR/DC entries to check against. At prototype maturity this is acceptable — the game rules in `design/systems/rules-v3.md` (external) serve as the de facto requirements.

## Role Assignment Review

All 5 issues are assigned `backend-engineer`. This is correct — all work is C# session-runner code, scoring logic, and data loading. No UI, no CI/CD, no documentation-only work.

## Recommendations

1. **Address #419 before #415 implementation**: The spec's example code won't compile. Add a note to #415 clarifying the `FragmentCollection` → `PromptBuilder` → `CharacterProfile` pipeline.
2. **Ensure item/anatomy JSON data files are available**: Either copy `starter-items.json` and `anatomy-parameters.json` into `data/` in this repo, or document the external path resolution. Without these, #415 is blocked.
3. **Clarify #414 vs #415 `--player` behavior**: Since #415 depends on #414, the final state should be that `--player gerald` uses the assembler pipeline (not prompt file parsing). Document this in #415 so the implementer builds #414's prompt-file loader as a temporary bridge, not the permanent solution.

## Filed Concerns

| # | Concern | Severity |
|---|---------|----------|
| #419 | CharacterAssembler returns FragmentCollection, not CharacterProfile — #415 spec has wrong usage | Advisory |

## Verdict: **ADVISORY**

One concern filed (#419). The sprint is well-aligned and appropriately scoped. The CharacterAssembler data flow gap in #415 is recoverable at prototype maturity but should be clarified before implementation to avoid wasted cycles.
