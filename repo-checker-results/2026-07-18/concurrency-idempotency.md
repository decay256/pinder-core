# concurrency-idempotency

Scope audited:
- PRIMARY `pinder-core` at `e96a75f4`
- DEPENDENT `pinder-web` at `1a7acb38`

No concrete `pinder-core`-only concurrency/idempotency finding was found in the inspected session state, clone/speculation, XP ledger, or prompt trace surfaces. The confirmed findings are in `pinder-web`, where GameApi persistence and admin content writes coordinate durable side effects.

### Finding 1: Resolve retry returns cached success after durable side effects fail
**File**: `pinder-web/src/Pinder.GameApi/Services/ActiveSession.Turn.cs:316`
**Issue**: `ResolveTurnAsync` returns `_lastTurnResult` whenever `_lastTurnResult != null && _inFlightTurnMarker == null` at lines 316-319. The same method sets `_lastTurnResult = result` and `_inFlightTurnMarker = null` at lines 386-389 before `await TryPersistStateAsync(result, ct)` and `await TryWriteAuditAsync(...)` at lines 391-393. If persistence, audit, or operation-status tracking throws after the in-memory game turn mutates, the catch at lines 426-429 only clears `_inFlightTurnMarker`; a retry then returns the cached result and skips the durable write that failed.
**Impact**: A transient database or audit failure can leave the session advanced in memory but stale or missing in durable storage. Retrying the same resolve reports success without reapplying the required persistence/audit side effects, so reconnects or process restarts can roll the user back or lose turn history.
**Urgency**: U1 - topic default; this is an idempotency defect in a user-visible turn commit path.
**Fixer-Agent Action Plan**: Move `_lastTurnResult` publication and marker clearing after all mandatory side effects succeed, or introduce a pending-result state that retries persistence/audit before returning a cached result. Add tests that make the first `UpsertStateAsync` or audit write fail, retry `ResolveTurnAsync`, and assert the second call performs the durable write exactly once before returning cached success.

### Finding 2: Progression settlement splits idempotency marker and aggregate update across commits
**File**: `pinder-web/src/Pinder.GameApi/Services/ProgressionSettlementService.cs:59`
**Issue**: Settlement first checks `HasProcessedSessionAsync(sessionDto.SessionId, ct)` at line 59, loads or creates a progression row at lines 65-66, mutates XP/level/skill points/cash in memory at lines 70-88, then calls `SetProgressionSettledAsync(delta, ct)` followed by `UpdateProgressionAsync(progression, ct)` at lines 99-100. The repository commits the session settled marker and progression delta in one DbContext at `pinder-web/src/Pinder.GameApi/Data/Repositories/PlayerProgressionRepository.cs:35-49`, then overwrites the aggregate in a separate DbContext at lines 52-77.
**Impact**: If the first save succeeds and the aggregate update fails, the session is permanently marked processed while XP/cash/skill points were never applied. Concurrent settlements for different sessions on the same user can also load the same aggregate, compute from stale totals, and overwrite each other, losing XP, cash, level-ups, or granted skill points.
**Urgency**: U1 - topic default; retry/idempotency markers and user progression are non-atomic.
**Fixer-Agent Action Plan**: Replace the split repository calls with a single transactional `SettleSessionProgressionAsync` that conditionally claims the session/delta and increments the aggregate only when the claim is new. Use serializable isolation, row locks, or one atomic SQL statement/CTE with `INSERT ... ON CONFLICT DO NOTHING` plus conditional `UPDATE xp = xp + @delta`. Add regression tests for duplicate same-session retries, first-save-success/aggregate-failure recovery, and concurrent different-session settlements for one user.

### Finding 3: Skill-point spend uses stale read-modify-write and can accept duplicate submits
**File**: `pinder-web/src/Pinder.GameApi/Services/SkillPointService.cs:26`
**Issue**: `SpendPointsAsync` reads progression at line 26, checks `totalRequested > progression.UnspentSkillPoints` at lines 33-35, mutates `progression.UnspentSkillPoints` and allocated stat fields at lines 55-67, then calls `UpdateProgressionAsync(progression, ct)` at line 70. `UpdateProgressionAsync` overwrites every progression field from the stale entity at `pinder-web/src/Pinder.GameApi/Data/Repositories/PlayerProgressionRepository.cs:64-74` without a version column, row lock, or conditional update.
**Impact**: Two concurrent level-up requests can both pass the unspent-points check and both return success. Depending on timing, one allocation can be silently lost, or concurrent settlement changes to XP/cash/unspent points can be overwritten by the stale skill-spend entity.
**Urgency**: U1 - topic default; duplicate application and lost updates affect player progression.
**Fixer-Agent Action Plan**: Move skill spending into an atomic repository method that updates unspent points and allocation columns in one conditional statement, for example `WHERE user_sub = @user AND unspent_skill_points >= @total` with stat-cap predicates, or protect the row with a transaction and optimistic concurrency token. Add tests for two parallel spends with one available point and for concurrent settlement plus spend on the same progression row.

