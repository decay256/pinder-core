> Scope: uncommitted files changed for Core #1342/#1343 (22 files)

### Finding 1: Compiler summary still says the emotional reaction artifact is not wired into DATEE prompts
**File**: `src/Pinder.LlmAdapters/EmotionalReactionEventCompiler.cs:14`
**Issue**: The XML summary still documents the compiler as producing an artifact that "is not wired into the current visible DATEE response prompt." Current production code now requires that artifact path: `PinderLlmAdapter.GetDateeResponseAsync` rejects missing `DateeContext.EmotionalTurnEvent`, calls `GenerateEmotionalDirectionAsync`, and passes the validated direction into `SessionDocumentBuilder.BuildDateePerformancePromptEx`; the performance builder inserts `emotional-reaction-performance-direction` before `datee-response-instruction`.
**Impact**: The stale summary points maintainers at the pre-#1342/#1343 architecture and can lead future changes to treat `EmotionalReactionEventCompiler` as isolated/private-only input plumbing instead of part of the production DATEE response pipeline.
**Urgency**: U3 - topic default; this is a maintainability/documentation drift issue, not an immediate runtime defect.
**Fixer-Agent Action Plan**: Update the XML summary to say the compiler performs no LLM call, produces the private source packet consumed by `GenerateEmotionalDirectionAsync`, and is now part of the production DATEE two-call response path before performance prompt rendering.

### Finding 2: LLM adapter docs still describe PromptTemplates members as C# const prompt strings
**File**: `docs/modules/llm-adapters.md:45`
**Issue**: The API section lists prompt members as hardcoded constants, e.g. `DialogueOptionsInstruction` as a "`const string`", `DateeResponseInstruction` as a "`const string`", and several `DateeReaction*` entries as "`internal const string`". The current `PromptTemplates` implementation exposes YAML-backed properties such as `public static string DialogueOptionsInstruction => GetCatalogString("dialogue-options-instruction")` and throws when the runtime `PromptCatalog` is not wired; prompt prose is no longer embedded as C# const content.
**Impact**: The module documentation contradicts the current prompt SSOT model and can send maintainers looking for editable prompt text in C# instead of `data/prompts/*.yaml`, especially around the newly scoped emotional performance and DATEE response prompt work.
**Urgency**: U3 - topic default; this is documentation drift with low immediate blast radius.
**Fixer-Agent Action Plan**: Rewrite the `PromptTemplates` API bullets to describe YAML-backed accessors/catalog keys rather than const strings, and cross-link the current `docs/prompts.md` catalog contract for where prompt prose is edited.

### Finding 3: GameDefinition docs omit current required parser surface
**File**: `docs/modules/llm-adapters.md:190`
**Issue**: The `GameDefinition` section says "All properties are non-null strings set at construction" and documents `LoadFrom` as mapping only `name`, `game_master_prompt`, `player_avatar_role_description`, `datee_role_description`, and `global_dc_bias`. Current code exposes non-string runtime knobs and required parser blocks including `MaxTurns`, `MaxDialogueOptions`, `MaxDeliveryWords`, `ActiveTrapInterestPenalty`, `HungerForIntimacy`, `TerrorOfRejection`, `xp_flat_awards`, `xp_success_base`, progression maps, and `progression_currency_per_xp`; tests assert missing gameplay knobs and missing progression blocks throw.
**Impact**: Anyone using this module doc to edit or validate `game-definition.yaml` will under-specify the file and hit fail-fast parser errors at startup/test time, despite the docs implying the small five-key shape is sufficient.
**Urgency**: U3 - topic default; stale docs can waste setup/debug time but the parser fails closed rather than silently accepting incomplete config.
**Fixer-Agent Action Plan**: Replace the old five-key `GameDefinition` API text with the current required YAML groups, mark optional vs required numeric/string/dictionary fields, and align it with `docs/data-architecture.md` plus `GameDefinition.Parser.cs`.

### Finding 4: SessionSystemPromptBuilder docs show a removed combined Build API as the public contract
**File**: `docs/modules/llm-adapters.md:201`
**Issue**: The API section still shows `public static string Build(string playerPrompt, string dateePrompt, GameDefinition? gameDef = null)` returning a combined 5-section prompt. Current code uses role-specific builders (`BuildPlayerAvatar`, `BuildPlayerAvatarEx`, `BuildDatee`, `BuildDateeEx`) and `PinderLlmAdapter` calls those separate methods for dialogue options, DATEE response, steering, horniness, and success improvement. The same doc later states the active builder produces role-specific prompts, so the API section contradicts both code and its own architecture note.
**Impact**: The stale signature can mislead callers/tests into expecting a combined player+DATEE system prompt path that is no longer the active session contract, increasing the chance of resurrecting the historical stateful prompt shape.
**Urgency**: U3 - topic default; this is stale API documentation and the code no longer exposes the documented combined method.
**Fixer-Agent Action Plan**: Remove the combined `Build` snippet and replace it with the role-specific `BuildPlayerAvatar`/`BuildDatee` signatures, summarizing the shared GM base plus per-role character-spec block ordering pinned by the current tests.
