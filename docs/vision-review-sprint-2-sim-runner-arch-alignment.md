# Architecture Strategic Alignment Review — Sprint 2 (Sim Runner + Scorer Improvements)

## Alignment: ✅ Strong

The architect's output is well-structured, appropriately scoped for prototype maturity, and correctly addresses all three vision concerns (#419, #421, #422) filed in the first pass. The ADRs are clear and the implementation order (waves 0-3) correctly sequences dependencies. No over-engineering detected — the architecture adds only what's needed for the sprint goals.

## Maturity Fit Assessment

### Appropriate for prototype:
- **Static utility classes** (`CharacterLoader`, `DataFileLocator`, `OutcomeProjector`) — correct. No premature DI framework, no abstract factories. Simple static methods that can be refactored at MVP if needed.
- **Data file duplication** — copying JSON into the repo is the right call at prototype. Submodules or shared packages are MVP concerns.
- **Heuristic projection** — simple interest thresholds for `OutcomeProjector`. Monte Carlo would be gold-plating at this stage.
- **`PlayerAgentContext` extension via optional constructor params** — backward-compatible without introducing a builder pattern. Right for prototype.

### Risks at next maturity level:
- **Two character loading paths** (`CharacterLoader` from prompt files + `CharacterDefinitionLoader` from JSON definitions) will coexist. The contract clarifies `--player gerald` maps to `data/characters/gerald.json` via `DataFileLocator`, but the prompt-file loader from #414 isn't explicitly deprecated. This is fine for prototype — at MVP, the assembler pipeline should be canonical and the prompt-file loader removed.

## Coupling Analysis

No concerning coupling introduced:
- All new components live in `session-runner/` — zero changes to `Pinder.Core` public API
- `CharacterDefinitionLoader` depends on `CharacterAssembler` + `PromptBuilder` (stable, well-tested Core APIs)
- `ScoringPlayerAgent` extensions are additive — new penalty terms don't change existing scoring
- `DataFileLocator` follows the same pattern as existing `TrapRegistryLoader`

The one-way dependency chain remains clean: `session-runner → Pinder.LlmAdapters → Pinder.Core`.

## ADR Evaluation

### ADR: Copy data files — ✅ Correct
Matches existing `data/traps/traps.json` pattern. Small files, infrequent changes. No structural risk.

### ADR: --max-turns owned by #414, default 20 — ✅ Correct
Clean resolution of #422. Eliminates merge conflict. Default 20 is better than 15 (evidence from session-008 hitting the cap at Interest 15 with Momentum 4).

### ADR: CharacterAssembler → CharacterProfile bridging — ✅ Correct
The `FragmentCollection → PromptBuilder.BuildSystemPrompt() → CharacterProfile` pipeline is explicitly documented with constructor params verified against source code. Using `new TrapState()` for initial prompt generation is the right call — no active traps at character creation time.

## Interface Design Review

The contracts are well-specified with:
- Clear pre/post conditions
- Specific exception types and when they're thrown
- Behavioral invariants (determinism for scorer, monotonicity for counter)
- Backward-compatible extension points

One observation: `CharacterDefinitionLoader.Load()` takes `IItemRepository` and `IAnatomyRepository` as params rather than loading them internally. This is good — it allows sharing repositories across multiple character loads and follows the existing DI pattern in `CharacterAssembler`.

## Separation of Concerns

The separation map is clean and each component has a clear "must NOT know" boundary. No component crosses its concern boundary. The `Program.cs` acts as the composition root (wiring everything together), which is the correct pattern for a console app.

## Gap Check

All three first-pass concerns are addressed:
- **#419** (FragmentCollection gap): Explicit bridging pipeline in ADR + contract
- **#421** (missing data files): Copy into `data/` + `DataFileLocator` resolution
- **#422** (--max-turns conflict): #414 owns arg, #417 owns projection

No new gaps identified in the architectural layer.

## Requirements Compliance

No `REQUIREMENTS.md` exists. The architecture doesn't violate any stated game rules — all changes are session-runner infrastructure, not game mechanics. The `ScoringPlayerAgent` shadow penalties (#416) are consistent with §7 shadow growth rules.

## Verdict

**VERDICT: CLEAN** — architecture aligns with product vision, proceed.

The architect correctly resolved all three vision concerns, made appropriate ADR decisions for prototype maturity, and produced clean interface contracts with proper separation of concerns. No coupling risks, no over-engineering, no abstraction debt that would be painful at MVP. The implementation order is correct and parallelizable.
