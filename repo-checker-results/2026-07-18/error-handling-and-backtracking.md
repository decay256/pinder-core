### Finding 1: Top-level React crash fallback exposes raw exception messages
**File**: `pinder-web/frontend/src/components/ErrorBoundary.tsx:41`
**Issue**: `getDerivedStateFromError` stores the thrown error text (`error.message || error.name || 'Unknown error'`), and the public fallback renders it under "Technical details" with `{this.state.errorMessage}` at line 125. `frontend/src/main.tsx:14` wraps the production `<App />` in this boundary, so any render-time exception can disclose component/library/internal assertion text to the browser user.
**Impact**: A production UI crash can reveal implementation details, internal invariant names, route/state assumptions, or third-party library messages to end users. The server-side reporter already receives `message`, `stack`, and `componentStack`, so user-facing disclosure is unnecessary.
**Urgency**: U1 - topic default for internals leaked to end users.
**Fixer-Agent Action Plan**: Change the fallback to render only a generic localized-safe failure message plus a request/crash reference if available; keep raw `message`, `stack`, and `componentStack` only in `reportClientError`. Add a React component test that throws `new Error('internal sentinel /src/private.ts')` and asserts the fallback omits the sentinel while the reporter receives it.

### Finding 2: Public media upload returns raw Eigencore failure body
**File**: `pinder-web/src/Pinder.GameApi/Controllers/MediaController.cs:94`
**Issue**: On upstream asset-store failure, the public `api/v1/media` controller reads the upstream body and returns it to the caller: `return StatusCode((int)response.StatusCode, new { error = "Eigencore storage upload failed.", details = errorBody });`. The same raw body is already logged at line 93.
**Impact**: Eigencore error bodies may include internal service diagnostics, validation internals, storage implementation details, or HTML/proxy error pages. Returning them from the GameApi media endpoint leaks backend internals to any caller who can trigger an upload failure.
**Urgency**: U1 - topic default for internals leaked to end users.
**Fixer-Agent Action Plan**: Keep `errorBody` in structured logs only, replace the response `details` with a stable public error code/message, and include a correlation/request id if available. Add `MediaController` tests for a non-2xx fake Eigencore response containing an internal sentinel string and assert the HTTP payload omits the sentinel while logging preserves it.

### Finding 3: Version endpoint silently loses GameApi failure context
**File**: `pinder-web/src/pinder-backend/main.py:216`
**Issue**: `/api/version` catches every exception while calling GameApi and then does `pass  # Degrade gracefully if GameApi is unreachable`, returning `"game_api_version": "unknown"` and `"core_version": "unknown"` with no log, exception type, URL, timeout/status, or request id.
**Impact**: Operators and agents cannot backtrack whether version drift came from GameApi being down, timing out, returning invalid JSON, or a bad `GAME_API_URL`. The user-facing downgrade is fine, but the developer trail is missing.
**Urgency**: U2 - topic default for missing developer backtrack context.
**Fixer-Agent Action Plan**: Log a warning or error with `exc_info=True`, the `/version` URL, duration, and request id before returning unknown versions. Add a FastAPI test that stubs the GameApi call to raise and asserts the endpoint still returns `"unknown"` while a structured `gameapi.version.error` log is emitted.

### Finding 4: OpenAI-compatible malformed-response wrapper drops the parser exception
**File**: `pinder-core/src/Pinder.LlmAdapters/OpenAi/OpenAiClient.cs:172`
**Issue**: The success-response parser catches arbitrary exceptions after `JObject.Parse(body)` / `ExtractAssistantText(json)`, but the wrapper created at lines 174-181 is `new InvalidOperationException(LlmDiagnosticFormatter.ProviderFailure(... body: body))` with no inner exception. Unlike the `OpenAiProviderResponseException` branch above, the original JSON/parser exception type, message, and stack are discarded.
**Impact**: Malformed provider responses still carry provider/model/status/body diagnostics, but the precise failure mode is lost, making it harder to distinguish invalid JSON, unexpected token shape, formatter bugs, and extraction regressions during incident backtracking.
**Urgency**: U2 - topic default for missing developer backtrack context.
**Fixer-Agent Action Plan**: Change the catch to `catch (Exception ex)` and construct `new InvalidOperationException(..., ex)` before telemetry/throw. Add a unit test that feeds malformed JSON and asserts the thrown wrapper has a non-null inner exception with the parser failure type while retaining provider/model/body diagnostics.

Suppressed by exception: Unsafe Leakage of GameApi Raw Exception/HTML Content (resp.text) to public-facing HTTP responses - would be U1 at `pinder-web/src/pinder-backend/session_services.py:391` and `pinder-web/src/pinder-backend/session_services.py:417` (`detail=resp.text`).

Suppressed by exception: Exposure of Internal low-level Library Exceptions and Socket Errors to End-Users - would be U1 at `pinder-web/src/pinder-backend/main.py:374` and `pinder-web/src/pinder-backend/main.py:426` (`detail=f"GameApi unreachable: {exc}"`), plus `pinder-web/src/pinder-backend/session_services.py:496` with the same pattern.

Suppressed by exception: Unsafe Leakage of Raw C# Exception Messages (ex.Message) in public GameApi endpoints - would be U1 at `pinder-web/src/Pinder.GameApi/Controllers/CharactersController.cs:301`, `pinder-web/src/Pinder.GameApi/Controllers/CharactersController.cs:691`, and `pinder-web/src/Pinder.GameApi/Controllers/AdminPromptsController.cs:104` where raw `ex.Message` is copied into HTTP error payloads.

Suppressed by exception: Direct Leakage of Internal System Errors and File Paths in Admin Controller Error Payloads - would be U1 at `pinder-web/src/pinder-backend/routes/admin.py:1170`, `pinder-web/src/pinder-backend/routes/admin.py:1202`, and `pinder-web/src/pinder-backend/routes/endpoints/admin_content_write_helpers.py:562` where admin endpoints return raw parse/write exception text.

U1 count: 2
U2 count: 2
U3 count: 0
Mirror hash verification: both destination files must be byte-identical by post-write SHA-256 comparison.
