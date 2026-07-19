Inspected production entry points across `pinder-core` and `pinder-web`: C# project references, `session-runner` setup, GameApi `Program.cs` DI and controller routes, core/session setup data loaders, and React `main.tsx`/`App.tsx` imports plus production frontend modules. Tests were used only to distinguish production wiring from test-only reachability. No findings were suppressed by the approved exceptions.

### Finding 1: Anthropic improvement pass is never called
**File**: `pinder-core/src/Pinder.LlmAdapters/Anthropic/AnthropicResponseImprover.cs:11`
**Issue**: `AnthropicResponseImprover` defines `internal static class AnthropicResponseImprover` and `ApplyImprovementAsync(...)`, which reads `options.GameDefinition?.ImprovementPrompt` and attaches `ToolSchemas.Improvement`, but exact-symbol search found no production or test caller of `AnthropicResponseImprover`, `ApplyImprovementAsync`, or `StripImprovementEvaluation` outside this file. The configured data still contains `improvement_prompt` at `pinder-core/data/game-definition.yaml:576`, while `AnthropicTransport.SendAsync` returns `response.GetText() ?? ""` at `pinder-core/src/Pinder.LlmAdapters/Anthropic/AnthropicTransport.cs:78` without invoking the improvement pass.
**Impact**: The documented Anthropic self-critique/rewrite feature is dead in the production Anthropic transport. Hosts can configure `GameDefinition.ImprovementPrompt`, but generated text is returned before that logic can run.
**Urgency**: U2 - escalated from U3 because this is configured runtime behavior on the LLM output path, not inert scaffolding.
**Fixer-Agent Action Plan**: Decide whether the improvement pass is still intended. If yes, thread `AnthropicOptions`/`GameDefinition` into the Anthropic transport call path and invoke `AnthropicResponseImprover.ApplyImprovementAsync` after the draft response, then add an Anthropic transport test proving a configured `improvement_prompt` makes the second tool-backed call. If no, remove `AnthropicResponseImprover`, `ToolSchemas.Improvement` references, and the stale `improvement_prompt` documentation/data contract.

### Finding 2: Timing profile JSON loader is test-only while production ignores `data/timing/response-profiles.json`
**File**: `pinder-core/src/Pinder.Core/Data/JsonTimingRepository.cs:10`
**Issue**: `JsonTimingRepository` is a production loader whose constructor comment says it consumes `response-profiles.json`, but exact-symbol search found only `JsonTimingRepositoryTests` constructing it. The production character path loads `JsonItemRepository` and `JsonAnatomyRepository` in `session-runner/Program.CharacterLoader.cs:102`, then `CharacterAssembler` builds timing from item/anatomy modifiers only: `// Base: delay=0, variance=1.0f, drySpell=0f, readReceipt="neutral"` at `pinder-core/src/Pinder.Core/Characters/CharacterAssembler.cs:135` and `var timingProfile = new TimingProfile(...)` at line 159. No production path reads `data/timing/response-profiles.json`.
**Impact**: The checked-in timing profiles (`eager-texter`, `playing-it-cool`, `average-responder`) are never selectable at runtime; all character response timing starts from zero delay plus item/anatomy deltas. This makes the data file and loader misleading, and likely disables intended datee response-delay personality curves.
**Urgency**: U2 - escalated from U3 because a shipped gameplay data file and loader are disconnected from the character assembly path.
**Fixer-Agent Action Plan**: Add an explicit timing-profile field to character definitions or derive it from existing character metadata, load `JsonTimingRepository` beside the item/anatomy repositories, and pass the selected base `TimingProfile` into `CharacterAssembler` before applying item/anatomy modifiers. Add integration tests that load real `data/timing/response-profiles.json` and prove assembled characters receive the selected base delay/variance/read-receipt values.

