> Scope: full pinder-core and pinder-web repositories at the finalized post-fix commits (`pinder-core` 7962415f750de354b53d4f9b953eaa3e37b3575b, `pinder-web` c7a465bb67fb86ee6b5ab1105955b7c0717eddca).

### Finding 1: Persisted conversation history restore silently drops invalid message roles
**File**: `pinder-core/src/Pinder.Core/Conversation/GameSessionState.cs:213`
**Issue**: `RestoreFromSnapshot(...)` rebuilds `DateeHistory` and `AvatarHistory` by calling `new ConversationMessage(role, content ?? string.Empty)` inside per-entry `try` blocks, then swallows `ArgumentException` with empty `catch (ArgumentException) { }` blocks at lines 213-215 and 229-231. `ConversationMessage` throws when the role is not exactly `user` or `assistant` after normalization, so a malformed persisted role is silently omitted from restored state.
**Impact**: A corrupted or drifted persisted conversation snapshot can lose individual datee/avatar messages without telemetry, diagnostics, or a failed restore. That can change the LLM state sent after reload/resimulation while leaving no obvious evidence for operators or fixers.
**Urgency**: U1 - swallowed/bare exception topic default.
**Fixer-Agent Action Plan**: Replace the empty catches with a structured restore diagnostic that includes the history kind, role, and entry index, and decide whether invalid persisted roles should fail restore or be explicitly quarantined. Add a regression test that an invalid datee/avatar history role is either surfaced as a restore error or emitted as a diagnostic rather than silently dropped.

### Finding 2: Missing progression threshold keys are treated as the end of the level table
**File**: `pinder-core/src/Pinder.Core/Progression/LevelTable.cs:62`
**Issue**: `GetLevel(...)` calls `rules.GetXpThresholdForLevel(currentCheck)` in a loop, but `catch (KeyNotFoundException)` at lines 62-80 converts a missing key after level 1 into `break;`. That means a resolver that throws for a missing level threshold is interpreted the same as a clean end-of-table result, even though other progression lookups in this file throw when required values are missing.
**Impact**: A malformed production rule table can cap player level silently instead of failing the settlement path. XP can continue accruing while level, roll bonus, build points, and failure-pool tier resolution drift from the configured rules.
**Urgency**: U1 - swallowed/bare exception topic default.
**Fixer-Agent Action Plan**: Stop catching `KeyNotFoundException` as normal control flow for production resolvers. Require `GetXpThresholdForLevel(...)` to return `null` for intentional end-of-table and throw an `InvalidOperationException` with level/resolver context for thrown lookup failures. Add tests for a resolver that throws at level 2 and for a clean `null` end-of-table.

### Finding 3: Replay audit JSON parse failures disappear as missing turn data
**File**: `pinder-web/src/Pinder.GameApi/Models/ReplayTurnResultMapper.cs:39`
**Issue**: The public replay mapper catches malformed audit JSON and returns `null` in multiple entry points: `FromAuditPayload(...)` at line 39, `OptionsFromAuditPayload(...)` at line 125, and `ChosenOptionFromAuditPayload(...)` at line 160. The owner-side mapper repeats the same `catch (JsonException) { return null; }` pattern in `ReplayTurnResultMapper.Owner.cs:19`.
**Impact**: A malformed persisted audit payload is projected as absent replay data, so public and owner replay views can silently omit turn results/options instead of surfacing a data integrity problem. The controller has no parse-failure signal to log, count, or show as a degraded replay row.
**Urgency**: U1 - swallowed/bare exception topic default.
**Fixer-Agent Action Plan**: Return a typed projection result that distinguishes `MissingLegacyPayload` from `MalformedPayload`, or throw a mapper exception that the replay controller logs with session/turn context before rendering a degraded row. Cover malformed payloads in public and owner replay tests.

