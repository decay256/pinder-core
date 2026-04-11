# Vision Review — Sprint 9 (Attempt 2): Anthropic LLM Adapter + Integration Test

## Alignment: ✅

This sprint remains strategically correct. After 8 sprints building a complete RPG engine with 1139 passing tests (1 known failing — #209), connecting to a real LLM is the highest-leverage next step. The sprint is well-scoped at 6 issues, properly sequenced in waves, and preserves the zero-dependency invariant on `Pinder.Core`.

## Vision Concern Review

### #211 — Context DTO fields missing (EDITED ✅)
**Before**: Proposed two options without clear recommendation.
**After**: Option A selected as the definitive approach. Concrete ACs added: add `CurrentTurn`, `PlayerName`, `OpponentName` to `DialogueContext`, `DeliveryContext`, `OpponentContext` with backward-compatible defaults. GameSession passes `_turnNumber`, `_player.DisplayName`, `_opponent.DisplayName`. ~30-line change.
**Status**: Well-specified, actionable.

### #212 — Test project for Pinder.LlmAdapters (EDITED ✅)
**Before**: Noted sln + test project gap but #205 AC already covers sln entry.
**After**: Focused on the actual gap: test project creation. ACs specify csproj targeting net8.0, xUnit references matching existing project, sln integration. Should be done as part of #205.
**Status**: Well-specified, actionable.

### #213 — Beta header outdated (EDITED ✅)
**Before**: "Verify current Anthropic docs" was a research task, not an AC.
**After**: Research done — Anthropic docs confirm prompt caching is GA and beta prefix is no longer needed. ACs now say: remove header, keep `cache_control` in body, add comment. One-line fix during #206 implementation.
**Status**: Well-specified, actionable.

### #214 — Tell/WeaknessWindow ambiguity (EDITED ✅)
**Before**: Three unclear options for how signals flow.
**After**: Concrete recommendation: LLM generates structured `[SIGNALS]` block in §3.5 response, adapter parses it leniently. ACs cover #207 prompt template updates, #208 parsing logic, and unit tests for both cases (with/without signals). ~40 lines of parsing + prompt additions.
**Status**: Well-specified, actionable. Requires PO agreement on the signal format — but the recommendation is sound and implementers can proceed.

## Remaining Gap Check

No new gaps identified. The 4 concerns cover all data flow issues in this sprint:

| Data Flow | Gap | Covered By |
|-----------|-----|-----------|
| Context DTOs → SessionDocumentBuilder | Missing fields | #211 |
| Build infra → CI | Missing test project | #212 |
| HTTP headers → Anthropic API | Outdated beta header | #213 |
| LLM output → GameSession tells/weakness | Parsing ambiguity | #214 |

## Domain Invariants (verified)
- ✅ `Pinder.Core` remains zero-dependency — `Pinder.LlmAdapters` is a separate project
- ✅ `ILlmAdapter` contract unchanged — `AnthropicLlmAdapter` implements the same interface as `NullLlmAdapter`
- ✅ Existing 1139 tests unaffected — context DTO changes use optional parameters with defaults
- ✅ `OpponentResponse` already carries `DetectedTell?` and `WeaknessWindow?` — adapter just needs to populate them

## Verdict: **CLEAN**

All 4 vision concerns are now well-specified with concrete acceptance criteria. The sprint can proceed to architect/implementation. Each concern is either a small addendum to an existing issue (#212→#205, #213→#206) or a clear data flow fix that implementers can address during their issue work (#211→#208, #214→#207+#208).

No blocking gaps remain. Sprint direction is sound.
