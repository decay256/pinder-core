> Scope: full repositories. Primary `pinder-core` at `e96a75f4`; dependent `pinder-web` at `1a7acb38`.
> Topic: silent-fallbacks - silent defaults, swallowed critical failures, mocked/invented contract data on failure, and degraded behavior presented as success.

### Finding 1: Character index silently drops unreadable, malformed, and duplicate character files
**File**: `pinder-core/src/Pinder.SessionSetup/DirectoryCharacterStore.cs:216`
**Issue**: The index builder treats invalid character files as absence and continues: `string? id = await TryReadCharacterIdAsync(path, ct).ConfigureAwait(false); if (id == null) continue;` and duplicate `character_id` values are overwritten by `index[id] = path;`. `TryReadCharacterIdAsync` also converts `IOException` and `JsonException` into `return null` at lines 244-250.
**Impact**: A corrupt, unreadable, schema-wrong, or duplicate character JSON file disappears from `ListIdsAsync()`/`LoadAsync()` without failing the character catalog load. Production can present a successful but incomplete character catalog, and duplicate IDs become last-write-wins data drift instead of a visible content error.
**Urgency**: U1 - topic default; this can silently remove or replace production character data while the repository/store reports success.
**Fixer-Agent Action Plan**: Replace the nullable ID probe with an index-validation result that records filename-specific parse/read/duplicate errors. Make `EnsureIndexLockedAsync` throw an aggregate `InvalidOperationException`/domain exception for malformed production files and duplicate IDs, while explicitly skipping only `character-schema.json`. Add tests for malformed JSON, missing `character_id`, unreadable file simulation, and duplicate IDs asserting that `ListIdsAsync()` fails loudly.

### Finding 2: Production rule resolver allows missing YAML rule values to fall back to hardcoded defaults
**File**: `pinder-core/src/Pinder.LlmAdapters/GameDefinition.cs:296`
**Issue**: The data-driven resolver advertises `public bool AllowDefaultFallback => true`, and core gameplay consumers honor it. For example, `LevelTable.GetLevel` returns `GetLevel(xp, DefaultRuleResolver.Instance)` when no thresholds are found, while `SessionXpRecorder` falls back through `DefaultRuleResolver.Instance.GetFlatXpAward(...)`, `GetRiskTierXpMultiplier(...)`, and `GetSuccessBaseXp(...)` before throwing.
**Impact**: If `data/game-definition.yaml` is partially missing progression or XP rule values, production gameplay silently uses embedded defaults instead of failing the config load. That can ship incorrect XP, level, roll-bonus, or failure-pool behavior while tests and runtime appear green because hardcoded defaults filled the gap.
**Urgency**: U1 - topic default; missing production game-rule data changes player progression silently.
**Fixer-Agent Action Plan**: Make parsed `GameDefinition` fail closed for required progression/XP keys, or set `AllowDefaultFallback` false for production-loaded definitions and reserve fallback only for explicit test/dev resolvers. Add parser/loader tests that remove each progression/XP section and assert startup/config load failure, plus unit tests proving `LevelTable` and `SessionXpRecorder` throw when a production resolver lacks required values.

### Finding 3: Backend health reports GameApi dependency failure as HTTP 200, so deploy and Docker checks mark degraded as healthy
**File**: `pinder-web/src/pinder-backend/main.py:252`
**Issue**: `/health` maps only the JSON body to degraded state: `game_api_status = "ok" if resp.status_code == 200 else "error"`, `game_api_status = "unreachable"` in the catch block, then `overall = "ok" if game_api_status == "ok" else "degraded"` and `return {"status": overall, "game_api": game_api_status}`. The docstring explicitly says GameApi down returns `200 {"status": "degraded"...}`. But `docker-compose.yml`/`docker-compose.staging.yml` healthchecks exit success for any 2xx, and `deploy.sh` waits with `curl -fsS .../health`, which also treats that 200 degraded response as healthy.
**Impact**: A backend whose required GameApi dependency is down can become Docker-healthy and pass staging/prod deploy health gates. Operators get a successful deployment/health signal while the main game API dependency is unavailable.
**Urgency**: U1 - topic default; degraded dependency failure is presented as deployment/container success.
**Fixer-Agent Action Plan**: Split liveness and readiness, or make `/health` return a non-2xx status when `game_api_status != "ok"` for readiness checks. Update Docker Compose and `deploy.sh` to assert both HTTP status and JSON `status == "ok"`. Add backend tests for GameApi unreachable returning non-healthy readiness, and script/compose tests proving `degraded` fails the deploy health gate.

### Finding 4: Setup succeeds after outfit-description generation failure and only stores an empty scene entry
**File**: `pinder-web/src/Pinder.GameApi/Services/ActiveSession.Setup.cs:199`
**Issue**: Setup awaits the outfit-description task inside a broad catch and converts any non-cancellation failure into empty content: `catch (Exception outfitEx) { _logger.LogWarning(...); outfitDescription = string.Empty; }`. The same setup then seeds scene entries with that empty value and immediately calls `TrackSetupSucceededAsync(...)`, sets `_setupComplete = true`, and logs setup complete.
**Impact**: The opening-scene/outfit generation can fail in production while the session setup operation is marked succeeded and the session becomes playable with missing scene context. Users see a normal setup success, while operators must notice a warning log to know setup actually degraded.
**Urgency**: U2 - de-escalated from U1 because the failure is logged and affects optional scene flavor, but the public operation status still presents degraded setup as success.
**Fixer-Agent Action Plan**: Represent outfit generation failure as an explicit degraded setup phase/status instead of `Succeeded`, or fail setup when degraded mode is disabled. Thread the degradation into `SetupStatusResponse`/operation diagnostics so UI and admin operations can distinguish ready-clean from ready-degraded. Add tests where `outfitTask` throws and assert either setup fails closed or records `OperationStatus.Degraded` with no silent empty scene entry.

Suppressed by exception: Unsafe Leakage of GameApi Raw Exception/HTML Content (resp.text) to public-facing HTTP responses — would be U1
Suppressed by exception: Unsafe Leakage of Raw C# Exception Messages (ex.Message) in public GameApi endpoints — would be U1
Suppressed by exception: Exposure of Internal low-level Library Exceptions and Socket Errors to End-Users — would be U1
Suppressed by exception: Direct Leakage of Internal System Errors and File Paths in Admin Controller Error Payloads — would be U1

Counts: 4 findings total; U1=3, U2=1, U3=0. Suppressed would-be U1 by approved exception=4.
Output hash confirmation: body SHA256 (all content above this line, UTF-8) = ce3397cbf64048cfdbc85846ed29c18c5892fede3e8a7a7742e8576afd55e67f
