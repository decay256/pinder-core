# pii-in-logs

Audit scope: full multi-repo Pinder system:
- `A:\Data\ClaudeCodex\pinder-core`
- `A:\Data\ClaudeCodex\pinder-web`

Approved exceptions loaded from `A:\Data\Obsidian\Eigen\Pinder Design\Audit\Currently acceptable exceptions.md` and the worker prompt. Those patterns were suppressed and are recorded below without findings.

### Finding 1: Turn audit records persist full LLM prompts, raw responses, and player-authored turn text
**File**: `pinder-web/src/Pinder.GameApi/Services/TurnAuditRecords.cs:163`
**Issue**: `LlmExchangeRecord` stores `StructuredRequest`, `StructuredResponse`, `SystemPrompt`, `UserPrompt`, `RawResponse`, and `ParsedJson`, and `From()` copies those fields directly from each `LlmExchange` at `pinder-web/src/Pinder.GameApi/Services/TurnAuditRecords.cs:209`. `TurnAuditWriter.BuildRecord()` then writes the exchanges into `LlmExchanges` at `pinder-web/src/Pinder.GameApi/Services/TurnAuditWriter.Builder.cs:305`. The same audit record also persists player-facing option text at `pinder-web/src/Pinder.GameApi/Services/TurnAuditWriter.Builder.cs:104` and word-level `TextDiffs` containing before/after message text at `pinder-web/src/Pinder.GameApi/Services/TurnAuditWriter.Builder.cs:321`. `TurnAuditWriter.WriteAsync()` serializes the record and writes it to `turn_records` at `pinder-web/src/Pinder.GameApi/Services/TurnAuditWriter.cs:200` and to per-session NDJSON at `pinder-web/src/Pinder.GameApi/Services/TurnAuditWriter.cs:295`.
**Impact**: These audit logs can contain raw user dialogue, character profiles, psychological stakes, generated replies, provider request JSON, and model outputs. Anyone with DB/log-file access gets full conversation and prompt content instead of operational metadata, and retention/deletion controls for ordinary logs become privacy-critical.
**Urgency**: U1 - topic default; raw prompts/payloads and user-authored text are persisted in audit logs on the normal production turn path.
**Fixer-Agent Action Plan**: Replace raw prompt/response fields in the persisted audit shape with redacted summaries, hashes, token counts, phase/call identifiers, and optionally a gated encrypted blob with explicit retention. Split public replay data from private diagnostic data, and add tests proving `turn_records.payload` and NDJSON never contain `system_prompt`, `user_prompt`, `raw_response`, option free text, or text-diff before/after bodies unless an explicitly approved secure sink is enabled.

### Finding 2: Client error telemetry logs raw client-supplied messages, stacks, URLs, extras, and user_sub
**File**: `pinder-web/src/pinder-backend/routes/ops.py:332`
**Issue**: `/api/client-errors` writes a `client.error` log with `client_message`, `client_stack`, `client_component_stack`, `client_url`, `client_user_agent`, `client_session_id`, `client_request_id`, `client_extra`, and `user_sub` at `pinder-web/src/pinder-backend/routes/ops.py:332`. The frontend reporter warns callers that `extra` is logged into the backend JSON stream and must not contain PII at `pinder-web/frontend/src/lib/clientErrorReporter.ts:45`, but that discipline is not enforced by the API beyond token/email redaction.
**Impact**: React errors, rejected promises, and custom `extra` payloads can capture user text, route-specific identifiers, or browser state and ship it into central logs. The endpoint also attaches the authenticated subject, making client crash telemetry linkable to a specific user account.
**Urgency**: U1 - topic default; public client-controlled error payloads are intentionally emitted into server logs with account linkage.
**Fixer-Agent Action Plan**: Store only an allowlisted operational schema for client errors: kind, release, sanitized component names, coarse route name, hashed session/user identifiers, and bounded safe codes. Drop or hash free-text `message`, `stack`, `url`, and `extra` by default; add a server-side schema that rejects or strips unknown `extra` keys, and extend `test_client_errors.py` with PII-like user text cases.

### Finding 3: FastAPI request and ownership logs expose persistent auth subjects
**File**: `pinder-web/src/pinder-backend/middleware.py:84`
**Issue**: `RequestLoggingMiddleware` binds `user_sub` and `session_id` into structlog contextvars at `pinder-web/src/pinder-backend/middleware.py:84`, so downstream logs inherit persistent account/session identifiers. The ownership helper also logs both caller and owner subjects on access denial: `ownership.forbidden session_id=%s caller_sub=%s owner_sub=%s` at `pinder-web/src/pinder-backend/db/queries.py:50`, and records ownership with `user_sub` at `pinder-web/src/pinder-backend/db/queries.py:70`.
**Impact**: Routine request logs and authorization failures become an account activity ledger. The forbidden path is especially sensitive because it links two user subjects to the same session ID in one warning log.
**Urgency**: U1 - topic default; stable user identifiers are emitted into normal operational logs.
**Fixer-Agent Action Plan**: Replace logged `user_sub` values with a keyed hash or short-lived correlation alias, and remove `owner_sub` from warning logs. Keep raw subjects only in the database authorization checks. Add middleware and `db.queries` tests asserting rendered logs do not contain the literal JWT `sub`.

