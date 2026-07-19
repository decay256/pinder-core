DRY violations audit for `pinder-core` and `pinder-web` at current checked-out commits. I inspected production C#, FastAPI, React/TypeScript API/editor code, and the larger repeated test scaffolding patterns. No approved exception suppressed a DRY finding.

### Finding 1: FastAPI GameApi proxy client/error handling is implemented twice
**File**: `pinder-web/src/pinder-backend/main.py:304`
**Issue**: `main.py` defines a standalone GameApi proxy stack with `async def forward_game_api_request(...)`, `handle_gameapi_errors(...)`, `_shared_clients: dict[float | None, httpx.AsyncClient] = {}`, and `get_shared_async_client(...)` at lines 304, 387, 436, and 452. `session_services.py` then defines a second stack with `_shared_clients` at line 31 and `class GameApiClient` containing `get_shared_client`, `handle_errors`, and `forward` at lines 121, 150, 161, and 204. The copies have already diverged: `main.py` translates status errors with `detail=_gameapi_error_detail(exc.response)` at line 364, while `session_services.py` returns `detail=f"GameApi returned {exc.response.status_code}"` at line 260.
**Impact**: Every FastAPI session proxy call depends on this path. Timeout/client-lifecycle/logging/error-body changes now need to be made in two helpers and multiple direct call sites, which makes upstream failure behavior inconsistent between routes.
**Urgency**: U2 - escalated from U3 because this is production session-proxy infrastructure and the duplicate implementations already diverge in user-visible error details.
**Fixer-Agent Action Plan**: Move the shared client cache, URL builder, request forwarding, and HTTP exception translation into one module or one `GameApiClient` implementation. Update `main.py` and `session_services.py` to call that single implementation, then run the FastAPI proxy tests that cover sessions, operations, auth, stream/audit proxying, and request-id propagation.

### Finding 2: Overlay degradation/refusal logic is copied across LLM overlay methods
**File**: `pinder-core/src/Pinder.LlmAdapters/PinderLlmAdapter.cs:423`
**Issue**: `ApplyHorninessOverlayAsync` handles empty output and refusal with the copied pattern `if (string.IsNullOrWhiteSpace(result)) { RaiseOverlayDegraded(... reason: "empty_output" ...); return message; }` at lines 423-432, followed by the refusal predicates `trimmed.StartsWith("I can't"...`, `trimmed.StartsWith("I cannot"...`, `trimmed.IndexOf("inappropriate"...`, and `trimmed.IndexOf("I'd be happy to help"...` at lines 441-445. The same empty/refusal block is duplicated for trap overlays at lines 511-541 and failure corruption at lines 598-625.
**Impact**: These are production LLM delivery-overlay hot paths. Any new refusal phrase, telemetry field, provider/model attribution, or degradation policy must be updated in three places, so the overlays can silently drift while still compiling.
**Urgency**: U2 - escalated from U3 because missed updates affect production LLM output handling on turn delivery.
**Fixer-Agent Action Plan**: Extract a shared helper, for example `NormalizeOverlayRewriteResult(...)`, that returns either the trimmed rewrite or a typed degradation reason plus overlay metadata. Rewire horniness, trap, and failure overlay methods through it, preserving trap-name metadata, then run `Pinder.LlmAdapters.Tests` and the core turn-delivery overlay regression tests.

### Finding 3: Markdown sanitization exists in two divergent C# utilities
**File**: `pinder-web/src/Pinder.GameApi/Services/MarkdownSanitizer.cs:50`
**Issue**: GameApi defines `internal static class MarkdownSanitizer` at line 50 even though the same project references `Pinder.Core` in `pinder-web/src/Pinder.GameApi/Pinder.GameApi.csproj:21`, where `pinder-core/src/Pinder.Core/Text/MarkdownSanitizer.cs:25` already exposes `public static class MarkdownSanitizer`. The copies disagree: GameApi strips numbered lists with `line = OrderedListPrefix.Replace(line, string.Empty);` at line 119, while the core sanitizer explicitly documents "Numbered list markers ... are also preserved" at line 21 and additionally strips links/images via `ImageRegex`/`LinkRegex` at lines 49 and 54.
**Impact**: LLM-output text cleanup has two rule sets. Tests can pass against the GameApi copy while production core text cleanup preserves different markdown structure, making future wiring or bug fixes brittle.
**Urgency**: U3 - topic default; the GameApi copy currently appears redundant rather than an active production call site.
**Fixer-Agent Action Plan**: Delete the GameApi-local sanitizer or make it a thin wrapper over `Pinder.Core.Text.MarkdownSanitizer`. Move any GameApi-specific tests to assert the shared sanitizer behavior and remove expectations that conflict with the core contract.

