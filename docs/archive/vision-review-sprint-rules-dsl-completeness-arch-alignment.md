# Architecture Strategic Alignment Review — Sprint: Rules DSL Completeness

## Alignment: ✅ Strong

This sprint is pure infrastructure investment that strengthens the data-driven rules pipeline — the foundation for the product's core value proposition of balance tuning without recompilation. All three issues (#443, #444, #445) are correctly scoped to the Python tooling layer and generated C# test stubs, with zero changes to Pinder.Core game logic. The architect's PROCEED verdict is well-justified: clear separation of concerns, correct dependency ordering (#443 → #444 → #445), and no structural changes needed. At prototype maturity, this is exactly the right work — building reliable pipeline tooling before depending on its output for runtime rule resolution.

## Maturity Fit Assessment

### Appropriate for prototype:
- **Round-trip fidelity tolerance** (<50 lines per doc, whitespace-only) — correct. Investing in pixel-perfect round-trip would be gold-plating at this stage.
- **1839-line enrich.py with hardcoded pattern matching** — acknowledged as brittle by both architect and contract. Acceptable at prototype. The accuracy_check.py safety net catches regressions.
- **17 skipped test stubs** for LLM/qualitative rules — correct to leave as `[Fact(Skip)]` rather than inventing testable proxies.
- **No new C# projects or dependencies** — this sprint is purely additive tooling. Zero coupling risk.

### No concerning coupling:
The contract's Separation of Concerns map is clean:
- `extract.py` → YAML parsing only (no enrichment/generation knowledge)
- `generate.py` → YAML→Markdown rendering only (no enrichment/extraction knowledge)
- `enrich.py` → YAML→enriched YAML only (no Markdown/C# knowledge)
- `RulesSpecTests.cs` → Pinder.Core public API only (no YAML/Python knowledge)

This is a pipeline where each stage reads the previous stage's output with no backward references. Good.

## Data Flow Traces

### Round-trip pipeline (extract → generate)
- Markdown doc → `extract.py` → YAML with ordered `blocks` list + `sep_cells` metadata → `generate.py` → regenerated Markdown
- Required: block ordering preserved, table column widths in `sep_cells`, legacy fallback for entries without `blocks`
- ✅ All documented in contract. Verified: diffs are archetypes(38), rules-v3(15), anatomy(6), others(2) — all whitespace-only.

### Enrichment pipeline (extract → enrich)
- Base YAML → `enrich.py` pattern matching → enriched YAML with `condition`/`outcome` dicts
- Required: condition keys match evaluator vocabulary, outcome keys match dispatcher vocabulary
- ✅ Vocabulary shared with `rules-v3-enriched.yaml` and documented in contract. 351 enriched entries across 9 files. 0 INACCURATE findings.

### Test stub pipeline (enriched YAML → C# tests)
- `rules-v3-enriched.yaml` entries → `generate_tests.py` → `RulesSpecTests.cs` → xUnit tests → validate Pinder.Core statics
- ✅ 54 tests (37 active + 17 skipped). All 2032 C# tests passing (including 37 RulesSpec).

## Unstated Requirements

- **Pipeline repeatability**: The game designer expects to edit Markdown rules → re-run pipeline → get updated YAML + tests. The contract documents this but there's no single `make rules` command yet. Acceptable to defer — the bonus AC in #445 acknowledged this.
- **Enrichment coverage expectations**: Not all 532 entries are enrichable (many are prose/qualitative). The 351/532 ratio is reasonable — the remaining entries genuinely lack numeric thresholds.

## Domain Invariants

- **Pinder.Core zero-dependency invariant holds** — no changes to Core dependencies this sprint
- **Enriched YAML must be accurate** — 0 INACCURATE findings from accuracy_check.py
- **All existing tests must pass unchanged** — 2032 C# tests passing, 17 skipped
- **Round-trip diffs must stay within tolerance** — <50 lines per doc, whitespace-only

## Requirements Compliance Check

- **FR (Issue #463)**: This sprint is prerequisite infrastructure for #463 (Wire GameSession to Rule Engine). The enriched YAML files produced here are the data that `RuleBook` will load. No conflict.
- **NFR (zero-dependency Core)**: ✅ Preserved — no changes to Pinder.Core project references.
- **NFR (backward compatibility)**: ✅ All existing tests pass unchanged. Test stubs are additive only.
- **Out of scope boundaries respected**: No GameSession changes, no LlmAdapters changes, no rule engine wiring.

## Gaps

- **None blocking**: All three issues have clear ACs, are well-scoped, and the implementation is already merged and verified.
- **Minor (deferred)**: `enrich.py` at 1839 lines is the largest and most brittle component in the pipeline. At MVP, this should be refactored into per-document enrichment modules. The accuracy_check.py mitigates the risk for now.
- **Minor (deferred)**: No single `make rules` or pipeline invocation command. Individual scripts must be run manually. Acceptable at prototype.

## Recommendations

1. No action required — the architecture is sound for prototype maturity.
2. When transitioning to MVP, prioritize refactoring `enrich.py` into modular per-document enrichers to reduce brittleness.
3. The `make rules` pipeline command should be added when the pipeline is used in CI (not needed yet at prototype).

## Verdict

**VERDICT: CLEAN** — architecture aligns with product vision, proceed

The sprint is correctly scoped infrastructure work that serves the product's data-driven balance tuning goal. No structural concerns, no coupling risks, no requirement violations. The architect's contract is well-documented with clean separation of concerns. All implementation is already merged and verified with passing tests.
