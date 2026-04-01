# Vision Review — Sprint 11: LLM Adapter Bug Fixes (#242)

## Alignment: ⚠️

This sprint contains a single issue (#242 — shadow taint injection into LLM prompts) which is the right work at the right time. Shadow taint is a core gameplay differentiator: as shadows grow, the *tone* of conversation should shift — uncanny, melancholy, obsessive. Without taint injection, shadows are purely mechanical (disadvantage at T2+) and invisible to the player/LLM. Fixing this is high-leverage for the playtesting milestone. However, the issue is underspecified and has data flow gaps that will produce an incomplete fix if not addressed.

## Data Flow Traces

### #242 — Shadow taint injection into LLM prompts

**Dialogue Options path (player shadow → player options):**
- `GameSession.StartTurnAsync()` → computes `shadowThresholds` dict from `_playerShadows` → passes to `DialogueContext` ✅ → `AnthropicLlmAdapter.GetDialogueOptionsAsync()` → `SessionDocumentBuilder.BuildDialogueOptionsPrompt()` — **has no shadow parameter, ignores context.ShadowThresholds entirely**
- Required: `BuildDialogueOptionsPrompt` must accept `Dictionary<ShadowStatType, int>?` and append taint text
- ⚠️ **Missing**: `SessionDocumentBuilder.BuildDialogueOptionsPrompt()` has no shadow parameter. `AnthropicLlmAdapter` does not read `context.ShadowThresholds`. (#244 already filed)

**Delivery path (player shadow → degraded message):**
- `GameSession.ResolveTurnAsync()` → creates `DeliveryContext` — **does NOT pass shadowThresholds** (null default) → adapter → builder → no taint
- Required: GameSession must store thresholds from `StartTurnAsync` and pass them in `ResolveTurnAsync`
- ⚠️ **BLOCKING**: `shadowThresholds` is a local variable in `StartTurnAsync()`, not a field. It's unavailable in `ResolveTurnAsync()`. Filed as #253.

**Opponent Response path (opponent shadow → opponent response tone):**
- `GameSession.ResolveTurnAsync()` → creates `OpponentContext` — **does NOT pass shadowThresholds** → adapter → builder → no taint
- Required: Opponent shadow thresholds must be computed from `_opponentShadows` and passed to `OpponentContext`
- ⚠️ **Missing**: `_opponentShadows` is stored but never evaluated for thresholds. No opponent threshold computation exists anywhere. Filed as #254.

## Unstated Requirements

- **All 6 shadow stats need taint text, not just Madness and Dread.** The AC only mentions Madness (uncanny) and Dread (melancholy). But Fixation, Denial, Cringe, and Loneliness also have thematic effects at T1/T2/T3 that need LLM instruction text. The implementer has no guidance for these.
- **Taint should be progressive across tiers.** T1 = subtle undertone, T2 = pronounced shift, T3 = overwhelming. The AC only specifies T1 behavior. The implementer needs all three tiers defined.
- **Taint is bidirectional.** Player shadows taint the player's options and delivery. Opponent shadows taint the opponent's responses. The AC implies this ("opponent response prompt includes uncanny quality") but the data flow only supports player shadow thresholds.

## Domain Invariants

- **Shadow taint is additive, not replacing.** Taint modifies the character's voice — it doesn't create a new voice. Sable with Dread=8 is still Sable, just with a melancholy edge.
- **Taint text must be deterministic given threshold tier.** Same shadow value → same taint instruction. No randomness in taint selection (randomness is in the LLM's interpretation).
- **Backward compatibility.** All `SessionDocumentBuilder` method signature changes must use optional parameters with null defaults. Existing tests must pass unchanged.

## Gaps

### Missing (should be in this sprint)
- **#253 (filed)**: GameSession doesn't pass shadowThresholds to DeliveryContext or OpponentContext. Without this, taint only appears in dialogue options, not in delivery or opponent responses.
- **#254 (filed)**: Opponent shadow thresholds are never computed. `_opponentShadows` is stored but unused for threshold evaluation.
- **#255 (filed)**: No spec document exists for #242. PR #249 was mislabeled and contained the #240 spec instead. The implementer needs a spec covering taint text for all shadow stats × tiers and the SessionDocumentBuilder API changes.

### Stale vision concerns (resolved by #241)
- **#243**: GameSession now passes playerName/opponentName to DeliveryContext and OpponentContext (fixed in PR #252).
- **#245**: DeliverMessageAsync now uses player-only system blocks (fixed in PR #252).
- These should be closed to reduce noise.

### Unnecessary
- Nothing — the single issue is necessary work.

### Assumptions needing validation
- **§3.6 taint text content**: Does the rules document (`design/systems/rules-v3.md`) contain explicit taint text for all 6 shadows × 3 tiers? If not, who writes it — the implementer or the designer?
- **Scope of "all relevant LLM calls"**: Does this include `GetInterestChangeBeatAsync`? That call is about the opponent's meta-reaction to interest change — shadow taint might not apply there.

## Recommendations

1. **Write a spec for #242 before implementation** (#255). The issue scope is bigger than it appears — it touches SessionDocumentBuilder API, PromptTemplates constants, AnthropicLlmAdapter wiring, and GameSession field storage. A spec prevents rework.
2. **Incorporate #253 into #242's implementation** — storing `shadowThresholds` as a GameSession field and passing it to DeliveryContext/OpponentContext is ~10 lines but critical for completeness.
3. **Defer #254 (opponent shadow thresholds) to a follow-up** — computing opponent thresholds requires new OpponentContext fields and is genuinely new scope. #242 should focus on player shadow taint first.
4. **Close stale vision concerns #243 and #245** — both were resolved by the #241 implementation (PR #252).

## Verdict: **ADVISORY**

Three vision concerns filed (#253, #254, #255). The sprint direction is correct but #242 needs a spec before implementation. The most critical gap is #253 (shadowThresholds not flowing to delivery/opponent contexts) — without it, taint only appears in 1 of 3 LLM calls. #254 (opponent thresholds) can be deferred. The sprint should incorporate #253 and #255 resolution before the backend-engineer starts coding.
