# Vision Review — Sprint 10: LLM Adapter Bug Fixes

## Alignment: ✅

This sprint is high-leverage, correctly prioritized work. Sprint 9 shipped the Anthropic LLM adapter — the first real LLM integration. These three bugs represent the first contact with actual LLM output, and all three block the product from producing coherent gameplay. Fixing prompt format (#240), character voice fidelity (#241), and shadow taint injection (#242) are prerequisites for any meaningful playtesting. This is the right work at the right time.

## Data Flow Traces

### #240 — DialogueOptionsInstruction missing output format
- User action → `GameSession.StartTurnAsync()` → `ILlmAdapter.GetDialogueOptionsAsync(DialogueContext)` → `AnthropicLlmAdapter` → `SessionDocumentBuilder.BuildDialogueOptionsPrompt()` injects `PromptTemplates.DialogueOptionsInstruction` → Anthropic API → freeform response → `ParseDialogueOptions()` splits by `OPTION_\d+` regex → no headers found → pads with 4 `"..."` placeholders
- Required fields in prompt: OPTION_N headers, `[STAT: X]` metadata tags, quoted `"text"` on next line
- ⚠️ **Missing**: `DialogueOptionsInstruction` specifies metadata format but omits the `OPTION_1`/`OPTION_2`/`OPTION_3`/`OPTION_4` header structure and the quoted-text-on-next-line requirement that the parser depends on
- Fix is correctly scoped — template change + parser regression test

### #241 — Legendary fail delivery wrong character voice
- User action → `GameSession.ResolveTurnAsync()` → creates `DeliveryContext` (line 503) → `ILlmAdapter.DeliverMessageAsync(DeliveryContext)` → `AnthropicLlmAdapter.DeliverMessageAsync()` → `CacheBlockBuilder.BuildCachedSystemBlocks(playerPrompt, opponentPrompt)` (BOTH prompts in system) → `SessionDocumentBuilder.BuildDeliveryPrompt()` → `FailureDeliveryInstruction` template (no `{player_name}` placeholder) → Anthropic API → LLM picks wrong voice
- Required fields: `PlayerName` in delivery prompt template, player-only system prompt (or explicit voice framing)
- ⚠️ **BLOCKING**: `GameSession` does NOT pass `playerName` or `opponentName` to `DeliveryContext` (line 503–512). They default to `""`. The adapter's `FallbackName()` resolves to generic `"Player"`. Even if the template adds `{player_name}`, it will say "Player" not "Sable". Filed as #243.
- ⚠️ **Architectural concern**: `DeliverMessageAsync` uses both system prompts but `GetOpponentResponseAsync` correctly uses opponent-only. Delivery should likely use player-only system blocks. Filed as #245.

### #242 — Shadow threshold taint not injected into LLM prompts
- `GameSession.StartTurnAsync()` computes `shadowThresholds` dict → passes to `DialogueContext` ✅ → `AnthropicLlmAdapter.GetDialogueOptionsAsync()` → `SessionDocumentBuilder.BuildDialogueOptionsPrompt()` — **has no shadow parameter, ignores thresholds entirely**
- Required: shadow taint text in `BuildDialogueOptionsPrompt()`, `BuildDeliveryPrompt()`, `BuildOpponentPrompt()` when thresholds ≥ T1
- ⚠️ **BLOCKING**: `SessionDocumentBuilder` has zero shadow awareness. None of its methods accept shadow thresholds. `PromptTemplates` has no taint text constants. The fix requires: new parameters on builder methods, new template constants, GameSession wiring shadowThresholds to DeliveryContext/OpponentContext. This is more than a template fix — it's a builder API change. Filed as #244.

## Unstated Requirements
- If shadow taint blocks are added (#242), they must vary per shadow stat AND per threshold tier (T1 = subtle, T2 = pronounced, T3 = overwhelming). The issue only specifies Madness/Dread at T1 — the implementer needs guidance for all 6 shadows × 3 tiers.
- Players expect that fixing #240 (options format) will also fix downstream delivery quality. The fix chain is: #240 → #241 → meaningful gameplay. If #241 ships without the name wiring (#243), delivery will still be wrong.
- Integration test AC on #240 ("real Anthropic call") needs an API key in CI. This either needs a secret configured or the AC should be marked as manual/local-only.

## Domain Invariants
- **Delivery voice must match the player character's identity** — a Legendary fail by Sable must sound like Sable at her worst, not like Brick
- **Parser and prompt must be symmetric** — any format the prompt instructs the LLM to use, the parser must handle; any format the parser expects, the prompt must specify
- **Shadow taint is additive to character voice** — it modifies tone, it doesn't replace the character. Taint at T1 should be subtle enough that it feels like the character having a bad day, not a different character

## Gaps

### Missing (should be in this sprint)
- **#243 (filed)**: GameSession must pass `playerName`, `opponentName`, `currentTurn` to `DeliveryContext` and `OpponentContext`. Without this, #241's fix is incomplete. This partially resolves long-standing #211.
- **#244 (filed)**: #242's AC implies a template-only fix, but `SessionDocumentBuilder` needs API changes to accept and render shadow thresholds. The scope is underestimated.
- **#245 (filed)**: `DeliverMessageAsync` should use player-only system blocks (like `GetOpponentResponseAsync` uses opponent-only). This is the root architectural cause of #241.

### Unnecessary
- Nothing — all three bugs are real and blocking gameplay.

### Assumptions needing validation
- #240 AC includes "integration test with real Anthropic call" — requires API key availability in CI. Validate this is set up.
- #242 assumes §3.6 taint text content exists somewhere accessible. If it's only in `design/systems/rules-v3.md` (external repo), the implementer needs it provided or referenced.

## Recommendations
1. **Add #243 to this sprint** — it's a 5-line fix in `GameSession` (pass names/turn to DeliveryContext and OpponentContext constructors). Without it, #241 is incomplete. This is **BLOCKING** for #241.
2. **Expand #242 scope or split it** — the current AC underestimates the work. Either update the AC to include SessionDocumentBuilder API changes (#244), or create a separate issue for the builder changes and make #242 depend on it.
3. **#241 implementer should evaluate player-only system blocks** (#245) — this is the cleaner architectural fix vs. just adding a "write as {player_name}" instruction to the template.
4. **All three issues have correct role assignment** (backend-engineer). No changes needed.

## Verdict: **ADVISORY**

Three vision concerns filed (#243, #244, #245). The sprint direction is correct and high-priority, but #241 and #242 have data flow gaps that will cause incomplete fixes if not addressed. #243 is the most critical — without name wiring, the template fix for #241 resolves to generic "Player" instead of the actual character name. The sprint should incorporate these concerns before implementation starts.
