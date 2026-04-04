# Pinder.Core — Functional Requirements

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
