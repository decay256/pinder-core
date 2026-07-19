> Scope: files changed in pinder-core 7962415f750de354b53d4f9b953eaa3e37b3575b..5dd4274b82e0e2d1c78471909341323eafb84856 (91 existing files) and pinder-web c7a465bb67fb86ee6b5ab1105955b7c0717eddca..3350d6692a4f38c1ebeb6df272135c4a4d4fc9fe (88 HEAD-present paths); current HEADs pinder-core 5dd4274b82e0e2d1c78471909341323eafb84856 and pinder-web 3350d6692a4f38c1ebeb6df272135c4a4d4fc9fe. Only changed files were eligible for findings.

Replay schema fix validation: confirmed current-schema `turn_records.payload` projection now rejects missing/wrong `option`, `roll`, `interest`, booleans, and numbers via `ValidateCurrentSchemaTurnResultPayload` and `Require*` helpers. Absent payloads still return null, and only explicit versioned legacy payloads (`audit_schema_version < 2`) remain on the tolerant projection path. The prior U1 for `ReplayTurnResultMapper.cs` is resolved and intentionally not repeated.

Speculation fail-fast validation: confirmed `SessionSimulationService.SpeculateSessionAsync` does not fabricate a default model or empty transcript. Missing persisted model, conversation-history load failure, and missing player history log structured `event=speculate.*` errors and throw typed dependency/invalid-state exceptions before an LLM call.

### Finding 1: Replay endpoints degrade turn-record load failures into successful text-only responses
**File**: `pinder-web/src/Pinder.GameApi/Controllers/SessionsController.Replays.cs:69`
**Issue**: Public share replay catches any exception from `LoadTurnRecordsAsync`, logs `"replay will return entries without per-turn results."`, and continues with `turnRecords = Array.Empty<Pinder.GameApi.Data.TurnRecordRow>()`. The same fail-open pattern remains in `/sessions/{id}/log` at line 214 and owner replay at line 362.
**Impact**: A repository/database failure on durable turn-record loading is indistinguishable from a legitimate legacy session with no audit rows at the HTTP contract. Callers receive 200 responses with missing roll/result/option details even though the replay dependency failed.
**Urgency**: U2 - de-escalated from U1 because this is a logged read-only replay/log path rather than core turn resolution, but it still presents unavailable durable audit data as a valid degraded response.
**Fixer-Agent Action Plan**: Preserve the legacy text-only response only when `LoadTurnRecordsAsync` succeeds and returns no records. For exceptions, return the same replay integrity/unavailable error shape used for malformed payload projection, or a distinct retryable dependency failure for transient repository errors. Add tests where `LoadTurnRecordsAsync` throws for share replay, session log, and owner replay and assert non-200 failure instead of null results.

### Finding 2: Frozen character-sheet replay baseline still falls back to live sheets on capture and decode failures
**File**: `pinder-web/src/Pinder.GameApi/Services/SessionStore.cs:179`
**Issue**: Session creation still wraps frozen character-sheet pre-computation in `catch (Exception ex)`, logs a warning, and sets `frozenCharacterSheetsJson = null`, with comments saying the live-fetch fallback remains correct. Owner replay also catches any exception while deserializing `frozen_character_sheets` at `SessionsController.Replays.cs:453`, logs, sets `frozen = null`, and returns 200 so the frontend live-fetches current sheets.
**Impact**: If sheet building, progression/rule lookup, serialization, or frozen-baseline JSON decoding fails for a new durable session, owner replay can silently lose the at-session-start baseline and show current character sheets instead. That can make replay deltas and starting-state comparisons wrong while the session/replay response looks successful.
**Urgency**: U2 - de-escalated from U1 because the failures are logged and affect replay correctness rather than live turn resolution, but the API still hides baseline loss behind a successful fallback.
**Fixer-Agent Action Plan**: Limit `null` frozen sheets to explicit legacy/no-persistence cases or missing character details that are intentionally supported. Fail session creation or persist an explicit frozen-baseline error marker when build/serialization fails, and make owner replay fail with a replay integrity error when a present `frozen_character_sheets` value is malformed. Add regression tests for builder exceptions, serialization failure, and malformed frozen JSON.

Suppressed by exception: Unsafe Leakage of GameApi Raw Exception/HTML Content (resp.text) to public-facing HTTP responses - would be U1
Suppressed by exception: Unsafe Leakage of Raw C# Exception Messages (ex.Message) in public GameApi endpoints - would be U1
Suppressed by exception: Exposure of Internal low-level Library Exceptions and Socket Errors to End-Users - would be U1
Suppressed by exception: Direct Leakage of Internal System Errors and File Paths in Admin Controller Error Payloads - would be U1

Verification: `DOTNET_ROLL_FORWARD=Major dotnet test src/Pinder.GameApi.Tests/Pinder.GameApi.Tests.csproj --no-build --filter "FullyQualifiedName~ReplayTurnResultMapperSchemaValidationTests"` passed 10/10.

Counts: 2 findings total; U1=0, U2=2, U3=0. Suppressed would-be U1 by approved exception=4.