### Finding 4: Data file discovery is duplicated between GameApi and session-runner
**File**: `pinder-web/src/Pinder.GameApi/Services/DataFileLocator.cs:46`
**Issue**: GameApi implements `FindDataFile` with the same `PINDER_DATA_PATH` override and ancestor-walk search used by `pinder-core/session-runner/DataFileLocator.cs:24`. The GameApi file even says it "Mirrors the behaviour of `Pinder.SessionRunner.DataFileLocator`" at line 11, while adding a private `FlipFirstSegmentCase(...)` variant at lines 56, 67, and 91 that session-runner does not share.
**Impact**: Data lookup behavior can drift by host. For example, GameApi accepts case-flipped `Data/` vs `data/` paths while the session runner does not, so a data-layout change can pass in one entry point and fail in another.
**Urgency**: U3 - topic default; maintainability risk across two runtime entry points.
**Fixer-Agent Action Plan**: Move data-file discovery into a shared core utility, likely under `Pinder.Core.Data`, and have both GameApi and session-runner call it. Preserve the case-flex behavior if it is required by container layout, then run `DataFileLocatorTests` in both repositories.

### Finding 5: Admin prompt API calls bypass the shared admin fetch/error helper
**File**: `pinder-web/frontend/src/api/adminPrompts.ts:47`
**Issue**: `adminPrompts.ts` defines its own `_admin_error(res)` at line 47 with the same parsed `{ error, detail }` logic that `adminClient.ts` exports as `adminApiError(res)` at line 54. It then repeats manual fetch wrappers for `/api/admin/prompts`, `/api/admin/prompts/save`, and `/api/admin/prompts/compile` at lines 63, 75, and 88 instead of using `adminJsonFetch<T>` from `adminClient.ts:34`.
**Impact**: Admin API behavior is split. Request credentials, request-id extraction, error-body precedence, and JSON parsing changes can be applied to most admin calls through `adminJsonFetch` while prompt editing keeps stale behavior.
**Urgency**: U3 - topic default; admin-only frontend maintainability issue.
**Fixer-Agent Action Plan**: Replace the three manual fetch blocks with `adminJsonFetch<PromptCatalogResponse>`, `adminJsonFetch<SavePromptResponse>`, and `adminJsonFetch<CompilePromptResponse>`. Remove `_admin_error`, then update `adminPrompts.test.ts` to assert calls through the shared helper behavior.

### Finding 6: Admin content editors repeat the same draft/save/reset UI shell
**File**: `pinder-web/frontend/src/pages/DeliveryInstructionsEditor.tsx:99`
**Issue**: `DeliveryInstructionsEditor` has the repeated editor shell `const [saved, setSaved] = useState(...)`, `const [draft, setDraft] = useState(...)`, an `initialRef` reset effect, `dirty = useMemo(...)`, and `useSavePipeline(...)` at lines 99-117. The same pattern is copied in `GameDefinitionEditor.tsx:73-91`, `ItemsEditor.tsx:109-127`, `AnatomyEditor.tsx:72-88`, and `TrapConfigurator.tsx:76-88`, with repeated save-button styling such as `cursor-not-allowed bg-gray-200 text-gray-500` vs `bg-blue-600 text-white hover:bg-blue-700` in all five editors.
**Impact**: Save-state UX, stale-initial handling, dirty-state resets, and validation affordances must be fixed in several places. That increases the chance that one admin editor keeps old save/dirty behavior after another is corrected.
**Urgency**: U3 - topic default; broad frontend maintenance cost but not currently a correctness failure.
**Fixer-Agent Action Plan**: Extract a reusable admin editor state hook, for example `useAdminDraftEditor`, plus a shared save-status/header control. Migrate the five editors incrementally, keeping editor-specific field rendering local, then run the existing admin editor tests.

