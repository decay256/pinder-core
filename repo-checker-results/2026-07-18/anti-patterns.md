Inspected full repositories at `pinder-core` commit `e96a75f4c4fb7b8c008f8c61403aae6327eb6ca2` and `pinder-web` commit `1a7acb382c7daab1976a23bf2b738b0a35b8c4ab` for anti-patterns only.

Suppressed by exception: Unsafe Leakage of GameApi Raw Exception/HTML Content (resp.text) to public-facing HTTP responses - would be U1
Suppressed by exception: Unsafe Leakage of Raw C# Exception Messages (ex.Message) in public GameApi endpoints - would be U1
Suppressed by exception: Exposure of Internal low-level Library Exceptions and Socket Errors to End-Users - would be U1
Suppressed by exception: Direct Leakage of Internal System Errors and File Paths in Admin Controller Error Payloads - would be U1

### Finding 1: Message-only fallback silently drops validated GM signals
**File**: `pinder-core/src/Pinder.LlmAdapters/GmOutputContract.cs:143`
**Issue**: `GmOutputContract.Parse` wraps the whole signal extraction block in a bare `catch` and returns `new GmTurnOutput(raw!.Trim())`. The file comment says production gameplay first calls `ValidateSignalsStrict`, but any later parser exception still collapses a response with `[SIGNALS]` into message-only output with null tell/weakness data.
**Impact**: A parser bug or unexpected regex/runtime failure can silently remove tell or weakness-window mechanics from a valid opponent response after the strict validation gate has accepted the shape, making gameplay state diverge without a failing turn or diagnostic.
**Urgency**: U1 - topic default for swallowed/bare exceptions; production gameplay signal parsing can silently lose mechanics.
**Fixer-Agent Action Plan**: Replace the bare catch with explicit, expected parse failures only, emit an operational diagnostic containing the parse failure and signal marker presence, and throw an `LlmContractException` when a validated `[SIGNALS]` block cannot be parsed. Add a regression test that feeds a signal-bearing response through `ValidateSignalsStrict` and forces the parse path to fail, asserting the turn fails loud instead of returning message-only output.

### Finding 2: Database URL option typos are swallowed during production startup
**File**: `pinder-web/src/Pinder.GameApi/Data/DatabaseUrlNormalizer.cs:96`
**Issue**: Query parameters from `PINDER_DATABASE_URL` are applied with `try { b[k] = v; } catch { /* unknown key - ignore */ }`. The comment explicitly says unknown keys are swallowed silently rather than failing boot.
**Impact**: A misspelled or unsupported option such as TLS/SSL, pooling, timeout, or application-name configuration is silently discarded while GameApi continues to start with different database behavior than the operator requested.
**Urgency**: U1 - topic default for swallowed/bare exceptions; production configuration errors can be silently ignored at startup.
**Fixer-Agent Action Plan**: Catch the concrete Npgsql exception type, collect unsupported keys, and fail startup with a redacted error naming only the rejected key names. Add a configuration test for an invalid query parameter that asserts `DatabaseUrlNormalizer.Normalize` throws, plus a positive test for known allowed query parameters.

### Finding 3: Request logging hides JWT extraction failures
**File**: `pinder-web/src/pinder-backend/auth.py:266`
**Issue**: `extract_user_sub_from_request` catches every `Exception` from `verify_jwt(...)`, executes `pass`, and returns `None`; `RequestLoggingMiddleware` then binds that `None` as `user_sub` for the request context.
**Impact**: Invalid, expired, malformed, or misconfigured-token failures on authenticated routes are logged as anonymous requests in the shared request context, reducing auditability and making auth incidents harder to correlate across backend and GameApi logs.
**Urgency**: U2 - de-escalated one level from swallowed-exception U1 because route authorization still fails through `require_auth`; the damage is observability/audit correlation.
**Fixer-Agent Action Plan**: Narrow the catch to the expected JWT/HTTPException path, bind a safe diagnostic marker such as `user_sub_extract_error=true` or `auth_failure_kind`, and avoid swallowing non-auth exceptions. Add middleware tests for malformed bearer tokens asserting request logs contain the request id plus an auth-extraction failure marker without exposing token contents.