### Finding 4: GameScreen silently ignores terminal turn fetch failures
**File**: `pinder-web/frontend/src/pages/GameScreen.tsx:177`
**Issue**: The end-state fetch effect defines `tryFetch(...)`, calls `getTurn(sessionId)`, optionally retries once after 250 ms for a missing cost summary, and then catches all failures with `catch { // Silent fail }` at lines 177-179.
**Impact**: If the terminal turn fetch fails because the backend returns an error, the session was removed, or the payload cannot be parsed, the player screen keeps whatever state it already had. The failure is not reported to UI state, console/error reporting, or retry telemetry, making a missing or stale game-end screen hard to diagnose.
**Urgency**: U1 - swallowed/bare exception topic default.
**Fixer-Agent Action Plan**: Capture the error into a terminal-state load error, send it through the client error reporter or existing user-visible error path, and stop retrying only after recording the failed fetch. Add a component test where `getTurn` rejects after `sessionEndedForFetch` and the UI records an error/degraded state.

### Finding 5: Backend version probe hides GameApi dependency failures
**File**: `pinder-web/src/pinder-backend/main.py:208`
**Issue**: `/api/version` initializes `game_api_version` and `core_version` to `"unknown"`, tries to fetch `GAME_API_URL/version`, and then uses `except Exception: pass` at lines 208-209 when the upstream call fails.
**Impact**: Operators and the top navigation can see `"unknown"` versions without any backend log tying that state to a GameApi connection, JSON, or status failure. The adjacent `/health` endpoint logs dependency errors, but the version endpoint itself erases the failure path.
**Urgency**: U2 - de-escalated from U1 because this is an ops metadata endpoint and `/health` logs dependency failures, but the bare swallow still hides version-drift diagnostics.
**Fixer-Agent Action Plan**: Log the exception with `GAME_API_URL`, duration, and request id, then return an explicit version probe status such as `game_api_version_status: "unreachable"`. Add a FastAPI test that a failing upstream version request leaves an observable log/status field instead of only `"unknown"`.

### Finding 6: Texting-style conflict YAML is parsed with substring state machines
**File**: `pinder-core/src/Pinder.Core/Prompts/TextingStyleConflicts.cs:142`
**Issue**: `TextingStyleConflicts.LoadFrom(...)` parses `persona/texting-style-conflicts.yaml` by splitting raw text on newlines at line 149, collecting blocks that start with the literal `"- axis_a:"`, and extracting values through `Substring(...)`, `IndexOf("axis:")`, `IndexOf("value:")`, and a custom `UnquoteYamlString(...)` routine at lines 213-305.
**Impact**: Valid YAML formatting changes, nested quoting, comments after values, multiline reasons, or flow-map variations can be rejected or misparsed even though the repository already depends on YAML parsers for prompt/admin content. This makes the conflict catalog fragile around exactly the data authors are likely to edit by hand.
**Urgency**: U3 - style-smell topic default.
**Fixer-Agent Action Plan**: Parse the conflict catalog with the same structured YAML stack used by the data/admin content pipeline, map it into a typed DTO, and keep the existing validation for non-empty reasons and known axes. Add fixtures for reordered flow-map keys, commas in values, quoted colons, and multiline reasons.

### Finding 7: Emotion-stem selection hides gameplay tuning in unexplained literals
**File**: `pinder-core/src/Pinder.Core/Conversation/EmotionStemSelector.cs:67`
**Issue**: `EmotionStemSelector.Resolve(...)` hardcodes phase thresholds `TurnCount >= 8`, `InterestScore >= 70`, `TurnCount >= 4`, `InterestScore >= 40`, hysteresis `TurnCount == 4 || TurnCount == 5` and `InterestScore <= 55`, registry sizes `20` and `15`, favored indices `13` and `19`, and weight `5.0` at lines 67-122. The only production caller seeds it with `new EmotionStemSelector(42 + state.TurnNumber)` in `TurnOrchestrator.cs:182`.
**Impact**: Revelation phase boundaries, registry cardinality, weighting, and randomness policy are gameplay-tuning values, but they are embedded as local literals instead of named constants or data-driven rules. Adding/removing backstory/stake fields or balancing phase movement can silently desynchronize selection from character data and tests.
**Urgency**: U3 - style-smell topic default.
**Fixer-Agent Action Plan**: Introduce named constants or a small rule/config object for phase thresholds, hysteresis ranges, registry limits, favored indices, weights, and the deterministic seed salt. Add tests that assert the configured registry sizes match available backstory/stake data and that phase boundaries are intentionally documented.