### Finding 7: Core tests duplicate full no-op ILlmAdapter implementations despite a shared stub
**File**: `pinder-core/tests/Pinder.Core.Tests/CallbackGameSessionTests.cs:52`
**Issue**: `CallbackGameSessionTests` embeds a local adapter with repeated no-op methods such as `GetDateeResponseAsync(...) => Task.FromResult(new DateeResponse("..."));`, `GetInterestChangeBeatAsync(...) => Task.FromResult<string?>(null);`, and the overlay pass-through methods at lines 52-63. The same ILlmAdapter boilerplate appears in many tests, including `FixationHighestPctProbabilityTests.cs:210`, `HorninessAlwaysRolledTests.cs:158`, `Issue307_ShadowTaintRawValueTests.cs:240`, `ShadowGrowthSpecTests.Helpers.cs:132`, and `TellBonusTests.cs:200`, while `pinder-core/tests/Pinder.Core.TestCommon/StubLlmAdapter.cs:13` already provides a shared `StubLlmAdapter`.
**Impact**: Any ILlmAdapter signature change or default overlay behavior change creates dozens of test edits and encourages slightly different local fakes that do not reflect shared test defaults.
**Urgency**: U3 - topic default; test maintainability risk.
**Fixer-Agent Action Plan**: Extend `StubLlmAdapter` with hooks/options needed by these tests, then replace local no-op adapters with `StubLlmAdapter` or small subclasses that override only the behavior under test. Run the core test suite after each batch.

### Finding 8: Prompt-related tests duplicate repo-subdirectory discovery
**File**: `pinder-core/tests/Pinder.Core.Tests/Issue843_PromptCatalogPhase1Tests.cs:35`
**Issue**: `Issue843_PromptCatalogPhase1Tests` defines `FindRepoSubdir(string subdir)` at line 35, walking up from `AppDomain.CurrentDomain.BaseDirectory` and throwing `Could not locate {subdir}...` at line 47. The same helper appears in `Issue868_StakePromptTests.cs:13`, `Issue872_PromptTemplatesPhase2Tests.cs:26`, `Issue873_ArchetypeCatalogPhase4Tests.cs:29`, `Issue874_PromptBuilderPhase3Tests.cs:36`, and `SessionSetup/OutfitDescriberPromptTests.cs:14`, even though `pinder-core/tests/Pinder.Core.TestCommon/TestRepoLocator.cs:7` already centralizes repository-root discovery.
**Impact**: Test fixture path resolution is scattered. A future build-layout change, repo-root marker change, or data directory rename requires updating several private helpers instead of one shared helper.
**Urgency**: U3 - topic default; test utility duplication.
**Fixer-Agent Action Plan**: Add a shared `FindRepoSubdir` or `DataSubdir` helper to `Pinder.Core.TestCommon.TestRepoLocator`, replace the copied private helpers, and run the prompt catalog/session setup tests.

### Finding 9: Text-layer noop diagnostic hashing is duplicated in two core helpers
**File**: `pinder-core/src/Pinder.Core/Conversation/LlmDispatcher.cs:374`
**Issue**: `LlmDispatcher` defines `EmitTextLayerNoop(...)` at line 374, computing `beforeHash = ComputeStableHash(beforeText)` and `afterHash = ComputeStableHash(afterText)` before swallowing diagnostic-only failures at lines 379-385. `TurnOrchestrator.Helpers.cs` repeats the same `EmitTextLayerNoop(...)` implementation at line 178 with the same hash calls and catch block at lines 183-189, and also repeats `ComputeStableHash(...)` starting at line 193.
**Impact**: Text-layer diagnostic behavior can drift between dispatcher-level overlays and orchestrator helpers. Any future change to hash length, event payload, or swallow/log policy has to be duplicated.
**Urgency**: U3 - topic default; shared diagnostic helper duplication.
**Fixer-Agent Action Plan**: Move noop emission and stable hashing into one internal shared helper in the conversation namespace. Update both call sites to use it and run text-diff/noop diagnostic tests.

Counts: U1=0, U2=2, U3=7.