### Finding 4: Session-number lock cleanup swallows filesystem failures
**File**: `pinder-core/session-runner/SessionFileCounter.cs:59`
**Issue**: stale lock cleanup uses `catch { }` around `File.GetCreationTimeUtc(lockFile)` and `File.Delete(lockFile)`, and `ReleaseLock` repeats the same pattern at line 85 with `try { File.Delete(lockPath); } catch { }`.
**Impact**: Permission, path, or filesystem failures leave stale `session-*.lock` files behind with no warning, so later playtest runs can skip numbers or eventually fail after 100 attempts with no evidence of the original cleanup problem.
**Urgency**: U2 - de-escalated one level from swallowed-exception U1 because this is CLI/session-runner tooling, but it can still block playtest output generation.
**Fixer-Agent Action Plan**: Return a cleanup result or accept an optional warning sink so lock cleanup failures are surfaced with the lock path and exception type. Add tests that simulate delete failure and assert the warning path is exercised while preserving the existing retry behavior for active locks.

### Finding 5: Player response delay buckets are embedded as unexplained numeric thresholds
**File**: `pinder-core/src/Pinder.Core/Conversation/PlayerResponseDelayEvaluator.cs:69`
**Issue**: delay policy is hardcoded inline as `1.0`, `15.0`, `60.0`, `360.0`, and `1440.0` minute thresholds, with the 1-6 hour condition duplicated in `IsOneToSixHourBucket`.
**Impact**: Any future tuning of the response-delay rules must update multiple literals correctly, and reviewers cannot tell from code whether these values are design-authoritative, defaults, or temporary calibration.
**Urgency**: U3 - topic default for magic-value style smells.
**Fixer-Agent Action Plan**: Introduce named constants or a small immutable policy object for the delay bucket boundaries and penalties, then update existing delay tests to assert behavior through named cases such as `OneToSixHours` and `TwentyFourHoursPlus`.

### Finding 6: XP success labels retain hidden fallback DC thresholds
**File**: `pinder-core/src/Pinder.Core/Conversation/SessionXpRecorder.cs:111`
**Issue**: `SessionXpRecorder` initializes `lowMax = 16` and `midMax = 20`, then uses reflection to look for `XpSuccessBase` before labeling XP events as `Success_DC_Low`, `Success_DC_Mid`, or `Success_DC_High`.
**Impact**: The success XP award can come from configured rules while the audit label silently falls back to hardcoded bucket boundaries if reflection misses the property, creating misleading XP ledger labels that are hard to trace back to rule configuration.
**Urgency**: U3 - topic default for magic values and fragile reflective access.
**Fixer-Agent Action Plan**: Add an explicit rule resolver method for success DC bucket thresholds or reuse the typed `GameDefinition` access path, remove the reflection and numeric fallbacks, and add tests where configured `dc_low_max`/`dc_mid_max` differ from 16/20 to verify labels follow configuration.

### Finding 7: Visual asset canvas concentrates multiple render-loop responsibilities in one component file
**File**: `pinder-web/frontend/src/components/visual-assets/VisualAssetCanvas.tsx:1037`
**Issue**: `VisualAssetCanvas.tsx` is 988 lines and combines asset preparation, material mapping, context-loss handling, keyboard camera control, animation loops, and the exported `VisualAssetCanvas` component; the same file contains multiple `useEffect` blocks and `useFrame` loops before the exported component.
**Impact**: Changes to visual asset loading, camera controls, WebGL lifecycle, or material mapping require reasoning through a large callback-heavy file, increasing the risk of hook-order regressions or missed cleanup when extending the 3D canvas.
**Urgency**: U3 - topic default for callback/component complexity style smells.
**Fixer-Agent Action Plan**: Extract focused hooks/components for WebGL context restoration, prepared-model lifecycle, keyboard camera controls, and material application. Preserve existing behavior with component tests plus Playwright/canvas smoke coverage for model render, camera movement, and context-loss recovery.

Counts: 7 findings total: U1=2, U2=2, U3=3.
