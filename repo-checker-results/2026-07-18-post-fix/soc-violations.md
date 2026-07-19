> Scope: full pinder-core and pinder-web repositories at the finalized post-fix commits (`pinder-core` 7962415f750de354b53d4f9b953eaa3e37b3575b, `pinder-web` c7a465bb67fb86ee6b5ab1105955b7c0717eddca).

### Finding 1: Core kernel owns runtime filesystem discovery
**File**: `pinder-core/src/Pinder.Core/Data/DataFileLocator.cs:27`
**Issue**: `Pinder.Core.Data.DataFileLocator` resolves runtime data paths by reading `Environment.GetEnvironmentVariable("PINDER_DATA_PATH")`, walking parent directories with `Directory.GetParent(...)`, checking `Directory.Exists(...)`, and probing files with `File.Exists(...)`. The current architecture doc says `Pinder.Core` is the netstandard domain kernel and "must not perform I/O"; data-path discovery is a host/application concern currently used by `session-runner` and `Pinder.GameApi`.
**Impact**: The domain kernel now depends on process environment and filesystem layout, so future core consumers inherit host-specific path behavior and tests can pass because the core silently walks a local repo instead of receiving explicit data from the owning entry point.
**Urgency**: U3 - topic default; this is a clean architecture violation with maintainability risk, but current callers still appear to resolve the intended files.
**Fixer-Agent Action Plan**: Move filesystem/environment probing to `session-runner` and `Pinder.GameApi` infrastructure, or to a small non-core shared infrastructure assembly. Keep `Pinder.Core` on parsed content/value objects only, then update `session-runner/DataFileLocator.cs`, GameApi startup, and tests to call the infrastructure locator instead.

### Finding 2: Core interface layer reflects into LlmAdapters for default rules
**File**: `pinder-core/src/Pinder.Core/Interfaces/IRuleResolver.cs:121`
**Issue**: `DefaultRuleResolver.Instance` lives in `Pinder.Core.Interfaces` but searches loaded assemblies for the string type `"Pinder.LlmAdapters.GameDefinition"` and reads its static `PinderDefaults` property via reflection. This makes the core rules/progression path aware of an adapter-layer implementation while the architecture says `Pinder.Core` must not take provider/service dependencies and the prompt docs describe delegate wiring as the way to avoid cross-assembly coupling.
**Impact**: Progression helpers such as `LevelTable.GetLevel(...)` and `SessionXpRecorder` depend on a hidden global that may or may not be initialized depending on assembly load order. That blurs ownership of default gameplay constants and can make a missing adapter assembly look like a core rules failure.
**Urgency**: U2 - escalated from U3 because this hidden cross-layer dependency sits on production XP/progression paths and can fail at runtime when the adapter default is not registered.
**Fixer-Agent Action Plan**: Remove reflection from `Pinder.Core`. Require an explicit `IRuleResolver` in production session/progression construction, or register a core-owned default resolver/value table that has no `Pinder.LlmAdapters` knowledge. Add a guard test proving `Pinder.Core` no longer references adapter type names.

### Finding 3: Core delivery retry policy knows HTTP exception types and wire error text
**File**: `pinder-core/src/Pinder.Core/Conversation/DeliveryStage.cs:542`
**Issue**: `DeliveryStage.IsRetryableException(...)` treats `System.Net.Http.HttpRequestException` as retryable and then classifies raw exception messages containing `"429"`, `"503"`, `"rate limit"`, `"service unavailable"`, and `"LLM failure"`. Retryability for provider transport/protocol failures belongs in the LLM transport/adapter layer, not the core delivery stage.
**Impact**: The domain turn pipeline now carries provider/protocol knowledge, and any new transport error vocabulary must be mirrored in core string matching to preserve retry behavior. That makes retry policy harder to test and easier to drift from `LlmTransportException.FailureKind`.
**Urgency**: U3 - topic default; this is a boundary leak, but the typed `LlmTransportException` branch still gives fixers a clear replacement path.
**Fixer-Agent Action Plan**: Normalize provider failures into `LlmTransportException` before they enter `Pinder.Core`, then make `DeliveryStage` branch only on core-owned exception categories. Add adapter tests that HTTP 429/503/network failures map to `LlmFailureKind.RateLimited` or `Network`.

### Finding 4: Session state controller assembles persisted/live state semantics itself
**File**: `pinder-web/src/Pinder.GameApi/Controllers/SessionsController.Actions.cs:20`
**Issue**: `GetSessionStateAsync(...)` directly chooses between `_sessions.GetAsync(...)` live state and `_sessionRepo.LoadStateAsync(...)` persisted state, derives `stage`, forces persisted `optionsReady = true`, fabricates `resumeToken = $"resume_{id}_{snapshot.TurnNumber}_{snapshot.Interest}"`, and maps `SetupStatus` in the controller. This state projection is business/query logic, not HTTP binding.
**Impact**: Reconnect/session-state semantics can drift from `TurnStateProjectionService`, `SessionStore`, and repository behavior. A future change to setup stages, persisted terminal state, or resume-token meaning must remember this controller-specific projection path.
**Urgency**: U3 - topic default; no current wrong response was proven, but this controller owns behavior that should be centralized.
**Fixer-Agent Action Plan**: Extract a `SessionStateService` or extend `ITurnStateProjectionService` to load live-or-persisted state and produce `SessionStateResponse`. Keep the controller to parameter binding plus `Ok/NotFound`, and add tests for live setup, persisted ended, and missing-session cases through the service.

