# CPO Strategic Review — Sprint 15

## Alignment: ⚠️
This sprint performs critical cleanup (test suite deduplication, rules DSL tool finalization) and introduces missing simulation and scoring agents. While the test cleanup and tooling are strategically necessary to reduce tech debt and ensure maintainability, the feature additions contain ambiguous or flawed logic (overlapping level tiers for archetypes, broken variance math in the scoring agent) that threaten the integrity of the data and simulation if implemented as specified.

## Data Flow Traces

### Active Archetype Injection (#649)
- User action: Session start (CharacterAssembler initializes characters)
- Data flow: `Level` & `EquippedArchetypes` -> `Tier Filter` -> `ActiveArchetype` -> `PromptBuilder` -> LLM System Blocks
- Required fields: `CharacterProfile.Level`, `Archetype.Tier` (missing from spec), `FragmentCollection.EquippedArchetypes`
- ⚠️ **Missing**: The `Archetype` data model does not currently define an `ArchetypeTier`, and `CharacterAssembler` does not group by tier. Level tiers are defined with overlapping ranges (`Tier 2: 2-6`, `Tier 3: 3-9`), causing data ambiguity.

### LlmPlayerAgent (#492)
- User action: Session playtest execution (`--agent llm`)
- Data flow: `TurnStart` options + `GameState` + `CharacterProfile.DominantArchetype` + `CharacterProfile.TextingStyleFragment` -> `AnthropicLlmAdapter` -> `PICK: [A/B/C/D]`
- Required fields: `DialogueOptions`, `Interest`, `Momentum`, `Shadows`, `ActiveTraps`, `TurnNumber`, `History`, `DominantArchetype`, `TextingStyleFragment`
- ✅ Data flow is complete, but relies on #649 for archetype injection.

### Shadow Growth Risk Display (#644)
- User action: Turn generation via `ScoringPlayerAgent`
- Data flow: `ScoringPlayerAgent.ScoreOptions()` calculates EV and shadow penalty -> returned in `OptionScore` -> `Program.cs` console output appends warning string
- Required fields: `DialogueOption.Stat`, `SessionShadowTracker` state -> `OptionScore.ShadowGrowthWarning`
- ✅ Data flow is well understood.

## Unstated Requirements
- **LLM Agent Fallback Depth**: If `LlmPlayerAgent` fails to parse `PICK: [A/B/C/D]`, it must fall back to `ScoringPlayerAgent`. The user will expect the failure reason to be logged in the playtest output for debugging prompt compliance.
- **Test Suite Execution Time**: Consolidating and deleting 30+ duplicate tests (#375-#385) must noticeably improve test suite execution time and maintain CI reliability.

## Domain Invariants
- **Immutable Rule DSL**: The generated YAML DSL (#442) must be a 100% mathematically lossless representation of the authoritative Markdown rules. Any information loss invalidates the engine's grounding.
- **Risk-Reward Axiom**: The scoring evaluation (#481) must mathematically distinguish between variance and raw expected value.

## Gaps
- **Missing**: Mutually exclusive level tiers and `ArchetypeTier` definitions for archetype injection (#649).
- **Unnecessary**: Scaling EV to reward variance (#481) is mathematically flawed. To reward variance, failure weight must be reduced independently from success weight.
- **Assumption**: The CI workflow (#447) assumes `ANTHROPIC_API_KEY` is safely passed to the checker, which could expose the secret if not scoped to specific PR conditions.

## Insufficient Requirements
None flagged. All issues meet context thresholds and define clear problem statements and acceptance criteria.

## Wave Plan
- **Wave 1**: #442, #470, #385, #384, #383, #382, #381, #380, #379, #378, #377, #376, #644, #484
- **Wave 2**: #447 (depends on #442), #375 (depends on #376-#385), #481 (depends on #347), #649 (depends on #648)
- **Wave 3**: #492 (depends on #346, #489, #649)

## Recommendations
1. Halt #649 until level tiers are redefined cleanly (e.g., 1-3, 4-6, 7-9, 10+) and `ArchetypeTier` is added to the data model. (Concern filed: #668)
2. Halt #481 until the variance math is corrected. EV scaling does not reward variance; penalty weighting must be adjusted independently. (Concern filed: #667)

## Verdict: ADVISORY
Concerns have been raised regarding archetype level tiers and scoring agent mathematics. The sprint should incorporate these corrections and proceed.
