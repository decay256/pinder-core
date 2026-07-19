## Documentation vs Code Mismatches Audit

Audited `pinder-core` and `pinder-web` at their current checked-out commits for stale markdown, source comments, examples, runbooks, and code-contract descriptions where live code is authoritative. Inspected top-level READMEs, architecture/deployment/features docs, source comments in core domain models and frontend pages, deploy scripts, Dockerfile data-copy behavior, FastAPI route wiring, ASP.NET DTOs/controllers, frontend API/types, and relevant tests around share DTOs and deployment checks.

### Finding 1: Migration Runbook Still Says Alembic Is Manual
**File**: `pinder-web/docs/ARCHITECTURE.md:157`
**Issue**: The deployment runbook says `### Migrations are NOT auto-run` and instructs operators: `If any migration files have changed: after deploy, run alembic upgrade head inside the backend container BEFORE traffic is restored.` Live deploy code now runs Alembic before service startup on both paths: `deploy.sh:519` runs `run --rm --no-deps backend-staging alembic upgrade head`, and `deploy.sh:597` runs `docker compose run --rm --no-deps backend alembic upgrade head` for prod.
**Impact**: Operators following the canonical architecture doc can perform redundant or wrongly ordered manual schema work, and the doc no longer describes the actual failure mode: deploy now stops services and fails loudly if Alembic fails before startup.
**Urgency**: U2 - escalated from U3 because this is production deployment runbook drift on the schema migration path.
**Fixer-Agent Action Plan**: Update `docs/ARCHITECTURE.md` migration section to state that `deploy.sh` runs Alembic automatically for prod and staging before service startup, remove the manual post-deploy instruction, and cross-reference the rollback/failure messages in `deploy.sh`. Verify by grepping `deploy.sh` for both `backend-staging alembic upgrade head` and `backend alembic upgrade head`.

### Finding 2: GameApi Data-Sync Docs Contradict the Script and Reference a Missing Drift Gate
**File**: `pinder-web/docs/deployment-and-staging.md:554`
**Issue**: The data-sync section says `deploy.sh calls verify_gameapi_data_sync as the final step of the prod deploy path only (staging does not verify sync)`, but the same section later says both stacks are checked, and live code calls `verify_gameapi_data_sync "staging"` on staging deploys at `deploy.sh:580` plus both `verify_gameapi_data_sync "prod"` and `verify_gameapi_data_sync "staging"` on prod deploys at `deploy.sh:629-630`. The section also points readers to `scripts/check-data-drift.sh`, but `scripts/` contains no such file.
**Impact**: Operators and fixers get conflicting instructions about whether staging validates baked pinder-core data, and the referenced local verification command cannot be run.
**Urgency**: U3 - topic default; operationally relevant but the deploy script itself is authoritative and does perform the checks.
**Fixer-Agent Action Plan**: Rewrite `docs/deployment-and-staging.md` §7b and `docs/ARCHITECTURE.md` §5.9 to match `deploy.sh`: staging-only deploy checks staging, prod deploy checks prod and staging. Either add the missing `scripts/check-data-drift.sh` or remove/replace the references with the current verification command. Verify with `rg -n "check-data-drift|verify_gameapi_data_sync" docs deploy.sh scripts`.

### Finding 3: Core README Omits Live Assemblies and Tools
**File**: `pinder-core/README.md:7`
**Issue**: The README assembly table lists only `Pinder.Core`, `Pinder.Rules`, `Pinder.LlmAdapters`, and `session-runner`, and the project structure block lists only `src/Pinder.Core`, `src/Pinder.LlmAdapters`, `src/Pinder.Rules`, and `session-runner`. Live solution membership includes additional production/tool assemblies: `src/Pinder.RemoteAssets/Pinder.RemoteAssets.csproj`, `src/Pinder.SessionSetup/Pinder.SessionSetup.csproj`, `tools/NarrativeHarness/NarrativeHarness.csproj`, and `tools/TextingStyleAuditor/TextingStyleAuditor.csproj`.
**Impact**: New agents entering through the README miss the current setup-generation, remote-assets, and narrative/texting audit surfaces, which are active project boundaries with tests and public docs elsewhere.
**Urgency**: U3 - topic default; onboarding/documentation accuracy drift.
**Fixer-Agent Action Plan**: Refresh the README assembly table and project structure from `dotnet sln Pinder.Core.sln list`, adding purpose/dependency rows for SessionSetup, RemoteAssets, NarrativeHarness, TextingStyleAuditor, and related test projects. Verify by comparing the README table against the solution list.

