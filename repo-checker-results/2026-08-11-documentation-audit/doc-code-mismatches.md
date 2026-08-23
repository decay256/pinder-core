# Documentation vs Code Mismatches

Inspected documentation/code set: audited current pinder-core documentation under `README.md`, `design/**/*.md`, active `docs/**/*.md`, and module READMEs (`src/Pinder.SessionSetup/README.md`, `tools/NarrativeHarness/README.md`, `rules/tools/README.md`) against current pinder-core production code, tests, data/config, and project files. Cross-repo checks covered pinder-web docs/code where pinder-core docs make pinder-web, GameApi, transport, or UI-display claims, especially `docs/ARCHITECTURE.md`, `docs/modules/turn-card.md`, `src/Pinder.GameApi/**`, and `frontend/src/**` gameplay display paths.

Exclusions applied: `docs/archive/**`, `docs/sprint-runs/**`, `repo-checker-results/**`, generated reports, dependency/vendor output, and historical issue implementation notes that clearly declare themselves historical. The unrelated generated pinder-web changes in `frontend/playwright-report/index.html` and `frontend/test-results/` were not modified, reverted, or used as findings.

Premise check: the premise is confirmed. Current documentation does disagree with HEAD software in several public gameplay/session/LLM/display areas below. I also found no single current end-to-end document that authoritatively maps pinder-core gameplay state by phase through GameApi transport to the exact Pinder Web display surfaces.

No U1 findings were identified.

### Finding 1: Stateful adapter/session ownership documentation describes removed APIs
**File**: `pinder-core/docs/modules/conversation-game-session.md:14`

**Issue**: The module doc still says `IStatefulLlmAdapter` extends `ILlmAdapter` with `StartConversation(string)` and `HasActiveConversation`, and its sample interface includes only those two members plus `ILlmAdapter` inheritance (`pinder-core/docs/modules/conversation-game-session.md:14-27`). It also says `GameSession` calls `stateful.StartConversation(systemPrompt)` and offers a five-parameter constructor delegating to a six-parameter overload (`pinder-core/docs/modules/conversation-game-session.md:69-70`). Current code says the opposite: `IStatefulLlmAdapter` is a stateless/contextual interface where the engine owns conversation history (`pinder-core/src/Pinder.Core/Interfaces/IStatefulLlmAdapter.cs:15-20`) and exposes contextual methods such as `GetDateeResponseAsync(DateeContext, IReadOnlyList<ConversationMessage>, CancellationToken)` (`pinder-core/src/Pinder.Core/Interfaces/IStatefulLlmAdapter.cs:30-98`). Current `GameSession` exposes the six-parameter constructor at `pinder-core/src/Pinder.Core/Conversation/GameSession.cs:158-164`, and explicitly notes that stateful datee context now lives on `GameSession` with no adapter initialization required (`pinder-core/src/Pinder.Core/Conversation/GameSession.cs:259-263`). This also contradicts the newer adapter module note that `StartConversation`/`HasActiveConversation` are historical (`pinder-core/docs/modules/llm-adapters.md:7-9`).

**Impact**: Engineers following this doc would implement or call APIs that no longer exist, and would put session history ownership in the wrong layer. That blocks an accurate gameplay-state overview because the doc assigns state to the adapter while current code assigns it to `GameSession`.

**Urgency**: U2 - escalated from topic default because this is stale public API and state-ownership documentation for the core session engine.

**Fixer-Agent Action Plan**: Rewrite `pinder-core/docs/modules/conversation-game-session.md` around the current contextual `IStatefulLlmAdapter` contract, remove `StartConversation`/`HasActiveConversation`, remove the five-parameter constructor claim, align the ownership model with `GameSession`'s `DateeContext` and conversation-history behavior, and cross-link to `docs/modules/llm-adapters.md` only after both pages use the same current terminology.

### Finding 2: GameSession docs still describe removed Read/Recover actions and optional-clock behavior
**File**: `pinder-core/docs/modules/game-session.md:33`

