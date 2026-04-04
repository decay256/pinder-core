# Architecture Strategic Alignment Review — Sprint 3 (Rules DSL + Rule Engine)

## Alignment: ✅ Strong

This sprint moves toward a data-driven rules system — a clear win for a game engine where balance iteration speed is critical. The architect correctly keeps `Pinder.Core` zero-dependency by placing `Pinder.Rules` in a separate project (same pattern as `Pinder.LlmAdapters`). The wave plan (#443/#445 parallel → #444 → #446) is correctly sequenced. For prototype maturity, proving equivalence via tests rather than wiring directly into GameSession is a pragmatic choice that avoids breaking the zero-dependency invariant.

## Maturity Fit Assessment

### Appropriate for prototype:
- **Untyped condition/outcome dicts** (`Dictionary<string, object>`) — correct for prototype. The heterogeneous YAML schema doesn't warrant type-safe condition classes yet. MVP can add strongly-typed wrappers.
- **Equivalence testing over integration** — proving the rule engine matches hardcoded C# without wiring it into GameSession avoids premature coupling decisions.
- **YamlDotNet as sole dependency** — zero transitive deps, netstandard2.0 compatible. Same isolation pattern as Newtonsoft.Json in LlmAdapters.
- **Python tooling fixes** — round-trip fidelity is infrastructure. Necessary before enrichment can proceed at scale.

### Risks at next maturity level:
- **Two sources of truth for rule values** — hardcoded C# constants AND YAML will coexist after this sprint. The architect acknowledges this ("YAML becomes source of truth at MVP"). The risk is that someone changes a constant in C# but not YAML (or vice versa) during the interim. The equivalence tests mitigate this — they'll fail if the two drift apart.
- **GameSession integration pattern TBD** — the architect chose Option A (host-level wiring) but didn't prototype it. At MVP, the wiring pattern (delegates, middleware, or host orchestration) needs to be decided. This is acceptable to defer.

## Coupling Analysis

No concerning coupling introduced:
- `Pinder.Rules → Pinder.Core` is one-way (correct)
- `Pinder.Core` has zero knowledge of `Pinder.Rules` (correct)
- `GameState` is an independent snapshot — no reference back to GameSession
- `IEffectHandler` is a clean callback interface with no rule-engine-specific types in its signature
- Test stubs (#445) call Pinder.Core APIs directly — no coupling to rule engine

The three-assembly dependency graph remains clean:
```
session-runner → Pinder.LlmAdapters → Pinder.Core
             → Pinder.Rules      → Pinder.Core
```

## ADR Evaluation

### ADR: Pinder.Rules as separate project — ✅ Correct
Follows established pattern. Preserves Core's zero-dependency invariant for Unity compat.

### ADR: No direct GameSession integration — ⚠️ Concern filed
Pragmatically correct, but conflicts with #446's written AC. See arch-concern below.

### ADR: Untyped condition/outcome dictionaries — ✅ Correct for prototype
Runtime type checking is acceptable at this maturity. The condition vocabulary is well-documented in the contract.

## Data Flow Traces

### Rule Evaluation (§5 failure tiers)
- YAML file content → `RuleBook.LoadFrom(yamlContent)` → indexed `RuleEntry` list
- Caller builds `GameState(missMargin: N)` snapshot
- `ConditionEvaluator.Evaluate(entry.Condition, gameState)` checks `miss_range` against `MissMargin`
- `OutcomeDispatcher.Dispatch(entry.Outcome, gameState, handler)` calls `handler.ApplyInterestDelta(delta)`
- Required fields: `miss_range` in condition, `interest_delta` in outcome, `MissMargin` in GameState
- ✅ All fields present in contract

### Rule Evaluation (§6 interest states)
- Same flow as above but with `interest_range` condition key matching `Interest` in GameState
- Outcome dispatches `effect` (advantage/disadvantage) via `handler.SetRollModifier()`
- Required fields: `interest_range` in condition, `effect` in outcome, `Interest` in GameState
- ✅ All fields present in contract

### Python round-trip (extract → generate)
- Markdown file → `extract.py` → YAML with ordered `blocks` list → `generate.py` → regenerated Markdown
- ⚠️ Minor: contract specifies `blocks` field with `separator_widths` but doesn't specify how existing YAML files (without `blocks`) are migrated. The contract does mention "fall back to existing field-by-field emission when `blocks` is absent" — this is sufficient.

## Unstated Requirements

- **Rule engine loading must be fail-fast**: If YAML is malformed, `RuleBook.LoadFrom()` should throw immediately rather than silently producing empty rule sets. The contract specifies `FormatException on invalid YAML` — this is covered.
- **Equivalence tests should cover edge cases**: Not just happy-path mappings, but boundary values (miss margin 0, interest exactly at state boundary, nat 1/nat 20). The contract mentions `EquivalenceTests.cs` but doesn't specify boundary coverage. At prototype, happy-path equivalence is sufficient.

## Domain Invariants

- **Pinder.Core zero-dependency invariant must hold** — no direct or transitive reference from Core to Rules or YamlDotNet
- **Hardcoded C# constants remain canonical this sprint** — rule engine proves equivalence but doesn't replace
- **All 2453 existing tests must pass unchanged**
- **YAML enrichment must be accurate** — structured condition/outcome fields must match the prose description exactly

## Gaps

- **Missing: #446 AC reconciliation** — Two AC items ("GameSession uses the engine" and "No numeric constants remain hardcoded") directly conflict with the architect's decision to defer integration. Filed as arch-concern below.
- **Unnecessary: None** — All four issues are well-scoped for the sprint goals.
- **Assumption: YamlDotNet deserializes heterogeneous dicts correctly** — The contract flags this risk and provides a fallback (manual YamlStream walking). Reasonable mitigation.

## Requirements Compliance

No `REQUIREMENTS.md` found in the repo. No formal FR/NFR/DC entries to check against. The implicit design constraints (netstandard2.0, zero Core dependencies, backward compatibility) are all preserved by this architecture.

## Insufficient Requirements Check

All four issues have adequate context for prototype maturity:
- **#443**: Detailed body (>500 chars), clear AC with 5 checkboxes, specific goal (<50 diff lines per doc) ✅
- **#444**: Detailed body with priority order, enrichment approach, AC ✅
- **#445**: Detailed body with specific file paths, method mappings, AC ✅
- **#446**: Detailed body with architecture, code examples, AC with 6 checkboxes ✅

## Recommendations

1. **Reconcile #446 AC before implementation** — The implementer will face conflicting guidance between the AC ("GameSession uses the engine", "No numeric constants remain hardcoded") and the architect's explicit deferral. Update the AC or add a clarification note so the implementer knows which authority to follow.
2. **Proceed with sprint** — The architecture is sound, well-scoped, and correctly structured for prototype maturity.
