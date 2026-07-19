### Finding 1: Character generation bypasses the configured max-token ceiling
**File**: `pinder-web/src/Pinder.GameApi/Services/CharacterGenerator.cs:88`
**Issue**: The random character generator hardcodes a token floor in two places: `MaxTokens = Math.Max(2048, _config.LlmMaxTokens)` and later `maxTokens: Math.Max(2048, _config.LlmMaxTokens)`. This creates a second effective token-limit policy outside `GameApiConfig.LlmMaxTokens`, `LLM_MAX_TOKENS`, and the prompt/catalog defaults used by the session-setup generators.
**Impact**: Operators cannot lower `LLM_MAX_TOKENS` below 2048 for this production LLM path, so a cost/latency cap that appears global is silently ignored during character generation.
**Urgency**: U3 - topic default; this is configuration drift and cost-control surprise, not immediate data loss or outage.
**Fixer-Agent Action Plan**: Add an explicit `CharacterGenerationMaxTokens`/env setting or move this floor into the canonical prompt/catalog config, then make `CharacterGenerator` read that named setting once. Add tests proving a low global `LlmMaxTokens` either stays honored or is overridden only by the new documented character-generation setting.

### Finding 2: Speculative resimulation copies core phase-temperature defaults
**File**: `pinder-web/src/Pinder.GameApi/Services/SessionSimulationService.cs:200`
**Issue**: Speculative resimulation pins per-phase temperatures directly: `DeliveryTemperature = 0.7`, `DialogueOptionsTemperature = 0.9`, and `DateeResponseTemperature = 0.85`. The canonical core values already exist in `pinder-core/src/Pinder.LlmAdapters/LlmPhaseTemperatures.cs:14` (`DialogueOptions = 0.9`), `:17` (`OverlayRewrite = 0.7`), and `:20` (`DateeResponse = 0.85`).
**Impact**: A future retune of live adapter defaults can leave resimulation using old sampling behavior, making admin speculation/replay disagree with real session behavior in a way that is hard to spot from configuration.
**Urgency**: U3 - topic default; this is a maintainability and behavioral-drift risk, not currently divergent values.
**Fixer-Agent Action Plan**: Replace the literals with `LlmPhaseTemperatures.OverlayRewrite`, `DialogueOptions`, and `DateeResponse` (or omit overrides and rely on adapter defaults). Add a regression test that updates through the core constants rather than duplicated numeric literals.

### Finding 3: Frontend model enrichment catalog contains unsupported direct-provider slugs
**File**: `pinder-web/frontend/src/data/modelCatalog.ts:302`
**Issue**: `MODEL_CATALOG` is documented as keyed by IDs that appear in backend `SUPPORTED_MODELS`, but it also includes direct-provider entries such as `anthropic/claude-opus-4.6`, `anthropic/claude-opus-4.8`, `google/gemini-3.5-flash`, `anthropic/claude-3-haiku`, `openai/gpt-4o`, and `openai/gpt-4o-mini`. The backend allowlist in `pinder-web/src/Pinder.GameApi/appsettings.json:3` through `:29` contains only the 27 `openrouter/...` production models.
**Impact**: The frontend can richly label and price model IDs that the backend rejects, and tests at `pinder-web/frontend/src/data/modelCatalog.test.ts:49` explicitly preserve at least one unsupported direct Anthropic slug. That keeps stale model IDs alive outside the canonical supported-model catalog.
**Urgency**: U3 - topic default; current dropdowns consume `/models`, but the catalog itself has drift-prone unsupported entries.
**Fixer-Agent Action Plan**: Split unsupported legacy/direct-provider labels into a separate test-only or fallback catalog, or add an exact-set test that `MODEL_CATALOG` production keys match `scripts/regen-llm-pricing/supported-models.txt` unless a key is annotated as non-production. Update the direct-provider tests to assert fallback behavior without preserving unsupported production metadata.

### Finding 4: Core and GameApi carry different default model slugs
**File**: `pinder-core/src/Pinder.LlmAdapters/Anthropic/AnthropicModelIds.cs:14`
**Issue**: Core declares `public const string DefaultModel = "claude-sonnet-4-20250514"`, while the GameApi deploy template sets `DEFAULT_MODEL=openrouter/anthropic/claude-opus-4.7` at `pinder-web/.env.template:103`. Both are described as defaults for omitted model selection, but they live in separate places and point at different providers/models.
**Impact**: Any core consumer or tool that constructs `AnthropicTransport` without an explicit model gets direct Anthropic Sonnet 4, while GameApi sessions default to OpenRouter Opus 4.7. That makes "default model" behavior depend on construction path rather than a shared product-level configuration decision.
**Urgency**: U3 - topic default; GameApi currently requires `DEFAULT_MODEL`, so the mismatch is drift-prone rather than an immediate production failure.
**Fixer-Agent Action Plan**: Remove the transport-level product default or rename it to an Anthropic SDK fallback, and require hosts/tools to pass an explicit configured model. Add tests/documentation that the GameApi default is the only session default and that direct transports do not imply product defaults.

### Finding 5: GameApi startup duplicates LLM default token and temperature constants
**File**: `pinder-web/src/Pinder.GameApi/Program.cs:170`
**Issue**: Startup defines `const int DefaultLlmMaxTokens = 1024` and `const double DefaultLlmTemperature = 0.9`, while `GameApiConfig` also defaults `LlmMaxTokens` to `1024` at `pinder-web/src/Pinder.GameApi/Config/GameApiConfig.cs:58` and `LlmTemperature` to `0.9` at `:96`; core adapter defaults also carry `MaxTokens = 1024` at `pinder-core/src/Pinder.LlmAdapters/PinderLlmAdapterOptions.cs:33` and `LlmPhaseTemperatures.Default = 0.9` at `pinder-core/src/Pinder.LlmAdapters/LlmPhaseTemperatures.cs:11`.
**Impact**: The same global LLM defaults must be changed in multiple places. A future retune can pass some paths and tests while leaving startup fallback, config record defaults, or adapter defaults on old values.
**Urgency**: U3 - topic default; values currently match, but the pattern creates maintainability drift.
**Fixer-Agent Action Plan**: Choose one canonical owner for global gameplay adapter defaults, preferably `GameApiConfig` initialized from `PinderLlmAdapterOptions`/`LlmPhaseTemperatures`, and make startup use those values instead of local constants. Add a regression test that the startup fallback values equal the canonical defaults without hardcoding the numbers again.

Suppressed by exception: none.

Counts: U1=0, U2=0, U3=5, suppressed would-be U1=0.

Output hash confirmation: report-body-sha256=b5e49459436cdadc1ec0071cc77960cf6e77318daf7806f0227ae8ad7d19e3af; both requested files must match byte-for-byte.