**Issue**: The GameSession module doc publishes a public API with both five- and six-parameter constructors (`pinder-core/docs/modules/game-session.md:33-46`), describes horniness as rolling even without `IGameClock` by using `_clock?.GetHorninessModifier() ?? 0` (`pinder-core/docs/modules/game-session.md:75-82`), and says Triple/Overthinking/Shadow behavior applies to `ReadAsync` and `RecoverAsync` (`pinder-core/docs/modules/game-session.md:97-106`). The conversation module repeats the same stale action surface, listing `ReadAsync` and `RecoverAsync` as standalone actions with `ReadResult.cs`/`RecoverResult.cs` (`pinder-core/docs/modules/conversation.md:16-17`, `pinder-core/docs/modules/conversation.md:41-43`, `pinder-core/docs/modules/conversation.md:63-65`). Current code exposes the six-parameter `GameSession` constructor (`pinder-core/src/Pinder.Core/Conversation/GameSession.cs:158-164`), requires `GameClock` at construction time and throws when it is missing (`pinder-core/src/Pinder.Core/Conversation/GameSession.cs:222-224`), and the current turn API is `StartTurnAsync(CancellationToken)`, `ResolveTurnAsync(...)`, plus `Wait()` (`pinder-core/src/Pinder.Core/Conversation/GameSession.Turns.cs:30-36`, `pinder-core/src/Pinder.Core/Conversation/GameSession.Turns.cs:182-236`, `pinder-core/src/Pinder.Core/Conversation/GameSession.Turns.cs:294`). Source searches found no current production `ReadAsync`, `RecoverAsync`, `ReadResult`, or `RecoverResult` definitions under `pinder-core/src`.

**Impact**: The docs teach a gameplay phase model that does not match the engine. That is a direct obstacle to producing a future authoritative phase-by-phase gameplay-state document, because it would include non-existent Read/Recover phases and miss the clock requirement.

**Urgency**: U2 - escalated from topic default because the mismatch changes the public action model and construction preconditions of the core gameplay session.

**Fixer-Agent Action Plan**: Update `pinder-core/docs/modules/game-session.md` and `pinder-core/docs/modules/conversation.md` to the current `StartTurnAsync`/`ResolveTurnAsync`/`Wait` model; remove `ReadAsync`, `RecoverAsync`, `ReadResult`, and `RecoverResult` references; document that `GameSessionConfig.Clock` must be provided in practice; and replace obsolete constructor examples with the current six-parameter construction flow.

### Finding 3: Unity integration guide contains non-compiling adapter and transport examples
**File**: `pinder-core/docs/unity-integration.md:287`

**Issue**: The Unity guide says its inline `ILlmAdapter` surface is the "full surface" but lists six methods and omits `ApplyFailureCorruptionAsync` (`pinder-core/docs/unity-integration.md:287-303`). It then tells Unity implementers to implement that same incomplete `ILlmAdapter` directly (`pinder-core/docs/unity-integration.md:324-329`). Current `ILlmAdapter` requires seven methods, including `ApplyFailureCorruptionAsync` (`pinder-core/src/Pinder.Core/Interfaces/ILlmAdapter.cs:21-94`). The same guide says `OpenAiTransport` takes an `HttpClient`, API key, and model (`pinder-core/docs/unity-integration.md:314-316`) and shows `new OpenAiTransport(_http, apiKey: "sk-...", model: "gpt-4o")` (`pinder-core/docs/unity-integration.md:450`), but current constructors require `apiKey`, `baseUrl`, `model`, and only the overload at `pinder-core/src/Pinder.LlmAdapters/OpenAi/OpenAiTransport.cs:47-54` accepts an externally supplied `HttpClient` after those first three string parameters. Finally, the guide says a zero-argument `new GameSessionConfig()` is acceptable for bring-up (`pinder-core/docs/unity-integration.md:464-468`), while current `GameSession` throws if `GameClock` is not provided (`pinder-core/src/Pinder.Core/Conversation/GameSession.cs:222-224`).

**Impact**: A Unity consumer following the current integration doc will fail to compile the adapter or transport sample, then fail at runtime if they use the suggested default config. This is a public integration break in documentation rather than an internal wording issue.

**Urgency**: U2 - escalated from topic default because this doc is an implementation guide for external/Unity integration and its examples no longer match public constructors/interfaces.

**Fixer-Agent Action Plan**: Patch `pinder-core/docs/unity-integration.md` to include the current seven-method `ILlmAdapter` surface, replace `OpenAiTransport` snippets with valid constructor signatures including `baseUrl`, add a valid `GameSessionConfig` example with a clock, and preferably point Unity authors at a maintained minimal adapter sample or test fixture rather than duplicating the full interface inline.

