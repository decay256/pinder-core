> Scope: pinder-core 7962415f750de354b53d4f9b953eaa3e37b3575b..5dd4274b82e0e2d1c78471909341323eafb84856 (91 existing changed files); pinder-web c7a465bb67fb86ee6b5ab1105955b7c0717eddca..3d5d279c520b596560368b0d2afaaa650e270d68 (87 existing changed files). Topic 6 anti-patterns only; excludes sync-blocking, external-call robustness, and resource lifecycle.

Confirmed fixed U1s: `pinder-web/src/pinder-backend/main.py:127` now logs unexpected cleanup task failures with `exc_info=True`; `pinder-core/src/Pinder.LlmAdapters/OpenAi/OpenAiClient.cs:172` now wraps malformed-response failures with the caught exception as the inner exception.

Approved exceptions reviewed: the four approved raw-error leakage patterns in `A:\Data\Obsidian\Eigen\Pinder Design\Audit\Currently acceptable exceptions.md` do not suppress any topic-6 finding below.

### Finding 1: Test database reset failures are silently ignored
**File**: `pinder-web/src/pinder-backend/tests/conftest.py:49`
**Issue**: The autouse fixture imports and calls `reset_for_tests()`, then catches every `Exception` and `pass`es: `except Exception: pass`.
**Impact**: A broken test reset hook can leave cached database engine state alive across tests, causing order-dependent failures or false passes without any signal pointing at the failed reset.
**Urgency**: U2 - de-escalated from U1 because this is test-only code, but the swallowed exception can still hide correctness failures in the changed gate.
**Fixer-Agent Action Plan**: Catch only the expected optional-import failure, or log/raise failures from `reset_for_tests()`; if optional DB support is intentional, separate `ImportError` from runtime reset exceptions.

### Finding 2: Admin proxy hides malformed upstream error payloads
**File**: `pinder-web/src/pinder-backend/admin_proxy_service.py:195`
**Issue**: `run_temporary_chat_async` catches every exception from `resp.json().get(...)` and substitutes `"Temporary chat request failed."`; the same pattern appears in `_request` at line 281 for bad-request JSON.
**Impact**: If GameApi starts returning malformed or unexpected error bodies, the proxy silently erases that contract break. Admin users see a generic failure and logs lose the parse failure that would identify the upstream response-shape regression.
**Urgency**: U2 - de-escalated from U1 because the primary operation has already failed and this is error-message shaping, but the broad swallowed exception still hides a backend contract problem.
**Fixer-Agent Action Plan**: Catch the narrow JSON/shape exceptions expected from `resp.json()`, log the parse failure with status and request id, and then fall back to the generic message.

### Finding 3: Prompt surrogate cleanup returns unsafe input on any exception
**File**: `pinder-web/src/pinder-backend/admin_prompt_content_service.py:251`
**Issue**: `_clean_surrogates` catches every exception from `obj.encode("utf-16", "surrogatepass").decode("utf-16")` and returns the original string unchanged.
**Impact**: The helper is meant to normalize strings before admin prompt content is returned or written. If that conversion fails, the bad string silently continues through the pipeline, so later JSON/YAML serialization errors surface far from the source and without the failed value's context.
**Urgency**: U2 - de-escalated from U1 because the failure path is narrow admin-content sanitation, but it is still a swallowed exception in changed code.
**Fixer-Agent Action Plan**: Catch `UnicodeError`/`UnicodeDecodeError` narrowly, log the failure with the prompt/source context where available, and return a deterministic replacement or explicit validation error instead of the original unchecked value.

### Finding 4: Admin route surrogate cleanup repeats the same swallow pattern
**File**: `pinder-web/src/pinder-backend/routes/admin.py:276`
**Issue**: The route-level `_clean_surrogates` helper also catches every exception from the UTF-16 surrogate cleanup and returns the original object.
**Impact**: Admin response sanitation can silently fail in a second code path, leaving malformed strings to break response serialization later with no local diagnostic.
**Urgency**: U2 - de-escalated from U1 because the code is admin-only and narrow, but the exception remains swallowed.
**Fixer-Agent Action Plan**: Share the fixed sanitizer from `admin_prompt_content_service.py` or move it to one utility, catch only expected Unicode exceptions, and emit a structured warning or explicit admin validation error when cleanup fails.

### Finding 5: Character synthesis branches on exception message substrings
**File**: `pinder-web/src/Pinder.GameApi/Controllers/CharactersController.cs:572`
**Issue**: `RegenerateSynthesis` chooses API behavior with filters such as `catch (InvalidOperationException ex) when (ex.Message.Contains("requires generated"))`, `ex.Message.Contains("could not be assembled")`, and `IsSynthesisShapeFailure(ex)` checking message fragments like `"returned incomplete"`, `"Expected exactly 15"`, `"Failed to parse"`, and `"returned empty output"`.
**Impact**: Human-readable exception text has become a machine contract for HTTP status and operation failure classification. Changing punctuation, wording, localization, or adding a different `InvalidOperationException` with the same phrase can silently change API semantics.
**Urgency**: U2 - escalated from U3 because this style smell affects production API control flow and operation retry/failure classification.
**Fixer-Agent Action Plan**: Replace message-fragment filters with typed exceptions or a result/error discriminated type from the synthesis layer; catch those explicit types in the controller and keep user-facing copy separate from the control-flow signal.

### Finding 6: Terminal-turn and speculation polling delays are magic values
**File**: `pinder-web/frontend/src/pages/GameScreen.tsx:174`
**Issue**: `GameScreen` hardcodes `250` for the terminal-turn cost-summary retry and `750` at line 240 for speculation polling, with no named constant describing why these intervals differ.
**Impact**: Future changes to end-state polling or speculation UX can accidentally tune one delay without understanding the other, producing inconsistent UI responsiveness or extra backend load.
**Urgency**: U3 - topic default for style smells and magic values.
**Fixer-Agent Action Plan**: Introduce named constants such as `TERMINAL_COST_SUMMARY_RETRY_MS` and `SPECULATION_STATUS_POLL_MS` near the component, and reuse them in the two `setTimeout` calls with brief names that encode the behavior.
