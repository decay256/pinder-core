# Vision Review — Sprint 9 Architecture Alignment

## Alignment: ✅

The architect's output is well-aligned with the product vision. Sprint 9 transforms Pinder from a rules library into a playable game by connecting the engine to a real LLM. The architecture preserves every key invariant: `Pinder.Core` stays zero-dependency, `ILlmAdapter` remains the clean boundary, and the new `Pinder.LlmAdapters` project has a strict one-way dependency. This is the highest-leverage work possible after 8 sprints of rules implementation.

## Maturity Fit Assessment

**Prototype-appropriate**: ✅ No over-engineering detected.

- Component separation (AnthropicClient / SessionDocumentBuilder / CacheBlockBuilder / AnthropicLlmAdapter) is the minimum viable split — each has a single responsibility and clear test surface.
- Regex-based LLM output parsing with null-fallback is the right prototype approach. Structured output (tool use, JSON mode) would be premature.
- No abstract factory patterns, no plugin system, no adapter registry. Just a concrete class implementing an existing interface.
- The `[SIGNALS]` block format for Tell/WeaknessWindow is pragmatic — structured enough to parse, informal enough to iterate.

**Risk of under-engineering**: None. The 7 contracts are detailed enough for implementation.

## Data Flow Traces (Architect's Output Verified)

### GetDialogueOptionsAsync
- GameSession → DialogueContext → AnthropicLlmAdapter → CacheBlockBuilder (system blocks) → SessionDocumentBuilder (user prompt) → AnthropicClient (HTTP) → parse → DialogueOption[]
- ✅ Vision concern #211 (missing DTO fields) explicitly incorporated into contract `sprint-9-context-dto-extensions.md`
- ✅ Fields flow: `_turnNumber` → `CurrentTurn`, `_player.DisplayName` → `PlayerName`, `_opponent.DisplayName` → `OpponentName`

### GetOpponentResponseAsync → Tell/WeaknessWindow
- GameSession → OpponentContext → AnthropicLlmAdapter → prompt includes §3.5 signal instructions → Anthropic API → parse `[SIGNALS]` block → OpponentResponse(messageText, detectedTell?, weaknessWindow?)
- ✅ Vision concern #214 resolved: `[SIGNALS]` block parsed leniently, null on failure
- ✅ Matches GameSession consumption pattern (lines 582-585 read `DetectedTell` and `WeaknessWindow` from `OpponentResponse`)

### Cache Strategy
- System blocks (player + opponent prompts, ~6k tokens) → `cache_control: ephemeral` → reused turns 2+
- ✅ Vision concern #213 resolved: no beta header, GA `cache_control` in body only
- ✅ Minimum 1024 tokens for Claude Sonnet caching, character prompts exceed this

## Coupling Analysis

| Boundary | Direction | Clean? |
|----------|-----------|--------|
| LlmAdapters → Core | One-way | ✅ Core has zero knowledge of LlmAdapters |
| AnthropicClient → Newtonsoft.Json | Direct dep | ✅ Contained in LlmAdapters project only |
| Core → Newtonsoft.Json | None | ✅ Zero-dependency invariant preserved |
| GameSession → ILlmAdapter | Interface | ✅ No coupling to Anthropic specifics |

No coupling conflicts with roadmap. Adding future adapters (OpenAI, local models) requires only a new `ILlmAdapter` implementation — no Core changes.

## Abstraction Pain Points at Next Maturity Level

Reviewed for "things that will be painful to undo":

1. **`SessionDocumentBuilder` as static class**: Fine for prototype. If we need per-adapter prompt strategies later, converting to an instance with interface is straightforward — callers don't hold references.
2. **`PromptTemplates` as const strings**: Will need sourcing from `character-construction.md` design docs eventually. Current placeholder approach is fine — the contract acknowledges this.
3. **Regex parsing of LLM output**: Will break on unexpected formats. At production maturity, move to tool_use or structured JSON output. The null-fallback strategy means failures degrade gracefully, not catastrophically.
4. **`AnthropicOptions` with hardcoded model default**: Model string will need updating as Claude versions change. This is a config concern, not an architecture concern.

None of these require architectural rework. All are incremental improvements at the next maturity level.

## GameSession God Object (#87)

This sprint does NOT worsen #87. The only GameSession change is wiring 3 existing fields (`_turnNumber`, `_player.DisplayName`, `_opponent.DisplayName`) into context DTOs. All new complexity lives in `Pinder.LlmAdapters`. The architect correctly deferred extraction to the next maturity level.

## Unstated Requirements (checked)

- **Cost visibility**: `UsageStats` DTO in contracts captures `InputTokens`/`OutputTokens`/`CacheCreationTokens`/`CacheReadTokens`. Host can compute cost. Adequate for prototype.
- **API failure UX**: Retry logic (429/529/5xx) handles transient errors. Permanent failures propagate as `AnthropicApiException`. Host must handle — but that's correct separation of concerns. Not a sprint gap.
- **Tell/WeaknessWindow reliability**: LLM may not always generate signals. Null fallback means game works without them (just no tactical depth from tells/cracks). Acceptable degradation for prototype.

## Gaps

- **None blocking.**
- **Minor**: #210 is named "Integration test" but uses `NullLlmAdapter` — it's really a GameSession integration test, not an adapter integration test. This is acknowledged in the first-pass review and is fine for this sprint.
- **Future**: No end-to-end test hitting real Anthropic API. Appropriate to defer — requires API key and costs money.

## Domain Invariants (verified against contracts)

- ✅ `Pinder.Core` remains zero NuGet dependencies
- ✅ `ILlmAdapter` interface unchanged — backward compatible
- ✅ All 1118 passing tests unaffected (DTO changes use optional params with defaults)
- ✅ Dialogue option parsing never throws — fallback to defaults
- ✅ `NullLlmAdapter` stays in Core as test double, unchanged
- ✅ Cache blocks deterministic for same session (system prompts immutable mid-session)

## Implementation Order (validated)

```
Wave 1: #205 (project scaffold), #209 (test fix) — independent foundations
Wave 2: #206 (HTTP client), #207 (prompts), #210 (integration test) — parallel, depend on Wave 1
Wave 3: #208 (adapter) — depends on all above
```

Sequencing is correct. No circular dependencies. Vision concerns #211-#214 are folded into their parent issues.

## Recommendations

1. **Proceed as designed.** Architecture is clean, well-separated, and appropriate for prototype maturity.
2. **Implementers for #208 should read all 4 vision concern issues** (#211-#214) — they contain concrete ACs that supplement the main issue specs.
3. **#209 (test fix) should be merged first** to establish a clean 1119/1119 test baseline before feature work begins.

**VERDICT: CLEAN**