### Finding 5: Character update controller bypasses repository/store ownership for definition loading
**File**: `pinder-web/src/Pinder.GameApi/Controllers/CharactersController.cs:342`
**Issue**: `PutCharacter(...)` first calls `_characters.GetDefinitionAsync(...)`, but if that returns null it calls `_characters.GetFilePathAsync(...)`, checks `System.IO.File.Exists(...)`, reads the JSON with `File.ReadAllTextAsync(...)`, parses it with `CharacterDefinitionLoader.ParseDefinition(...)`, then builds a new `CharacterDefinition` and serializes it with `CharacterDefinitionWriter.Write(...)` in the controller. The repository already documents the store-neutral definition read path, and remote stores such as Eigencore deliberately do not have file paths.
**Impact**: Controller behavior depends on whether the underlying character store is directory-backed, and domain update semantics are split between controller code and `CharacterRepository`/`ICharacterStore`. Future remote-store fixes can miss the directory fallback path, while controller edits can accidentally change canonical character serialization.
**Urgency**: U3 - topic default; this mainly raises ownership and drift risk, because the store-neutral path is attempted first.
**Fixer-Agent Action Plan**: Move identity preservation, fallback loading, payload-to-`CharacterDefinition` merging, validation, and serialization into a repository/application service method such as `UpdateCharacterDefinitionAsync`. Delete controller filesystem access and test both directory and remote-store implementations through the service contract.

### Finding 6: FastAPI admin router reimplements GameApi proxy protocol inline
**File**: `pinder-web/src/pinder-backend/routes/admin.py:54`
**Issue**: `admin_list_sessions(...)` validates query filters, imports `main`, constructs `url = f"{main.GAME_API_URL}/sessions/debug"`, creates a fresh `httpx.AsyncClient(timeout=10.0)`, injects `main._get_request_headers()`, logs request/response timings, and maps upstream HTTP/request errors itself. The same file repeats this pattern for debug detail, speculate, prompt trace, token usage, temporary chat, narrative harness, and prompt compile, despite `gameapi_proxy.py` and `SessionProxyService` existing to own shared GameApi protocol behavior.
**Impact**: Admin proxy endpoints carry parallel timeout, header, logging, sanitization, and upstream error-mapping policy. Fixes to `GameApiClient.forward(...)` or `gameapi_error_detail(...)` will not automatically apply to these admin routes.
**Urgency**: U3 - topic default; the leak is maintainability/protocol drift rather than a demonstrated production failure.
**Fixer-Agent Action Plan**: Add an `AdminProxyService` built on `GameApiClient` for admin GameApi calls, with typed methods for session debug, speculate, prompt trace, token usage, temporary chat, narrative harness, and prompt compile. Keep route handlers to auth, request DTO binding, and response return.

### Finding 7: FastAPI admin prompt editor owns catalog parsing and YAML mutation in the route file
**File**: `pinder-web/src/pinder-backend/routes/admin.py:1051`
**Issue**: `admin_get_prompts(...)` walks `prompts_dir.glob("*.yaml")`, resolves paths, opens files, parses YAML, and maps prompt entries into response rows. `admin_save_prompt(...)` then repeats path traversal checks, opens the selected YAML with `ruamel.yaml`, navigates prompt-specific shapes, mutates fields, writes the file, and only then imports `admin_content_write` for commit/reload. This is persistence and prompt-catalog business logic inside a router, alongside the already extracted `routes/endpoints/admin_content_write.py` and `admin_content_write_helpers.py`.
**Impact**: Prompt editing has two owners: route-local YAML mutation for `/prompts/save` and helper-owned write logic for prompt PUT handlers. Schema changes to `prompts/*.yaml`, staging gates, rollback behavior, and reload/commit semantics can diverge between those surfaces.
**Urgency**: U3 - topic default; the route currently works, but future prompt schema edits are likely to patch only one of the two ownership paths.
**Fixer-Agent Action Plan**: Extract prompt catalog listing and single-prompt mutation into a shared admin prompt content service used by both `/prompts/save` and prompt PUT endpoints. Reuse the helper path resolution, staging gate, rollback, commit, and reload response helpers consistently.

### Finding 8: Admin operations UI builds backend artifact URLs directly
**File**: `pinder-web/frontend/src/pages/AdminOperationsPanel.tsx:784`
**Issue**: The `artifactLinks(...)` view helper hardcodes backend protocol paths for artifacts: `/api/sessions/{id}/audit`, `/api/admin/sessions/{id}/token-usage`, `/api/admin/sessions/{id}/prompt-trace/calls/{callId}.jsonl`, and `/api/client-errors`. API URL construction otherwise lives under `frontend/src/api/*`, including `frontend/src/api/operations.ts` with `adminOperationExportUrl(...)`.
**Impact**: The view layer now owns part of the admin operations protocol. Route changes, base-path handling, or artifact access policy updates must be patched in JSX rather than in the API client layer, increasing the chance of broken admin links.
**Urgency**: U3 - topic default; this is a localized UI protocol leak with low blast radius.
**Fixer-Agent Action Plan**: Move artifact URL builders into `frontend/src/api/operations.ts` or a dedicated admin artifacts API module, and have `AdminOperationsPanel` consume named helpers. Add a small unit test for generated audit, token usage, prompt JSONL, and client-error artifact URLs.