### Finding 4: CharacterDefinition XML Comments Describe v1 Tier Anatomy While Code Is v2 Scalar Anatomy
**File**: `pinder-core/src/Pinder.Core/Characters/CharacterDefinition.cs:30`
**Issue**: The source comments say `The integer schema version. v1 files MUST have this set to 1`, then define `public const int CurrentSchemaVersion = 2`; they also say `Anatomy selections. Map of parameter id ... to tier id (e.g. "short")` while the property is `public IReadOnlyDictionary<string, float> Anatomy`. The live schema at `data/characters/character-schema.json:20-54` requires `schema_version` const `2` and describes anatomy as parameter id to normalized float `[0..1]`.
**Impact**: Developers reading IntelliSense or generated API docs can implement old v1/tier-id character data against a loader that now rejects unknown schema versions and expects scalar anatomy.
**Urgency**: U3 - topic default; source-comment drift with data-authoring consequences.
**Fixer-Agent Action Plan**: Update the XML comments to say schema v2 and normalized float anatomy scalars, with examples from `character-schema.json`. Verify by grepping for remaining `v1` or `tier id` comments in `CharacterDefinition.cs` and running the character schema/loader tests.

### Finding 5: PublicShareDto Privacy Contract Omits Live Public Fields
**File**: `pinder-web/docs/features.md:205`
**Issue**: The feature doc says the allowed public share fields are `share_token`, `character_slug`, `opponent_slug`, `model`, `created_at`, `ended_at`, `outcome`, `turn_count`, `final_interest`, and `conversation_history[]`. Live `PublicShareDto` also requires `character_id`, `opponent_id`, and `player_sender_name` (`src/Pinder.GameApi/Models/PublicShareDto.cs:39`, `:46`, `:107`), and the same doc later acknowledges `character_id + opponent_id` at `docs/features.md:470`.
**Impact**: The privacy contract is the place reviewers use to decide whether a public field is sanctioned; it currently makes shipped fields look like accidental leaks or makes the top-level contract less complete than the tests and DTO.
**Urgency**: U3 - topic default; public API privacy-documentation drift.
**Fixer-Agent Action Plan**: Update §8's allowed list to include `character_id`, `opponent_id`, and `player_sender_name` with the same privacy rationale used in the DTO comments. Verify against `ShareControllerTests` and `ReplayPrivacyTests` forbidden/allowed field assertions.

### Finding 6: AdminPage Header Comment Still Describes Phase-0 Read-Only Five-Tab UI
**File**: `pinder-web/frontend/src/pages/AdminPage.tsx:4`
**Issue**: The file header says `/admin` is a `Read-only viewer for the five game-content files`, renders `the five read-only forms`, and has `No save buttons, no add/delete, no PUT calls`. Live code in the same file defines 15 tabs at `AdminPage.tsx:44-60`, including `backstory`, prompt tabs, `narrative-testbed`, `session-inspector`, and `operations`, while editor modules such as `AdminPage.promptsEditor.tsx:4` import `putAdminContent`.
**Impact**: The first comment in the primary admin page misleads maintainers about the current surface area and write behavior before they reach the actual tab metadata and save-capable child modules.
**Urgency**: U3 - topic default; stale source comment on a broad frontend admin surface.
**Fixer-Agent Action Plan**: Replace the phase-0 header with a current summary of the admin shell, its auth gate, tab registry, and save-capable child modules. Verify by comparing the comment against `TABS` and grepping admin page modules for `putAdminContent`.

### Finding 7: Architecture Doc Points New Admin Content Endpoints at Retired Main.py Wiring
**File**: `pinder-web/docs/ARCHITECTURE.md:1623`
**Issue**: The "Where to Look" table says to add a new admin content endpoint as `New PUT /api/admin/content/<file> in src/pinder-backend/main.py` and to add a tab editor module under `frontend/src/pages/AdminPage.<tab>Editor.ts`. Live backend routing is split: `main.py:477-484` includes `routes.admin`, `routes/admin.py:35-37` includes `routes.endpoints.admin_content_write`, and the write path centralizes commits via `_commit_admin_content_write` in `routes/endpoints/admin_content_write.py:1121`. Live frontend naming also has `.tsx` editors such as `AdminPage.promptsEditor.tsx`, not just `.ts` helpers.
**Impact**: A fixer following the architecture map is likely to add endpoints to the wrong module or miss the CAS commit/push path that every write must use.
**Urgency**: U3 - topic default; maintainability drift in a frequently used change map.
**Fixer-Agent Action Plan**: Update the "Add a new admin content endpoint" row to name `routes/endpoints/admin_content_write.py`, `routes/admin.py` router inclusion, the CAS writer helper, and the current frontend component/helper naming. Verify by tracing a current PUT endpoint from FastAPI router include to frontend tab wiring.

## Counts

U1: 0
U2: 1
U3: 6

No approved exception suppressed a doc-code-mismatch finding in this topic.
