# Vision Review — Sprint 11 (Attempt 2): LLM Adapter Bug Fixes (#242)

## Alignment: ⚠️ → Improving

This sprint's single issue (#242 — shadow taint injection) is the right work. Shadow taint is what makes shadow growth *visible* to the player through LLM-generated text — without it, shadows are invisible mechanics. The previous vision pass identified three data flow gaps (#253, #254, #255). This review verifies those concerns are now well-specified and checks for remaining gaps.

## Vision Concern Status

### #253 — GameSession doesn't pass shadowThresholds to DeliveryContext/OpponentContext
**Status: Well-specified ✅**
- Clear problem: `shadowThresholds` is a local variable in `StartTurnAsync()`, unavailable in `ResolveTurnAsync()`
- Clear fix: promote to field, pass to both DTO constructors
- DTOs already have the optional parameter — just needs wiring
- **Should be incorporated into #242 scope** (not a separate issue)

### #254 — Opponent shadow thresholds never computed
**Status: Well-specified ✅ (updated this pass)**
- Updated to recommend **deferral to follow-up issue** — #242 should focus on player shadow taint first
- Added concrete AC for the eventual follow-up (compute from `_opponentShadows`, pass to `OpponentContext`, render in `BuildOpponentPrompt`)
- Not blocking for #242

### #255 — No spec document for #242
**Status: Well-specified ✅**
- Clear deliverable: `docs/specs/issue-242-spec.md`
- Spec must cover taint text for all 6 shadow stats × 3 tiers, SessionDocumentBuilder API changes, adapter wiring
- **Must be written before implementation begins** (architect phase)

### #244 — SessionDocumentBuilder changes not scoped
**Status: Well-specified ✅ (from previous pass)**
- Detailed data flow gap analysis for all three builder methods
- Overlaps with #255 (spec) — the spec should incorporate this concern's checklist
- No edit needed

### #243 — GameSession playerName/opponentName wiring
**Status: ✅ RESOLVED (updated this pass)**
- Fixed by PR #252 (issue #241). Body updated to reflect resolution.

### #245 — DeliverMessageAsync sends both system prompts
**Status: ✅ RESOLVED (updated this pass)**
- Fixed by PR #252 (issue #241). Body updated to reflect resolution.

## Data Flow Trace (updated)

### Shadow taint: user plays turn → taint appears in LLM output

```
StartTurnAsync():
  _playerShadows.GetEffectiveShadow() → per shadow stat
  ShadowThresholdEvaluator.GetThresholdLevel() → tier (0/1/2/3)
  Store as _shadowThresholds field (NEW — #253)              ← currently local var
  Pass to DialogueContext.ShadowThresholds ✅ (already done)

  AnthropicLlmAdapter.GetDialogueOptionsAsync():
    Read context.ShadowThresholds                            ← NOT DONE (adapter ignores it)
    SessionDocumentBuilder.BuildDialogueOptionsPrompt(shadowThresholds)  ← NO PARAM EXISTS
    PromptTemplates shadow taint text                        ← NO TEXT EXISTS

ResolveTurnAsync():
  Pass _shadowThresholds to DeliveryContext                  ← NOT DONE (#253)
  Pass _shadowThresholds to OpponentContext                  ← NOT DONE (#253)

  AnthropicLlmAdapter.DeliverMessageAsync():
    Same gap — adapter/builder don't read/render thresholds

  AnthropicLlmAdapter.GetOpponentResponseAsync():
    Same gap — adapter/builder don't read/render thresholds
```

**Required changes (all in #242 scope):**
1. GameSession: promote `shadowThresholds` to field, pass to DeliveryContext + OpponentContext
2. AnthropicLlmAdapter: read `ShadowThresholds` from all 3 contexts, pass to builder
3. SessionDocumentBuilder: accept optional `Dictionary<ShadowStatType, int>?` on all 3 build methods
4. PromptTemplates: add taint text constants for 6 shadows × 3 tiers

## Remaining Gaps Check

### Previously missed concerns?

**No new gaps found.** The four open concerns (#253, #254, #255, #244) cover the complete data flow from GameSession → adapter → builder → prompt text. The resolution path is:

1. Architect writes spec (#255) incorporating #244's checklist and #253's wiring fix
2. Backend engineer implements spec
3. #254 (opponent thresholds) deferred to follow-up

### Stale concerns to close

- **#243** — resolved by PR #252 (body updated)
- **#245** — resolved by PR #252 (body updated)

These should be closed by the PO to reduce noise (14 of the 21 open vision concerns are already resolved).

## Domain Invariants

- Shadow taint text is deterministic: same shadow type + tier = same instruction text
- Taint is additive — modifies character voice, doesn't replace it
- All SessionDocumentBuilder changes use optional params with null defaults (backward-compatible)
- Shadow taint is cosmetic (flavor text for LLM) — mechanical effects (disadvantage at T2+) are already handled by GameSession

## Verdict: **CLEAN**

All three concerns from the first pass (#253, #254, #255) are now well-specified with concrete acceptance criteria. #254 has been scoped as a follow-up (not blocking). #243 and #245 have been marked resolved. The sprint can proceed to the architect phase, which must produce `docs/specs/issue-242-spec.md` (#255) before implementation begins.

**No blocking gaps remain.** The data flow is fully traced and all missing pieces are documented in actionable issues.
