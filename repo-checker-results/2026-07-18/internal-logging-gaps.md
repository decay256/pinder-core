### Finding 1: Texting-style conflict drops are written as unstructured per-drop console noise
**File**: `pinder-core/src/Pinder.SessionSetup/CharacterDefinitionLoader.cs:206`
**Issue**: Character loading aggregates texting-style conflicts and writes each drop directly to stderr: `foreach (var drop in aggregationResult.Drops) Console.Error.WriteLine(drop.ToString());`. The log has no structured level, character/session correlation, drop count, or stable event name, and it emits once per dropped entry.
**Impact**: Conflict-resolution diagnostics can become noisy when many character items conflict, while still being hard to query or correlate to the character/session that triggered them. Hosts that use `ILogger`/structured sinks cannot reliably capture or route these events.
**Urgency**: U2 - topic default; this is a production character-loading diagnostic path with noisy-loop and correlation gaps.
**Fixer-Agent Action Plan**: Replace the per-drop `Console.Error` loop with a host-controlled structured diagnostic path. Aggregate a summary event containing `character_id`, `character_name`, `drop_count`, and conflict categories, optionally include bounded per-drop details at debug level, and add a unit test that loading a character with conflicts emits one structured diagnostic and does not write directly to `Console.Error`.

### Finding 2: Operational diagnostic sink failures are swallowed without any fallback signal
**File**: `pinder-core/src/Pinder.Core/Conversation/OperationalDiagnosticEvent.cs:163`
**Issue**: The central diagnostic emitter silently returns when `sink == null` and catches every sink exception without reporting it: `if (sink == null) { return; }` followed by `catch { /* Diagnostic callbacks must never alter gameplay/library control flow. */ }`.
**Impact**: If the host diagnostic bridge breaks, all downstream transition/external-call diagnostics from setup, delivery, turn orchestration, and LLM transport can disappear with no counter, warning, or failure hook. That makes logging outages indistinguishable from "nothing happened" during incident diagnosis.
**Urgency**: U2 - topic default; this is the shared internal logging/diagnostic dispatcher for multiple gameplay and LLM transition paths.
**Fixer-Agent Action Plan**: Keep gameplay non-fatal, but make diagnostic delivery observable: have `Emit` return a boolean or accept an optional failure callback, and have GameApi's `OperationDiagnosticAdapter`/tests assert that sink failures produce a structured `diagnostic_sink_failed` log with source/event/severity/exception type while still not throwing into gameplay.

### Finding 3: Admin operation proxy request failures omit exception type and traceback
**File**: `pinder-web/src/pinder-backend/routes/endpoints/admin_operations.py:108`
**Issue**: `_admin_upstream_request` catches `httpx.RequestError as exc` but logs only route/method/filter fields and duration: `log.error("gameapi.unreachable", method=method, route=route, admin=True, ... duration_ms=..., **safe_log_fields)`. Unlike nearby proxy helpers, it does not pass `exc_info=True` and does not include `exception_type`.
**Impact**: Admin operation failures caused by timeout, DNS, TLS, connection reset, or refused connection all produce the same log shape. The public response is intentionally sanitized, so operators lose the only practical place to diagnose which upstream failure mode occurred.
**Urgency**: U2 - topic default; this is an admin-facing external-call proxy where the response hides details and the internal log needs to carry them.
**Fixer-Agent Action Plan**: Add `exc_info=True` and `exception_type=type(exc).__name__` to this `gameapi.unreachable` record. Extend `src/pinder-backend/tests/test_operation_proxy.py` coverage to assert the captured log contains `route`, `operation_id` when present, `exception_type`, and traceback metadata for `httpx.ConnectError`/timeout cases.

### Finding 4: Media asset fetch failures return upstream status without structured external-call logs
**File**: `pinder-web/src/Pinder.GameApi/Controllers/MediaController.cs:141`
**Issue**: The media fetch path calls Eigencore with `var response = await client.GetAsync($"{baseUri}/assets/{assetId:D}", ...)`, but there is no request-start, success, duration, or upstream-status log. For non-404 non-success responses it immediately returns `StatusCode((int)response.StatusCode, new { error = "Failed to fetch asset from Eigencore." });` without logging the `assetId`, upstream status, or elapsed time.
**Impact**: Repeated 500/502/503 responses from Eigencore media retrieval leave no searchable internal trail unless the call throws. Operators can see client failures but cannot correlate them to an asset id, upstream status, or latency from GameApi logs.
**Urgency**: U2 - topic default; this is a public media endpoint backed by an external storage service.
**Fixer-Agent Action Plan**: Add structured begin/complete/failure logging around media fetch and upload with `asset_id`, upstream route/service name, status, duration, and exception type. Add controller tests with a fake `HttpClient`/handler that assert non-success GET responses emit a warning/error log and successful GET emits a completion log without logging token/header values.

Suppressed by exception: Unsafe Leakage of GameApi Raw Exception/HTML Content (resp.text) to public-facing HTTP responses - would be U1 at `pinder-web/src/pinder-backend/session_services.py:391` and `pinder-web/src/pinder-backend/session_services.py:417`.
Suppressed by exception: Exposure of Internal low-level Library Exceptions and Socket Errors to End-Users - would be U1 at `pinder-web/src/pinder-backend/main.py:376`, `pinder-web/src/pinder-backend/main.py:428`, `pinder-web/src/pinder-backend/session_services.py:201`, `pinder-web/src/pinder-backend/session_services.py:272`, and `pinder-web/src/pinder-backend/session_services.py:498`.

U1 count: 0
U2 count: 4
U3 count: 0
Mirror hash verification: both requested report mirrors must be byte-identical; final SHA-256 verified after write.