### Finding 4: GameApi progression/session logs expose UserSub in ASP.NET logs
**File**: `pinder-web/src/Pinder.GameApi/Services/ProgressionSettlementService.cs:51`
**Issue**: Progression settlement logs the account subject in normal info logs: `Settling progression for session {SessionId}, user {UserSub}` at `pinder-web/src/Pinder.GameApi/Services/ProgressionSettlementService.cs:51`, `Player {UserSub} leveled up...` at line 77, and `Progression successfully persisted for user {UserSub}` at line 102. Session listing also logs `user_sub={UserSub}` at `pinder-web/src/Pinder.GameApi/Data/GameSessionQueryHelper.cs:138`.
**Impact**: Account identifiers become searchable in ASP.NET logs across progression, listing, and settlement workflows. This duplicates PII exposure across both the Python edge service and the C# GameApi service.
**Urgency**: U1 - topic default; stable account identifiers are written to production logs.
**Fixer-Agent Action Plan**: Introduce a shared safe logging helper for account identifiers, using a keyed hash or redacted suffix, and update progression/session query log templates to use that safe form. Add logger-capture tests covering settlement and session listing with a sentinel `UserSub` and asserting the raw value is absent.

### Finding 5: Remote asset exception logging includes verbatim upstream response bodies
**File**: `pinder-core/src/Pinder.RemoteAssets/Exceptions/RemoteAssetException.cs:36`
**Issue**: `RemoteAssetException.ToString()` appends `ResponseBody` verbatim at `pinder-core/src/Pinder.RemoteAssets/Exceptions/RemoteAssetException.cs:39`. `RemoteAssetOperationLog.Failure()` passes the exception object to `ILogger.LogError()` at `pinder-core/src/Pinder.RemoteAssets/RemoteAssetOperationLog.cs:77`, so structured log providers that render exception details will include the upstream body. `EigencoreResponseHandler.HandleFailureResponseAsync()` attaches raw bodies from failed HTTP responses to these exceptions at `pinder-core/src/Pinder.RemoteAssets/EigencoreResponseHandler.cs:20`.
**Impact**: If Eigencore returns a validation/auth/server body containing emails, user IDs, tags, metadata, or internal diagnostics, that body is copied into logs through exception rendering. This bypasses the otherwise metadata-only operation log fields.
**Urgency**: U1 - topic default; raw upstream response bodies can be emitted through error telemetry.
**Fixer-Agent Action Plan**: Stop including `ResponseBody` in `ToString()`. Store only status code, error code, and a redacted/truncated body excerpt produced by a sanitizer that removes emails, tokens, cookies, and user identifiers. Add tests for `RemoteAssetOperationLog.Failure()` with a response body containing sentinel email/token values and assert they are absent from captured logs.

Suppressed by exception: Unsafe Leakage of GameApi Raw Exception/HTML Content (resp.text) to public-facing HTTP responses - would be U1. Examples observed include `pinder-web/src/pinder-backend/session_services.py:391`, `pinder-web/src/pinder-backend/session_services.py:417`, and `pinder-web/src/pinder-backend/routes/admin.py:958`; not raised for this topic per approved exception.

Suppressed by exception: Unsafe Leakage of Raw C# Exception Messages (ex.Message) in public GameApi endpoints - would be U1. Examples observed include `pinder-web/src/Pinder.GameApi/Controllers/CharactersController.cs:301`, `pinder-web/src/Pinder.GameApi/Controllers/AdminPromptsController.cs:104`, and `pinder-web/src/Pinder.GameApi/Controllers/SessionsController.Speculate.cs:63`; not raised for this topic per approved exception.

Suppressed by exception: Exposure of Internal low-level Library Exceptions and Socket Errors to End-Users - would be U1. Examples observed include `pinder-web/src/pinder-backend/main.py:376`, `pinder-web/src/pinder-backend/session_services.py:201`, and `pinder-web/src/pinder-backend/routes/characters.py:94`; not raised for this topic per approved exception.

Suppressed by exception: Direct Leakage of Internal System Errors and File Paths in Admin Controller Error Payloads - would be U1. Examples observed include `pinder-web/src/pinder-backend/routes/endpoints/admin_content_write.py:921`, `pinder-web/src/pinder-backend/routes/endpoints/admin_git_writer.py:266`, and `pinder-web/src/pinder-backend/routes/admin.py:1202`; not raised for this topic per approved exception.

Counts: U1=5, U2=0, U3=0. Suppressions=4.