### Finding 3: `BiasedDiceRoller` is obsolete after DC bias moved into `GameSessionConfig`
**File**: `pinder-core/src/Pinder.Core/Rolls/BiasedDiceRoller.cs:15`
**Issue**: `public sealed class BiasedDiceRoller : IDiceRoller` wraps another roller and subtracts `_dcBias` from d20 rolls, but exact-symbol search found no production or test construction of `BiasedDiceRoller`. The live session-runner difficulty flow now computes `totalDcBias` and passes it as `globalDcBias` into `new GameSessionConfig(...)`, then creates the session with `new SystemRandomDiceRoller(diceSeed)` in `pinder-core/session-runner/Program.Setup.cs:296-305`.
**Impact**: There are two competing implementations of "difficulty bias": the wired config-level DC adjustment and an unused dice wrapper that preserves nat 1/nat 20 differently by mutating roll values. Keeping both invites future fixes to update the wrong mechanism.
**Urgency**: U3 - topic default; this is maintainability risk rather than an active production bug because the config path is wired.
**Fixer-Agent Action Plan**: Remove `BiasedDiceRoller` if config-level bias is the intended implementation, or wire it deliberately through `GameSessionConfig.DiceRoller` with tests comparing nat 1/nat 20 and regular roll behavior. Update architecture docs that currently mention `BiasedDiceRoller` as an implementation.

### Finding 4: `IFailurePool` has no implementation or consumer
**File**: `pinder-core/src/Pinder.Core/Interfaces/IRollDataProvider.cs:13`
**Issue**: `public interface IFailurePool` exposes `GetFailureEntry(...)` and `GetSuccessEntry(...)`, but exact-symbol search across production `pinder-core` and `pinder-web` found no implementation and no consumer. The same file's `ITrapRegistry` and `IDiceRoller` are wired heavily, so this interface is the outlier in the shared roll data provider file.
**Impact**: Failure/success prompt pools appear to be a public core contract, but production uses other systems for roll consequences and delivery text. New integrations may implement or depend on `IFailurePool` expecting it to affect gameplay, only to find it is never queried.
**Urgency**: U3 - topic default; public API clutter and integration confusion, with no current runtime caller.
**Fixer-Agent Action Plan**: Confirm whether failure/success prompt pools have been superseded by `ConsequenceCatalog`/delivery instructions. If superseded, delete `IFailurePool` and update docs. If still intended, add a production implementation and inject it into the roll/delivery path with tests proving both failure and success entries are consumed.

### Finding 5: Removed setup-status response model remains in production
**File**: `pinder-web/src/Pinder.GameApi/Models/SetupStatusResponse.cs:3`
**Issue**: `public sealed class SetupStatusResponse` defines the old setup polling response (`OptionsReady`, `CurrentStage`, `PlayerStake`, `SetupRecoverable`, etc.), but exact-symbol search found no GameApi controller returning it. The repo also has `RemovedSetupStatusEndpointsRegistrationTests` asserting `GET sessions/{id}/setup-status` and `/stream` are not registered, while live state now returns `SessionStateResponse` from `SessionsController.Actions.cs`.
**Impact**: The model preserves a removed API surface in production code, which can mislead future endpoint work or contract regeneration into reintroducing setup polling.
**Urgency**: U3 - topic default; the removed endpoints are pinned by tests, so this is cleanup/contract clarity.
**Fixer-Agent Action Plan**: Delete `SetupStatusResponse.cs` if setup polling remains removed, then run GameApi tests including `RemovedSetupStatusEndpointsRegistrationTests`. If the endpoint should return, wire it explicitly and update the removal tests and OpenAPI contract together.