### Finding 4: Authoritative prompt map omits active prompt files loaded at runtime
**File**: `pinder-core/docs/prompts.md:3`

**Issue**: The prompt doc declares itself an "authoritative map of every LLM prompt template pinder-core/pinder-web emit" (`pinder-core/docs/prompts.md:3`) and says the YAML catalog is the current single source of truth (`pinder-core/docs/prompts.md:5`). Its "Current SSOT" file layout lists only seven YAML files (`pinder-core/docs/prompts.md:58-69`). Current runtime loading scans every `*.yaml` file in the prompts directory (`pinder-core/src/Pinder.LlmAdapters/PromptCatalog.cs:351-370`), and active files omitted by the doc include `backstory.yaml` (`pinder-core/data/prompts/backstory.yaml:1-3`) and `outfit.yaml` (`pinder-core/data/prompts/outfit.yaml:1-8`), along with other present prompt catalog files under `pinder-core/data/prompts`.

**Impact**: A document that claims to enumerate every emitted prompt family is incomplete. That would lead a prompt audit or gameplay-state-to-LLM-call overview to miss active generation paths and underestimate the runtime prompt catalog.

**Urgency**: U2 - escalated from topic default because the doc explicitly claims authority/completeness over LLM prompt emission.

**Fixer-Agent Action Plan**: Regenerate the file layout in `pinder-core/docs/prompts.md` from the current `data/prompts/*.yaml` catalog, add sections or a table row for each active prompt family, and clarify whether pinder-web emits any prompts itself or only triggers pinder-core/GameApi paths. If the doc should not be exhaustive, remove the authoritative wording.

### Finding 5: Overlay transport follow-up claim is stale against pinder-web wiring
**File**: `pinder-core/docs/prompts.md:157`

**Issue**: The prompt doc says overlay calls can use an optional transport but that "GameApi wiring for this is tracked in a separate follow-up ticket" (`pinder-core/docs/prompts.md:155-157`). Current pinder-web documentation says `LlmProviderFactory` supports `OVERLAY_MODEL` and that overlay transport is wired during `PinderLlmAdapter` construction across session creation, rehydration, and speculation (`pinder-web/docs/ARCHITECTURE.md:899-904`). Current code reads `OVERLAY_MODEL` (`pinder-web/src/Pinder.GameApi/Program.cs:38`), builds overlay transports (`pinder-web/src/Pinder.GameApi/Services/LlmProviderFactory.cs:150-169`), and passes them into `PinderLlmAdapter` from the real construction sites (`pinder-web/src/Pinder.GameApi/Services/SessionStore.cs:312-317`, `pinder-web/src/Pinder.GameApi/Services/SessionStore.Persistence.cs:139-143`, `pinder-web/src/Pinder.GameApi/Services/SessionSimulationService.cs:264-267`).

**Impact**: The stale follow-up status misleads cross-repo operators about whether overlay routing is live. It can also cause a future prompt/transport overview to treat implemented behavior as pending work.

**Urgency**: U3 - topic default.

**Fixer-Agent Action Plan**: Update `pinder-core/docs/prompts.md` to say GameApi overlay wiring is implemented, summarize the current `OVERLAY_MODEL` fallback behavior, and link to the pinder-web architecture section or current GameApi config field instead of referencing an open follow-up.

### Finding 6: README dependency table omits current Pinder.LlmAdapters dependencies
**File**: `pinder-core/README.md:17`

**Issue**: The README project table lists `Pinder.LlmAdapters` dependencies as only `Core, Rules, Newtonsoft.Json` (`pinder-core/README.md:13-18`). The current project file references `Pinder.Core`, `Pinder.Rules`, `Newtonsoft.Json`, `System.Text.Json`, `YamlDotNet`, `Microsoft.Bcl.AsyncInterfaces`, `Pi.AI`, and `Pi.Agent.Core` (`pinder-core/src/Pinder.LlmAdapters/Pinder.LlmAdapters.csproj:23-32`). The nearby README note says Pi C# adoption is isolated to `Pinder.LlmAdapters` (`pinder-core/README.md:7-9`), but the table omits those Pi package dependencies.

