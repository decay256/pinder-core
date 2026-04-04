# Pinder.Core — Functional Requirements

## Sprint: Rules DSL Completeness

### Issue #443 — Rules DSL: fix remaining round-trip diffs (paragraph reordering, table formatting)

#### User Story

As a game designer, I want the round-trip conversion (Markdown → YAML → Markdown) to preserve the original document structure faithfully, so that I can trust the extraction pipeline doesn't reorder or reformat my rules documents.

#### Acceptance Criteria

- [ ] Paragraph reordering fixed — extracted/generated order matches original
- [ ] Table column widths preserved
- [ ] Re-run roundtrip_test.sh on all 9 docs
- [ ] All diffs < 50 lines per doc
- [ ] Report: before/after line counts

#### NFR Notes (flag for Architect)

- **Reliability**: Round-trip fidelity is critical for the rules-as-data pipeline — formatting drift erodes trust in the tooling.

#### Out of Scope

- Enrichment of YAML files (that's #444)
- C# test integration (that's #445)
- Semantic changes to rules content

---

### Issue #444 — Rules DSL: enrich all 9 YAML files with explicit condition/outcome fields

#### User Story

As a game designer, I want all 9 rules YAML files to have structured `condition`/`outcome` fields on entries with numeric thresholds or named effects, so that the rule engine can evaluate them programmatically.

#### Acceptance Criteria

- [ ] All 9 docs have enriched YAML files
- [ ] Enriched entries per doc reported
- [ ] Accuracy check run on all enriched entries
- [ ] 0 INACCURATE findings
- [ ] Total enriched entries across all docs reported

#### NFR Notes (flag for Architect)

- **Data quality**: Enriched fields must exactly match the prose rules — no invented or approximated values. Accuracy check is mandatory.

#### Out of Scope

- Round-trip formatting fixes (that's #443)
- C# test integration (that's #445)
- Rule engine implementation or wiring

---

### Issue #445 — Rules DSL: integrate generated test stubs into pinder-core test suite

#### User Story

As a developer, I want the 54 generated rules-spec test stubs to be part of the pinder-core test suite and run in CI, so that any code changes that violate rules-v3 are caught automatically.

#### Acceptance Criteria

- [ ] 54 generated tests in `tests/Pinder.Core.Tests/RulesSpec/RulesSpecTests.cs`
- [ ] All 54 compile and pass (no failures, 17 stubs remain as skipped/NotImplemented)
- [ ] Source attribution comment at top of file
- [ ] All existing 2238 tests still pass
- [ ] Build clean

#### NFR Notes (flag for Architect)

- **Maintainability**: Tests are auto-generated from YAML — source attribution comment prevents manual edits that would be overwritten.
- **Backward compatibility**: All existing tests must continue to pass unchanged.

#### Out of Scope

- Auto-regeneration pipeline (bonus item, not required for acceptance)
- Enrichment of YAML files (that's #444)
- Rule engine wiring to GameSession

---

## PM Pre-flight Summary

### Issue #443 — Already well-formed (no changes needed)

| Check | Status | Notes |
|-------|--------|-------|
| Acceptance Criteria | ✅ Present | 5 checkbox items |
| Description quality | ✅ Present | Clear context with categories, goal, and metrics |
| Role field | ✅ Present | `backend-engineer` |
| Maturity field | ✅ Present | `prototype` |
| Concern Type | N/A | Not a vision-concern issue |

### Issue #444 — Already well-formed (no changes needed)

| Check | Status | Notes |
|-------|--------|-------|
| Acceptance Criteria | ✅ Present | 5 checkbox items |
| Description quality | ✅ Present | Clear context with priority order and enrichment scope |
| Role field | ✅ Present | `backend-engineer` |
| Maturity field | ✅ Present | `prototype` |
| Concern Type | N/A | Not a vision-concern issue |

### Issue #445 — Already well-formed (no changes needed)

| Check | Status | Notes |
|-------|--------|-------|
| Acceptance Criteria | ✅ Present | 5 checkbox items |
| Description quality | ✅ Present | Clear context with step-by-step instructions and bonus item |
| Role field | ✅ Present | `backend-engineer` |
| Maturity field | ✅ Present | `prototype` |
| Concern Type | N/A | Not a vision-concern issue |

---

## Sprint: Wire GameSession to Rule Engine

### Issue #463 — Wire GameSession to use RuleEngine for §5/§6/§7/§15 rules

#### User Story

As a game designer, I want GameSession to resolve §5/§6/§7/§15 rules through the RuleEngine (loaded from YAML) instead of hardcoded C# constants, so that I can tune game balance by editing YAML without recompilation.

#### Acceptance Criteria

- [ ] `RuleBook` loaded from `rules/extracted/rules-v3-enriched.yaml` at session start
- [ ] §5 failure tier → interest delta flows through the engine
- [ ] §6 interest ranges → InterestState flows through the engine
- [ ] §7 shadow thresholds flow through the engine
- [ ] §15 momentum bonuses flow through the engine
- [ ] §15 risk tier XP multipliers flow through the engine
- [ ] All 45 `RulesSpecTests` assertions pass against the wired implementation
- [ ] All 17 `NotImplementedException` stubs remain as stubs (LLM/qualitative effects)
- [ ] All 2507 existing tests still pass
- [ ] Fallback to hardcoded constants if YAML missing/corrupt
- [ ] Build clean

#### NFR Notes (flag for Architect)

- **Performance**: RuleBook is loaded once at GameSession construction, not per-turn. Rule lookups should be O(1) or O(n) where n is small (< 100 rules per type).
- **Reliability**: Fallback to hardcoded constants if YAML is missing or corrupt — zero regression risk.
- **Dependency**: `Pinder.Rules` project (with YamlDotNet) must NOT be referenced by `Pinder.Core`. The wiring must respect the one-way dependency (`Pinder.Rules → Pinder.Core`). GameSession integration requires either:
  - An abstraction layer (interface) in Core that Rules implements, OR
  - The wiring happens at the session-runner/host level, passing resolved values into GameSession
- **Backward compatibility**: All 2507 existing tests must continue to pass unchanged.

#### Out of Scope

- Wiring LLM/qualitative rules (the 17 skipped stubs)
- Removing hardcoded constants from static classes (they remain as fallback)
- Multi-session (ConversationRegistry) integration
- Changes to Pinder.LlmAdapters

#### Dependencies

- #446 (rule engine exists) — must be merged first

## PM Pre-flight Summary

### Issue #463 — Improved

| Check | Status | Notes |
|-------|--------|-------|
| Acceptance Criteria | ✅ Already present | 11 checkbox items covering load, wiring, tests, fallback |
| Description quality | ✅ Already present | Clear context, phased plan, rationale |
| Role field | ❌ → ✅ Added | `backend-engineer` |
| Maturity field | ❌ → ✅ Added | `prototype` |
| Concern Type | N/A | Not a vision-concern issue |

**Changes made**: Added `**Role**: backend-engineer` and `**Maturity**: prototype` fields to the issue body. Posted a comment documenting the change.