### Finding 4: Admin content saves share a mutable working tree before CAS captures request bytes
**File**: `pinder-web/src/pinder-backend/routes/endpoints/admin_git_writer.py:255`
**Issue**: `commit_and_push_to_main` documents that callers write bytes before calling the helper at lines 139-150, then it reads the "new desired bytes" back from the shared checkout at lines 255-263. Admin endpoints such as `pinder-web/src/pinder-backend/routes/endpoints/admin_content_write.py:348-350`, `:375-377`, `:402-412`, and `:740-779` write YAML/JSON first and commit afterward. Two concurrent saves to the same file can interleave between the handler write and the helper's `read_bytes()`, causing one request to capture and commit another request's payload; rollback paths can similarly restore stale bytes over a concurrent save.
**Impact**: An admin save can return success and a commit hash for bytes that were not the request's intended content, or a rejected save can clobber another admin's accepted update. This corrupts authoritative game/admin content despite the later remote compare-and-swap.
**Urgency**: U1 - topic default; local pre-CAS shared-state races can lose or miscommit content.
**Fixer-Agent Action Plan**: Pass the intended bytes into the git writer instead of rereading from the shared worktree, or lock write/capture/commit/rollback per repository and path. Prefer applying request bytes after fetch/reset inside the CAS loop or using an isolated temporary worktree per save. Add tests that delay request A after writing, let request B write/commit the same file, and assert A cannot commit B's bytes or roll B back.

### Finding 5: First share-token mint is check-then-write and can return an invalid token
**File**: `pinder-web/src/Pinder.GameApi/Data/GameSessionRepository.cs:234`
**Issue**: `RotateShareTokenAsync` loads the session at lines 234-236, returns the existing `row.ShareToken` if present at lines 239-244, otherwise generates a new token, assigns it, and saves at lines 250-252. Two first-time share requests can both observe no token, generate different tokens, and save; the last commit wins, while the earlier request has already returned a token that is no longer stored.
**Impact**: A user can copy or publish a newly returned share URL that immediately fails because a concurrent request overwrote the token. The comment promises re-clicking Share is idempotent, but the first mint is not safe under concurrency.
**Urgency**: U2 - de-escalated one level because the defect is narrow to first token minting and does not corrupt unrelated session state, but it still breaks an externally shared URL.
**Fixer-Agent Action Plan**: Mint with an atomic conditional update such as `UPDATE user_sessions SET share_token = @token WHERE session_id = @id AND share_token IS NULL RETURNING share_token`; when no row updates, reload and return the existing token. Alternatively use a row lock/serializable transaction. Add a parallel first-share test asserting all callers receive the persisted token.

### Suppressed Approved U1 Patterns Encountered
Suppressed by exception: Unsafe Leakage of Raw C# Exception Messages (ex.Message) in public GameApi endpoints at `pinder-web/src/Pinder.GameApi/Controllers/SessionsController.Actions.cs:410-412` - would be U1.

Suppressed by exception: Unsafe Leakage of GameApi Raw Exception/HTML Content (resp.text) to public-facing HTTP responses at `pinder-web/src/pinder-backend/routes/endpoints/admin_content_write.py:905-909` - would be U1.

Suppressed by exception: Direct Leakage of Internal System Errors and File Paths in Admin Controller Error Payloads at `pinder-web/src/pinder-backend/routes/endpoints/admin_content_write.py:918-922` - would be U1.

Suppressed by exception: Direct Leakage of Internal System Errors and File Paths in Admin Controller Error Payloads at `pinder-web/src/pinder-backend/routes/endpoints/admin_content_write.py:841-842` and `pinder-web/src/pinder-backend/routes/endpoints/admin_content_write.py:1068-1078` - would be U1.

Suppressed by exception: Direct Leakage of Internal System Errors and File Paths in Admin Controller Error Payloads at `pinder-web/src/pinder-backend/routes/endpoints/admin_git_writer.py:286-288`, `:295-297`, `:313-315`, `:348-350`, and `:406-412` - would be U1.

U1 count: 4
U2 count: 1
U3 count: 0
Mirror hash verification: byte-identical mirror verified after write; SHA256 recorded in worker final response.