**Impact**: The top-level dependency map under-reports the adapter project's runtime/build surface, which can mislead maintainers reviewing package risk, dependency ownership, or Unity-compatible packaging.

**Urgency**: U3 - topic default.

**Fixer-Agent Action Plan**: Refresh the README dependency table from the current `.csproj` files, especially `Pinder.LlmAdapters`, and decide whether the table should list all package references or intentionally summarize only major internal dependencies. If summarized, label it as a summary and call out Pi/YAML/JSON dependencies in prose.

### Finding 7: Defending-stat spec says the field is displayed, but current turn roll display path drops it
**File**: `pinder-core/docs/specs/issue-906-defending-stat.md:100`

**Issue**: The current pinder-core spec says pinder-web carries `defending_stat` in the SSE `roll_result` event and "uses it to display which defending stat was in play for the roll" (`pinder-core/docs/specs/issue-906-defending-stat.md:100-102`). The wire side is implemented: `RollResultDto.From()` includes required `DefendingStat` and maps it from `r.DefendingStat` (`pinder-web/src/Pinder.GameApi/Models/TurnResultDtos.cs:243-270`). The current turn event display path, however, adapts `result.roll` through `adaptOptionRoll(...)` and builds the visible option-roll summary from `used`, `final_total`, and `dc` (`pinder-web/frontend/src/components/TurnEventNode.tsx:221-233`); `adaptOptionRoll` maps die, modifiers, total, DC, verdict, and tier but not `defending_stat` (`pinder-web/frontend/src/components/eventbox/rollAdapters.ts:137-173`). This is therefore not a transport mismatch, but the documented display claim is not true for the current option-roll display path.

**Impact**: A future end-to-end gameplay-state-to-Pinder-Web overview would likely mark `defending_stat` as displayed in the roll card even though the current roll formula/event adapter does not surface it there. That hides an important state-to-UI projection gap.

**Urgency**: U3 - topic default.

**Fixer-Agent Action Plan**: Either update pinder-web to display `defending_stat` in the option-roll/EventBox path and document where it appears, or revise `pinder-core/docs/specs/issue-906-defending-stat.md` to say the field is transported but not currently rendered in that path. Include the exact component/field mapping in the future gameplay-state display overview.

U3 overflow: none.

## Coverage / files inspected

- pinder-core documentation: `README.md`, `design/game-definition.md`, active `docs/**/*.md`, and module READMEs under `src/Pinder.SessionSetup`, `tools/NarrativeHarness`, and `rules/tools`, with archive/sprint/generated/historical exclusions applied.
- pinder-core code/data/config: `src/Pinder.Core/**`, `src/Pinder.LlmAdapters/**`, `data/prompts/*.yaml`, project files, and targeted searches across `tests/**` and `session-runner/**` for documented public APIs and referenced behavior.
- pinder-web cross-repo verification: `docs/ARCHITECTURE.md`, `docs/modules/turn-card.md`, `src/Pinder.GameApi/**`, and frontend gameplay display code under `frontend/src/components/**`, `frontend/src/types/**`, and related eventbox adapters.
- Explicitly ignored: pinder-web generated `frontend/playwright-report/index.html` and `frontend/test-results/`.

## Prerequisites for the future gameplay-state-to-Pinder-Web overview

- Define the canonical phase list from setup through game over using current code names, not stale Read/Recover terminology: setup/session creation, `StartTurnAsync`, option selection, streamed progress stages, `ResolveTurnAsync`, `Wait`, settlement/progression, replay/rehydration, and game-over.
- For each phase, map pinder-core state objects and fields to GameApi DTO/SSE payloads, then to exact pinder-web TypeScript types and components. The pinder-web architecture doc already notes that `TurnResultDisplay` consumes mostly `TurnResult` plus frontend-threaded `intendedText`, `callbackTurn`, and `playerName` (`pinder-web/docs/ARCHITECTURE.md:755-760`); that split needs to be canonicalized.
- Inventory every current prompt/LLM call family from the YAML catalog loaded by `PromptCatalog`, including overlay transport routing and fallback behavior, before tying LLM calls to gameplay phases.
- Decide and document where transported-but-not-clearly-rendered fields such as `defending_stat` are expected to appear in Pinder Web.
- Cover live play, owner replay/public replay, session rehydration, and speculation paths separately where they construct adapters or provide display data differently.
