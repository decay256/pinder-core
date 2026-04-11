# Vision Review — Sprint 2 (Sim Runner + Scorer Improvements) — Attempt 2

## Alignment: ✅ Strong

This sprint remains well-aligned with the product vision. The session runner is the primary tool for exercising the RPG engine at prototype maturity — every improvement here directly increases the value of automated playtesting. Character loading from CLI (#414) and CharacterAssembler integration (#415) eliminate hardcoded test data, making it possible to test any character combination. Shadow-aware scoring (#416) makes the automated player smarter, producing more realistic playtests. The max-turns fix (#417) and file counter fix (#418) are quality-of-life improvements that prevent data loss and misleading results.

## Insufficient Requirements

None. All 5 issues have substantive bodies with clear acceptance criteria. Appropriate for prototype maturity.

## Data Flow Traces

### Character Loading from CLI (#414)
- User runs `--player gerald --opponent velvet` → `CharacterLoader` parses `design/examples/{name}-prompt.md` → extracts Level, 6 stat modifiers, shadow values, system prompt → constructs `StatBlock` + `CharacterProfile` → passed to `GameSession`
- Required fields: Level, 6 stat modifiers, 5+ shadow values, system prompt text, display name
- ✅ Path is clear. Prompt files exist at external path.

### CharacterAssembler Integration (#415)
- User runs `--player-def player-gerald.json` → load JSON definition → `JsonItemRepository` + `JsonAnatomyRepository` → `CharacterAssembler.Assemble(itemIds, anatomySelections, baseStats, shadows)` → `FragmentCollection` → `PromptBuilder.BuildSystemPrompt()` → `CharacterProfile` constructor → `GameSession`
- Required fields: item IDs, anatomy selections, build points, shadow values, display name, level, bio, gender identity
- ⚠️ **#419 (existing)**: `Assemble()` returns `FragmentCollection`, not `CharacterProfile` — spec code won't compile
- ⚠️ **#421 (new)**: `data/items/starter-items.json` and `data/anatomy/anatomy-parameters.json` don't exist in pinder-core

### Shadow Growth Risk Scoring (#416)
- `ScoringPlayerAgent.DecideAsync(turn, context)` → for each option, compute `shadowPenalty` from stat history + Denial risk → adjust EV → pick highest
- Required fields: last 2 stats used, current shadow levels, whether Honesty is in options
- ⚠️ `PlayerAgentContext` currently lacks stat history. #416 AC correctly requires adding this.

### Max Turns + Projected Outcome (#417)
- Session loop runs up to `maxTurns` → on cutoff, compute projected outcome → append to markdown
- ✅ All data available from `GameStateSnapshot`.
- ⚠️ **#422 (new)**: `--max-turns` arg defined in both #414 (default 15) and #417 (default 20) — conflict

### File Counter Fix (#418)
- `SessionFileCounter.GetNextSessionNumber(dir)` → glob `session-*.md` → parse number → return max+1
- ✅ `SessionFileCounter.cs` already uses correct glob and parsing. Issue suggests a path resolution bug.

## Unstated Requirements

- **Character definition validation**: If #415 creates definition files with item IDs, those IDs must exist in `starter-items.json`. No cross-reference validation is specified.
- **Loading path unification**: After #415 ships, `--player gerald` should use the assembler pipeline, not the prompt file parser from #414. The migration path isn't explicit.
- **Shadow penalty tuning**: The constants in #416 (0.5, 0.3, 0.1) need to be validated against actual playtest outcomes. Should be easy to adjust.

## Domain Invariants

- `ScoringPlayerAgent` must remain deterministic: identical inputs → identical outputs
- File counter must be monotonically increasing: N sequential runs → N correctly-numbered files
- `CharacterAssembler` pipeline must produce mechanically equivalent characters to hand-written prompt files — otherwise sim comparisons are invalid across loading paths
- Shadow growth penalty must not make the scorer strictly worse vs. pure-EV baseline

## Gaps

### Missing
- **Item/anatomy data files** (#421): `data/items/starter-items.json` and `data/anatomy/anatomy-parameters.json` must be available for #415
- **--max-turns ownership** (#422): Both #414 and #417 define this arg with different defaults

### Unnecessary
- Nothing — all 5 issues are well-scoped.

### Assumptions to validate
- Starter character prompt files produce mechanically identical results to running through `CharacterAssembler` + `PromptBuilder`

## Requirements Compliance Check

No `REQUIREMENTS.md` exists in the repo. No formal FR/NFR/DC entries to check against. Acceptable at prototype maturity.

## Filed Concerns

| # | Concern | Status |
|---|---------|--------|
| #419 | CharacterAssembler returns FragmentCollection, not CharacterProfile | Existing — well-specified ✅ |
| #421 | #415 requires item/anatomy data files not in pinder-core | **New** — filed this pass |
| #422 | #414 and #417 both add --max-turns with conflicting defaults | **New** — filed this pass |

## Existing Concerns (resolved in code, still open on GitHub)

These were filed in prior sprints and have been implemented. They remain open for PO closure:
- #355 (IPlayerAgent in session-runner) — ✅ implemented correctly
- #356 (JsonTrapRepository takes string) — ✅ implemented via TrapRegistryLoader
- #359 (file counter glob) — ✅ implemented in SessionFileCounter
- #360 (SessionShadowTracker takes StatBlock) — ✅ implemented correctly

## Recommendations

1. **Address #421 before #415**: Either copy data files into pinder-core `data/` or add path resolution (like TrapRegistryLoader pattern).
2. **Remove `--max-turns` from #414 scope** per #422: Let #417 own it with default 20.
3. **Sequence #414 before #415**: #415 depends on #414 (stated). Ensure they're in separate waves.
4. **#419 remains valid**: Implementer of #415 must bridge `FragmentCollection` → `PromptBuilder` → `CharacterProfile`.

## Verdict: **ADVISORY**

Two new concerns filed (#421, #422). Both are recoverable at prototype maturity but should be addressed before implementation to avoid wasted cycles. The sprint is well-aligned and appropriately scoped.