### Finding 6: Legacy token usage DTO is replaced by the wired token usage response
**File**: `pinder-web/src/Pinder.GameApi/Models/SessionTokenUsageDto.cs:5`
**Issue**: `public sealed class SessionTokenUsageDto` defines `session_id`, input/output/cache token fields, `call_count`, and `total_billed_input`, but exact-symbol search found no production caller. The actual `/api/admin/sessions/{id}/token-usage` controller constructs `TokenUsageResponseDto` and `TurnUsageDto` from `pinder-web/src/Pinder.GameApi/Models/TokenUsageResponseDto.cs:7` and line 43.
**Impact**: Two backend DTO shapes now describe token usage, but only one is wired. The dead one has different fields (`total_billed_input`, no cost/per-turn/source), so it can confuse admin API clients or future contract work.
**Urgency**: U3 - topic default; dead response model after a replacement.
**Fixer-Agent Action Plan**: Remove `SessionTokenUsageDto` and verify `Issue1082_TokenUsageRegressionTests` and frontend admin token usage still compile. If a compact aggregate DTO is intended, add a real endpoint or projection that returns it and document the separate shape.

### Finding 7: `MECHANIC_ROWS` duplicates stat wiring but is not consumed
**File**: `pinder-web/frontend/src/lib/statMechanics.ts:9`
**Issue**: `export const MECHANIC_ROWS: MechanicRow[] = [...]` contains the six stat/shadow/defender rows, but production search found no import or reference to `MECHANIC_ROWS`. The same module's `ATTACKER_TO_DEFENDER_MAP` is the value actually imported by `frontend/src/components/StatDisplayTable.tsx:35`.
**Impact**: The frontend has two places to encode the same combat/stat relationship, but only the map is wired. Future changes to stat mechanics may update `MECHANIC_ROWS` and leave the visible table unchanged.
**Urgency**: U3 - topic default; duplicate unwired frontend model data.
**Fixer-Agent Action Plan**: Either delete `MECHANIC_ROWS` and its `MechanicRow` interface, or make `ATTACKER_TO_DEFENDER_MAP` derive from `MECHANIC_ROWS` and add a frontend test proving `StatDisplayTable` renders the derived defender stat mapping.

### Finding 8: Operation player title localization helper is never rendered
**File**: `pinder-web/frontend/src/lib/operationText.ts:181`
**Issue**: `export function operationPlayerTitle(snapshot: OperationSnapshot | null | undefined): string` maps `snapshot.player.title_code`, `snapshot.player.title`, and `operation.title.*` keys, but production imports only `operationPlayerSummary`, `operationRetryLabel`, and `operationStatusFetchErrorMessage`. No production component calls `operationPlayerTitle`.
**Impact**: Backend-provided operation title codes can be localized by this helper, but the player UI never renders them. Either the title feature was intended and is unwired, or this helper is dead alongside the `GAME_TITLE_KEYS` table.
**Urgency**: U3 - topic default; user-facing copy helper is unused but the summary/retry path still works.
**Fixer-Agent Action Plan**: If operation titles should appear, wire `operationPlayerTitle(operationSnapshot)` into `GameScreen`/operation status UI and add a component test for `player.title_code`. If summaries are the only intended surface, delete `operationPlayerTitle` and the unused `GAME_TITLE_KEYS` map.

### Finding 9: Admin prompt source-file map is exported but unused
**File**: `pinder-web/frontend/src/pages/AdminPage.promptsEditor.sandbox.tsx:8`
**Issue**: `export const TAB_TO_SOURCE_FILE: Record<string, string> = { ... }` maps prompt editor tabs to YAML source files, but exact search found no production import or local use. `AdminPage.promptsEditor.tsx` imports `PromptEditModal` and `SpeculationPanel` from this module, not `TAB_TO_SOURCE_FILE`.
**Impact**: The admin prompt editor appears to have planned source-file diagnostics for prompt tabs, but that mapping never reaches the UI or API calls. Future prompt editor work may assume source-file routing exists when it does not.
**Urgency**: U3 - topic default; unused admin UI wiring data.
**Fixer-Agent Action Plan**: Either surface the mapped source filename in the prompt editor where tab metadata is displayed/used, with a test for one mapped tab, or delete `TAB_TO_SOURCE_FILE` if the editor no longer needs file-level routing.

Counts: U1=0, U2=2, U3=7.
