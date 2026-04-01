# Vision Review — Sprint 10 Architecture Alignment: LLM Adapter Bug Fixes

## Alignment: ✅

The architect's output correctly addresses all three vision concerns (#243, #244, #245) from the first visionary pass. The contracts are well-scoped, backward-compatible, and appropriate for prototype maturity. No over-engineering detected. The sprint fixes real blockers to playtesting — the right work at the right time.

## Architecture Evaluation

### 1. Maturity Fit — ✅ Appropriate for Prototype

The architect made the right call on several fronts:
- **Hardcoded taint text constants** rather than data-driven loading — correct for prototype. The comment "can be extracted to data files at MVP" shows maturity awareness.
- **Optional parameters with defaults** for all SessionDocumentBuilder changes — zero disruption to existing callers and tests.
- **No structural changes** — all fixes are additive. This is a bug-fix sprint, not an architecture sprint, and the contracts reflect that.
- **No new abstractions introduced** — `GetShadowTaintText()` is a simple static lookup, not a new interface or service. Right choice.

### 2. Coupling Analysis — ✅ No Concerning Coupling

- **LlmAdapters → Core dependency remains strictly one-way.** The contracts don't introduce any reverse dependency.
- **GameSession wiring changes (pass names/thresholds to DTOs)** are mechanical — the fields already exist, the data is already available. No new coupling.
- **`BuildPlayerOnlySystemBlocks`** mirrors the existing `BuildOpponentOnlySystemBlocks` pattern — consistent and predictable.

### 3. Abstraction Reversibility — ✅ Nothing Painful to Undo

- Taint text constants in `PromptTemplates` can trivially be extracted to JSON/config later.
- Optional params on builder methods can be made required later without breaking the pattern.
- `BuildPlayerOnlySystemBlocks` is a simple parallel method — no complex inheritance or generics.
- No decisions in these contracts create lock-in beyond the current sprint.

### 4. Interface Design — ⚠️ One Minor Observation

The `GetShadowTaintText(ShadowStatType, int tier)` method returns tier-specific text. This is clean. However, the `Dictionary<ShadowStatType, int>? shadowThresholds` parameter on builder methods means the builder must call `GetShadowTaintText` for each entry and concatenate. This is fine for prototype, but at MVP the taint rendering logic (iterate dict → look up text → format section) should live in a dedicated helper rather than being duplicated across three builder methods. **Not blocking — note for future.**

## Data Flow Validation

### #240 (Options Format)
- **Flow**: GameSession → ILlmAdapter.GetDialogueOptionsAsync → AnthropicLlmAdapter → SessionDocumentBuilder.BuildDialogueOptionsPrompt → PromptTemplates.DialogueOptionsInstruction → Anthropic API → ParseDialogueOptions
- **Contract trace**: Only `PromptTemplates.DialogueOptionsInstruction` changes. Parser is already correct. ✅ Complete.

### #241 (Delivery Voice)
- **Flow**: GameSession.ResolveTurnAsync → new DeliveryContext(**with names**) → ILlmAdapter.DeliverMessageAsync → AnthropicLlmAdapter → CacheBlockBuilder.**BuildPlayerOnlySystemBlocks** → SessionDocumentBuilder.BuildDeliveryPrompt(**with {player_name} substitution**) → Anthropic API
- **Contract trace**: 
  - GameSession passes `_player.DisplayName`, `_opponent.DisplayName`, `_turnNumber` → ✅
  - CacheBlockBuilder adds `BuildPlayerOnlySystemBlocks` → ✅
  - AnthropicLlmAdapter switches to player-only system blocks → ✅
  - PromptTemplates adds `{player_name}` to FailureDeliveryInstruction → ✅
  - SessionDocumentBuilder performs `{player_name}` replacement → ✅
- **All three vision concerns (#243, #244, #245) addressed.** ✅ Complete.

### #242 (Shadow Taint)
- **Flow**: GameSession computes shadowThresholds → passes to DeliveryContext/OpponentContext → AnthropicLlmAdapter passes to SessionDocumentBuilder → builder calls PromptTemplates.GetShadowTaintText per shadow → appends SHADOW TAINT section → Anthropic API
- **Contract trace**:
  - PromptTemplates adds 18 taint constants (6 shadows × 3 tiers) + `GetShadowTaintText()` → ✅
  - SessionDocumentBuilder gains optional `shadowThresholds` param on all 3 methods → ✅
  - AnthropicLlmAdapter passes `context.ShadowThresholds` through → ✅
  - GameSession wires `shadowThresholds` to DeliveryContext and OpponentContext → ✅
- ⚠️ **Opponent shadow thresholds**: Contract notes GameSession needs to compute opponent shadow thresholds for `OpponentContext`. Currently only player thresholds are computed (line ~227-230). The contract acknowledges this ("new code, similar to player shadow computation"). This is tracked as known gap #242 in architecture.md. The `arch-concern` issue #86 (GameSessionConfig has OpponentShadows but no opponent shadow growth loop) is related but distinct — that's about shadow *growth*, this is about threshold *reading*. The implementer can compute opponent thresholds from `_opponentShadows` (if non-null) using `ShadowThresholdEvaluator`. **Not blocking — implementer guidance is sufficient.**

## Unstated Requirements Check

All unstated requirements from the first pass are addressed:
1. ✅ Taint text covers all 6 shadows × 3 tiers (not just the two mentioned in the issue)
2. ✅ Name wiring (#243) is incorporated into #241 contract
3. ⚠️ Integration test AC on #240 ("real Anthropic call") — still unaddressed. This is a CI concern, not an architecture concern. The implementer will need an API key or must mark this test as `[Explicit]`/manual-only.

## Gaps

### Missing
- None critical. All first-pass vision concerns are incorporated.

### Unnecessary  
- Nothing — these are minimal, targeted fixes.

### Assumptions Needing Validation
- **Assumption**: `_player.DisplayName` and `_opponent.DisplayName` are populated. If `CharacterProfile.DisplayName` is null/empty for some character configurations, the fallback behavior needs definition. **Low risk** — DisplayName is set during character assembly.
- **Assumption**: `SuccessDeliveryInstruction` also needs `{player_name}` (contract #241 adds it "for consistency"). This is correct — success delivery is also the player's voice.

## Recommendations
1. **Proceed with implementation.** Architecture is sound, contracts are complete, all vision concerns incorporated.
2. **Implementer for #242 should check whether `_opponentShadows` can be null** — if so, skip opponent taint gracefully (which the optional param pattern already handles).
3. **Consider adding a `BuildShadowTaintSection(Dictionary<ShadowStatType, int>)` helper** to avoid duplicating the iterate-and-format logic across three builder methods. Not required for prototype, but would keep the builder clean.

**VERDICT: CLEAN**
