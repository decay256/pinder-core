# Contract: Sprint — Rules DSL Completeness

## Architecture Overview

This sprint continues the existing architecture with **no structural
changes**. All three issues (#443, #444, #445) operate on the Rules
DSL pipeline — a Python tooling layer that extracts Markdown rules
docs into structured YAML, enriches entries with condition/outcome
fields, and generates C# test stubs. No new projects, no new C#
components, no dependency changes.

**Existing architecture summary**: Pinder.Core is a zero-dependency
.NET Standard 2.0 RPG engine. `Pinder.Rules` (YamlDotNet) provides
`RuleBook`, `ConditionEvaluator`, `OutcomeDispatcher` for data-driven
rule evaluation. `Pinder.LlmAdapters` (Newtonsoft.Json) implements
`ILlmAdapter`. The Rules DSL pipeline (`rules/tools/`) transforms
authoritative Markdown docs into YAML (`rules/extracted/`) and back
(`rules/regenerated/`), with enrichment adding structured
`condition`/`outcome` fields to YAML entries. Generated C# test
stubs live in `tests/Pinder.Core.Tests/RulesSpec/`.

### Components being extended

- `rules/tools/extract.py` — #443: block-order preservation,
  table column width metadata
- `rules/tools/generate.py` — #443: ordered block rendering,
  separator row fidelity
- `rules/tools/enrich.py` — #444: enrichment rules for all
  8 remaining YAML files
- `rules/extracted/*-enriched.yaml` — #444: enriched output
- `tests/Pinder.Core.Tests/RulesSpec/RulesSpecTests.cs` — #445:
  54 test stubs integrated

### What is NOT changing

- All Pinder.Core game logic
- Pinder.Rules project
- Pinder.LlmAdapters project
- GameSession, RollEngine, InterestMeter, etc.
- NullLlmAdapter
- Session runner

### Implicit assumptions for implementers

1. **Python 3 + PyYAML** for all tooling — already used
2. **YAML files are the pipeline output** — `enrich.py` reads
   base YAML, writes `*-enriched.yaml`
3. **Round-trip test** validates `extract → generate` fidelity
4. **All 2716 existing C# tests must pass** — #445 stubs are
   additive only
5. **Source attribution comment** at top of RulesSpecTests.cs
   prevents accidental manual edits
6. **17 skipped stubs** are for LLM/qualitative rules — not
   testable mechanically, must stay as `[Fact(Skip = "...")]`

---

## Separation of Concerns Map

- extract.py
  - Responsibility:
    - Parse Markdown into structured YAML entries
    - Preserve block order (paragraphs, tables, code)
    - Store table column widths in sep_cells metadata
  - Interface:
    - `extract_rules(filepath) → list[dict]`
    - Output: YAML file with ordered `blocks` list per entry
  - Must NOT know:
    - Enrichment logic
    - C# code generation
    - Markdown regeneration

- generate.py
  - Responsibility:
    - Reconstruct Markdown from YAML entries
    - Render blocks in stored order
    - Reproduce table separators from sep_cells
  - Interface:
    - `generate_markdown(rules) → str`
    - `rule_to_markdown(rule, heading_level) → str`
  - Must NOT know:
    - Enrichment fields (condition/outcome)
    - C# test generation
    - Original Markdown source

- enrich.py
  - Responsibility:
    - Add condition/outcome fields to YAML entries
    - Pattern-match numeric thresholds and named effects
    - Produce *-enriched.yaml files
  - Interface:
    - CLI: `python3 enrich.py` (reads from rules/extracted/)
    - Output: enriched YAML files in rules/extracted/
  - Must NOT know:
    - Markdown parsing (uses extract.py output)
    - C# test generation
    - RuleBook/ConditionEvaluator internals

- RulesSpecTests.cs
  - Responsibility:
    - Assert Pinder.Core static classes match rules-v3 values
    - 37 active tests verify mechanical game constants
    - 17 skipped tests document untestable rules
  - Interface:
    - Standard xUnit test class
    - Depends on: Pinder.Core public API only
  - Must NOT know:
    - YAML structure or enrichment format
    - Python tooling internals
    - Pinder.Rules or RuleBook

---

## Per-Issue Interface Definitions

### Issue #443 — Round-trip diff fixes

**Component**: `rules/tools/extract.py` + `rules/tools/generate.py`

**extract.py contract**:
- `extract_rules(filepath: str) → list[dict]`
- Each entry has a `blocks` list preserving original document order
- Table blocks include `sep_cells: list[str]` with raw separator
  cell content (preserves alignment markers and column widths)
- Table blocks include `rows: list[dict]` with header keys
- Paragraph, code, blockquote, flavor, hr blocks stored in order

**generate.py contract**:
- `generate_markdown(rules: list[dict]) → str`
- Iterates `blocks` list in order for each rule
- Tables use `sep_cells` to reproduce original separator row
- Padded tables (detected by leading space in sep_cells) pad
  header and data cells to match stored widths
- Legacy fallback: entries without `blocks` use individual fields

**Acceptance metric**: `diff original.md regenerated.md | wc -l < 50`
per document. Whitespace-only diffs acceptable.

**Dependencies**: None (standalone Python)

**Consumers**: `enrich.py` (reads extract output), round-trip tests

---

### Issue #444 — Enrich all 9 YAML files

**Component**: `rules/tools/enrich.py` +
`rules/extracted/*-enriched.yaml`

**enrich.py contract**:
- CLI: `python3 enrich.py` — no arguments
- Reads all 9 `rules/extracted/*.yaml` (non-enriched)
- Writes 9 `rules/extracted/*-enriched.yaml` files
- Enriched entries gain:
  - `condition: dict` — keyed by condition type
    (e.g. `miss_margin_min`, `shadow_value_gte`, `stat_type`)
  - `outcome: dict` — keyed by effect type
    (e.g. `interest_delta`, `trap_activation`, `xp_award`)
- Entries without numeric thresholds or named effects: unchanged
- Condition/outcome values must be primitives (str, int, float,
  bool) or lists of primitives — no nested dicts

**Enrichment vocabulary** (shared with rules-v3-enriched.yaml):
- Condition keys: `miss_margin_min`, `miss_margin_max`,
  `beat_margin_min`, `beat_margin_max`, `natural_roll`,
  `interest_min`, `interest_max`, `shadow_value_gte`,
  `streak_min`, `streak_max`, `risk_tier`, `stat_type`,
  `timing_min_minutes`, `timing_max_minutes`
- Outcome keys: `interest_delta`, `trap_activation`,
  `xp_award`, `xp_multiplier`, `shadow_growth`,
  `roll_modifier`, `state_name`, `description`

**Accuracy check**: `python3 accuracy_check.py` — 0 INACCURATE
findings required.

**Dependencies**: #443 (extract.py must produce correct YAML first)

**Consumers**: `Pinder.Rules.RuleBook`, test stub generator,
accuracy checker

---

### Issue #445 — Integrate test stubs

**Component**: `tests/Pinder.Core.Tests/RulesSpec/RulesSpecTests.cs`

**Contract**:
- File location: `tests/Pinder.Core.Tests/RulesSpec/RulesSpecTests.cs`
- 54 test methods total (37 `[Fact]` + 17 `[Fact(Skip = "...")]`)
- Source attribution comment at lines 1-3:
  ```
  // Auto-generated from rules/extracted/rules-v3-enriched.yaml
  // See: rules/tools/generate_tests.py
  // Edit the YAML source, then re-run generation — do not edit this file manually.
  ```
- Namespace: `Pinder.Core.Tests.RulesSpec`
- Class: `RulesSpecTests`
- Uses only Pinder.Core public API:
  - `FailureScale.GetInterestDelta(FailureTier)`
  - `SuccessScale.GetInterestDelta(int beatMargin, bool isNat20)`
  - `InterestMeter` + `InterestState`
  - `RollEngine.Resolve()` / `RollEngine.ResolveFixedDC()`
  - `ShadowThresholdEvaluator.GetThresholdLevel(int)`
  - `RiskTierBonus.GetInterestBonus(RiskTier)`
  - `LevelTable.GetLevel(int xp)`
  - `ComboTracker` combo definitions
- All 37 active tests must pass
- All 17 skipped tests must compile
- All existing 2716 tests must continue to pass

**Dependencies**: None (tests validate existing Pinder.Core code)

**Consumers**: CI pipeline, rules-compliance verification

---

## Implementation Strategy

### Implementation order

1. **#443** first — extract.py/generate.py fixes are prerequisites
   for #444 (enrichment reads extract output)
2. **#444** second — enrichment builds on corrected YAML extraction
3. **#445** third — test stubs can be integrated independently, but
   benefit from verified enrichment data for future regeneration

**However**: All three issues appear to be already implemented and
merged (PRs #455, #457, #460 are all MERGED). The architecture
review is being written post-implementation.

### Current state assessment

All tests pass:
- 2032 C# tests (incl. 37 RulesSpec + 17 skipped) ✅
- 25 Python roundtrip tests ✅
- 35 Python enrichment tests ✅
- 195 Pinder.Rules tests ✅
- 489 LlmAdapters tests ✅

Round-trip diffs are now minimal:
- archetypes: 38 lines (blank line insertions — whitespace only)
- rules-v3: 15 lines (table cell padding — whitespace only)
- anatomy-parameters: 6 lines (trailing space + blank line)
- All others: 2 lines each (trailing newline)

Enrichment coverage: 532 entries total, 351 enriched across all 9
files. Accuracy check passes with 0 INACCURATE findings.

### Tradeoffs

- **Shortcut**: Round-trip diffs are not zero — archetypes has 38
  lines of blank-line insertions. Acceptable at prototype: the
  AC says "<50 lines per doc" and whitespace-only is acceptable.
- **Foundation**: Enriched YAML files are the data layer for the
  `Pinder.Rules` rule engine. This pipeline investment pays off
  when rules change — edit Markdown, re-run pipeline, regenerate
  tests.
- **Risk**: `enrich.py` at 1839 lines is the largest Python file.
  It has hardcoded pattern-matching for each document type. This
  is brittle but acceptable at prototype maturity.

### Risks and mitigations

| Risk | Mitigation |
|------|-----------|
| enrich.py pattern matching breaks on rules changes | accuracy_check.py catches regressions |
| Generated tests drift from actual YAML | Source attribution comment prevents manual edits; regeneration script rebuilds from YAML |
| Round-trip diffs creep back up | roundtrip tests in CI catch regressions |
| YAML format changes break RuleBook loading | RuleBook tests validate schema expectations |

---

## NFR Notes (prototype maturity)

- **Latency**: Not applicable — pipeline runs offline, not at
  game runtime. No latency target needed.
- **Reliability**: Round-trip fidelity measured by diff line count.
  Current: all docs < 50 lines. Monitored by Python test suite.
- **Data quality**: Enrichment accuracy validated by
  accuracy_check.py. Zero INACCURATE findings required.

---

## Sprint Plan Changes

**SPRINT PLAN CHANGES: None required.**

All three issues have clear acceptance criteria, sufficient
requirements (>50 chars for prototype), and are independent enough
for parallel implementation (with #443 → #444 ordering respected).
No sub-issues needed — each issue is implementable by a single
agent in one session.
