# Vision Review — Sprint 9: Anthropic LLM Adapter + Integration Test

## Alignment: ⚠️

This sprint is **strategically correct and well-sequenced**. After 8 sprints building a complete RPG engine with 1139 passing tests, connecting it to a real LLM is the natural next step — it's the difference between a rules library and a playable game. The decision to create `Pinder.LlmAdapters` as a separate project preserves the zero-dependency invariant on `Pinder.Core` (critical for Unity). The bug fix (#209) and integration test (#210) are good hygiene alongside the feature work.

However, there are **data flow gaps** between `GameSession`'s context DTOs and what `SessionDocumentBuilder` needs, plus an ambiguity in how Tell/WeaknessWindow signals flow from LLM output back into game state.

## Data Flow Traces

### GetDialogueOptionsAsync (§3.2)
- `GameSession.StartTurnAsync()` → builds `DialogueContext` → `ILlmAdapter.GetDialogueOptionsAsync(context)` → **AnthropicLlmAdapter** → `SessionDocumentBuilder.BuildDialogueOptionsPrompt(history, opponentLastMessage, traps, interest, currentTurn, playerName, opponentName)` → format with `[T{n}|PLAYER|name]` markers → Anthropic API → parse 4 `DialogueOption[]`
- Required fields: conversationHistory, opponentLastMessage, activeTraps, currentInterest, **currentTurn**, **playerName**, **opponentName**
- ⚠️ **Missing (#211)**: `DialogueContext` has no `CurrentTurn`, `PlayerName`, or `OpponentName`. `SessionDocumentBuilder` needs them for §3.2 markers. `GameSession` has `_turnNumber` and `_player.DisplayName`/`_opponent.DisplayName` but doesn't pass them to the context.

### GetOpponentResponseAsync (§3.5) → Tell/WeaknessWindow
- `GameSession.ResolveTurnAsync()` → builds `OpponentContext` → `ILlmAdapter.GetOpponentResponseAsync(context)` → **AnthropicLlmAdapter** → Anthropic API → parse `OpponentResponse(messageText, detectedTell?, weaknessWindow?)` → GameSession stores `_activeTell` and `_activeWeakness` for next turn
- Required: LLM output must contain parseable Tell/WeaknessWindow signals
- ⚠️ **Ambiguous (#214)**: Issue #208 says "these come from context, not LLM output" but `GameSession` reads them from `OpponentResponse.DetectedTell` and `OpponentResponse.WeaknessWindow` (lines 582-585). If the adapter always returns null, tells (+2 bonus) and weakness windows (DC −2/−3) are dead mechanics. §3.5 prompt says "optionally include a tell" but also "Output only the message text" — conflicting instructions.

### DeliverMessageAsync (§3.3/3.4)
- `GameSession.ResolveTurnAsync()` → builds `DeliveryContext` → adapter → `SessionDocumentBuilder.BuildDeliveryPrompt(...)` → Anthropic → raw text
- ✅ Clean flow. All needed fields on `DeliveryContext`.

### GetInterestChangeBeatAsync (§3.8)
- `GameSession.ResolveTurnAsync()` → threshold crossed → `InterestChangeContext` → adapter → `SessionDocumentBuilder.BuildInterestChangeBeatPrompt(...)` → Anthropic → narrative text
- ✅ Clean flow. `InterestChangeContext` has `OpponentName`, `InterestBefore`, `InterestAfter`, `NewState`.

### Cache Strategy
- Player prompt + opponent prompt → `ContentBlock[]` with `cache_control: ephemeral` → reused across turns
- ✅ Sound design. System blocks are large and static per session — prompt caching saves ~90% of input tokens.

## Unstated Requirements

- **Users expect the Anthropic adapter to produce gameplay-meaningful responses**: `NullLlmAdapter` returns generic placeholders. The Anthropic adapter is the first time real LLM text flows through the engine. If Tell/WeaknessWindow parsing doesn't work, the game plays flat — no tells, no cracks, no tactical depth from turns 2+.
- **Users expect cost visibility**: Anthropic API calls cost money. The `UsageStats` DTO captures token counts — the adapter or host should expose cumulative cost per session. Not in this sprint's scope, but the infrastructure (#205 `UsageStats`) enables it.
- **Test coverage for the new project**: Issues #206/#207/#208 all specify unit tests, but there's no test project in the sprint scope. The implementer needs to create it.

## Domain Invariants

- `Pinder.Core` must remain zero-dependency — no `Newtonsoft.Json` or HTTP references may leak into it ✅ (separate project)
- `ILlmAdapter` contract is the boundary — `AnthropicLlmAdapter` must satisfy the same contract as `NullLlmAdapter`
- Dialogue option parsing must never throw — fallback to safe defaults per #208 spec ✅
- `cache_control` blocks must be deterministic for the same session (player/opponent prompts don't change mid-session)
- Retry logic must not swallow errors silently — final failure must propagate as `AnthropicApiException`

## Gaps

### ⚠️ Concerns Filed
- **#211 (BLOCKING for #207/#208)**: `SessionDocumentBuilder` needs `currentTurn`, `playerName`, `opponentName` — not on context DTOs. The adapter cannot format §3.2 markers without them.
- **#212**: #205 must add new project to `.sln` and create/reference a test project
- **#213**: `anthropic-beta: prompt-caching-2024-07-31` header may be outdated (prompt caching is GA)
- **#214**: Tell/WeaknessWindow flow from LLM output is ambiguous — #208 spec contradicts `GameSession` expectations

### Missing (not filed — minor)
- No AC in #205 for adding the project to the solution file
- #210 integration test doesn't test the adapter at all (uses NullLlmAdapter) — it's really a GameSession integration test, not an adapter test. This is fine for this sprint but naming is misleading.

### Could Defer
- Nothing. This is a tight, well-scoped sprint. All 6 issues serve the goal.

### Assumptions to Validate
- Anthropic's `prompt-caching-2024-07-31` beta header is still required/accepted (March 2026)
- `netstandard2.0` can reference `Newtonsoft.Json 13.0.3` without issues (it can — this is well-tested)
- §3.2 prompt format with `[T{n}|PLAYER|name]` markers is the optimal format for Claude (vs. XML tags or other structuring)

## Wave Plan

```
Wave 1: #205, #209
Wave 2: #206, #207, #210
Wave 3: #208
```

Rationale:
- **Wave 1**: #205 (project setup) has no deps. #209 (bug fix) has no deps. Both are independent foundations.
- **Wave 2**: #206 (HTTP client) depends on #205 DTOs. #207 (prompt builder) depends on #205 DTOs. #210 (integration test) depends on #209 (fix the failing test first). All three can run in parallel.
- **Wave 3**: #208 (adapter) depends on #205 + #206 + #207 — all must be complete.

## Role Assignments

All issues are assigned `backend-engineer`. Reviewed each:
- #205 (project setup + DTOs) → backend-engineer ✅
- #206 (HTTP client + retry) → backend-engineer ✅
- #207 (prompt formatting) → backend-engineer ✅
- #208 (adapter implementation) → backend-engineer ✅
- #209 (test fix) → backend-engineer ✅
- #210 (integration test) → backend-engineer ✅ (deep domain knowledge required)

All roles are correct.

## Recommendations

1. **Resolve #211 before Wave 2**: Add `CurrentTurn`, `PlayerName`, `OpponentName` to context DTOs (backward-compatible with defaults). This unblocks `SessionDocumentBuilder` formatting.
2. **Resolve #214 before Wave 3**: Decide whether Tell/WeaknessWindow comes from LLM parsing or a separate game-logic mechanism. The adapter spec must match `GameSession`'s expectations.
3. **#205 implementer should add sln entry and test project** per #212 — otherwise the rest of the sprint has no build/test infrastructure.
4. **Verify Anthropic beta header** — 5-minute check against current docs, avoids a runtime surprise.

## Verdict

**ADVISORY** — Four concerns filed (#211, #212, #213, #214). #211 is the most significant: it's a data flow gap that will cause #207/#208 to produce incorrect prompts. The sprint should add #211 resolution to Wave 1 scope (context DTO expansion) before proceeding to Wave 2. #214 needs a design decision but can be resolved during #208 implementation. No full blockers — the sprint direction is sound.
