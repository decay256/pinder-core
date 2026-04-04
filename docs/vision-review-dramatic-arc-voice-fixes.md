# Vision Review: Dramatic Arc + Voice Fixes Sprint

**Reviewer**: product-visionary  
**Date**: 2026-04-04  
**Sprint**: Dramatic Arc + Voice Fixes  
**Issues**: #487, #489, #490, #491, #492, #493  
**Maturity**: prototype  

## 1. Maturity Fit Assessment

**Rating: APPROPRIATE**

All 6 issues are prompt engineering with minor DTO plumbing. This is exactly the right work at prototype maturity — tuning LLM output quality before investing in mechanical enforcement. The architect correctly identifies that "4 of 6 issues are purely prompt text changes" and calls this "correct for prototype maturity."

No over-engineering detected:
- No new abstractions, interfaces, or projects
- No new cross-project dependencies
- All DTO changes use optional constructor params with defaults (zero breaking changes)
- Prompt constants in `PromptTemplates` are trivially replaceable

## 2. Coupling Analysis

### TextingStyleFragment on CharacterProfile (#489)

**Concern level: LOW**

Adding `TextingStyleFragment` to `CharacterProfile` (Core) for a concern primarily consumed by `LlmAdapters` creates a minor leak of LLM concerns into Core. However, the architect's justification is sound:

1. The data originates from `FragmentCollection.TextingStyleFragments` which Core already owns
2. `session-runner/LlmPlayerAgent` also needs it (not just the adapter)
3. `CharacterProfile` is already a data carrier — adding one more string field is proportional

The alternative (parsing texting style out of the assembled system prompt in SessionDocumentBuilder) would be fragile and couple the adapter to prompt format internals. The current choice is the lesser coupling.

### DeliveryTier on OpponentContext (#493)

**Concern level: NONE**

`FailureTier` is already a Core type (`Pinder.Core.Rolls`). Passing it through `OpponentContext` is natural — the opponent needs to know how badly the player's message was corrupted to react appropriately. This is game-state data, not LLM implementation detail.

### Opponent prompt in user message (#487)

**Concern level: LOW — monitor**

Moving opponent prompt from cached system blocks to uncached user message increases per-turn token cost. The architect flags this. At prototype (low session volume), acceptable. At scale, will need either:
- A second cached system block with voice-separation framing
- Shorter opponent context summaries instead of full prompts

No action needed now — this is a known tradeoff with a clear upgrade path.

## 3. Roadmap Alignment

### GameSession god object (1454 lines)

The sprint adds ~5 lines to GameSession (wiring FailureTier and TextingStyle through to context DTOs). This does not materially worsen the god-object trajectory. The planned extraction at MVP maturity remains viable — these new wirings are trivial to relocate.

### Prompt architecture direction

The sprint establishes a clear pattern: game mechanics in Core, prompt text in PromptTemplates, prompt assembly in SessionDocumentBuilder, block strategy in CacheBlockBuilder. Each issue follows this separation cleanly. This is the right layering for evolving prompts independently of game logic.

### LlmPlayerAgent rework (#492)

Adding character context (system prompt, texting style, conversation history, scoring EV) to the LLM player agent is directionally correct for playtest quality. The agent living in `session-runner/` (not Core) means it can evolve freely without impacting the engine.

## 4. Abstraction Durability

No new abstractions introduced. Existing abstractions used:
- `ILlmAdapter` interface boundary — respected, no changes
- Context DTO pattern (optional params) — extended naturally
- `PromptTemplates` as static constants — prompt text is easily iterable

**Nothing here will be painful to undo at MVP.** The prompt text can be rewritten freely. The DTO fields can be removed or made non-optional. The system block strategy can be switched back or to a third pattern.

## 5. Wave Plan Assessment

The 3-wave plan respects real dependencies:
- Wave 1: #487 (voice bleed fix), #490, #491, #493 — independent
- Wave 2: #489 (texting style) — depends on #487's prompt structure
- Wave 3: #492 (LlmPlayerAgent) — depends on #489's TextingStyleFragment

This is correct and maximizes parallelism.

## 6. Gaps Noted

| Gap | Risk | Action |
|-----|------|--------|
| Prompt caching cost increase (#487) | Low at prototype | Monitor — architect flagged it |
| GameSession 1454 lines | Medium at MVP | Tracked as known gap #87 |
| TextingStyleFragment populated by loaders, not PromptBuilder | Low | Session-runner loaders own this — documented |
| No automated voice quality tests | Expected at prototype | Qualitative verification via session playtest is correct for maturity |

## Verdict

**VERDICT: CLEAN**

The architecture aligns with product vision. The sprint is appropriately scoped for prototype maturity — prompt engineering tuning with minimal structural changes. No over-engineering, no painful abstractions, no coupling that conflicts with the roadmap. The wave plan is sound. Proceed with implementation.
