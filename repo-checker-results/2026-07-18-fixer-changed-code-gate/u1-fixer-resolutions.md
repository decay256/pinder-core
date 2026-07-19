# U1 fixer resolutions

## core-openai / anti-patterns-resolved.md

### Finding 2: OpenAiClient malformed-response catch discards the original parser/deserializer exception
**Status**: Resolved
**Resolution**: OpenAiClient now preserves malformed successful-response parser/deserializer failures by capturing the original exception and passing it as the InnerException of the safe InvalidOperationException wrapper. The existing provider telemetry path and sanitized diagnostic message remain unchanged, so callers keep safe provider context while still retaining the original failure cause for debugging.
**Verification**: `DOTNET_ROLL_FORWARD=Major dotnet test tests\Pinder.LlmAdapters.Tests\Pinder.LlmAdapters.Tests.csproj --filter FullyQualifiedName~OpenAiClientSafeDiagnosticsTests --no-restore --verbosity minimal` passed with 2 tests. `dotnet build src\Pinder.LlmAdapters\Pinder.LlmAdapters.csproj --no-restore --verbosity minimal` succeeded with 0 warnings and 0 errors. `git diff --check` passed with only existing line-ending normalization warnings.

## web-lifespan / anti-patterns-resolved.md

### Finding 1: src/pinder-backend/main.py lifespan shutdown catches asyncio.CancelledError and Exception together and silently passes
**Status**: Resolved
**Resolution**: FastAPI lifespan shutdown now drains the cleanup loop task explicitly, separating the intentional shutdown cancellation path from unexpected failures. If the cleanup task is still running, shutdown cancels it and keeps the resulting `asyncio.CancelledError` quiet. Any other exception raised while awaiting the cleanup task is logged as `cleanup.loop.task_failed` with structured task name, cancellation-state, failure-kind, and `exc_info` metadata before the remaining HTTP client and database engine shutdown work continues. Regression tests cover both the quiet intentional-cancel path and the unexpected task-failure logging path.
**Verification**: `uv run --with-requirements requirements.txt --with-requirements requirements-dev.txt python -m pytest tests/test_lifespan_resources.py -q` passed with 5 tests. `git diff --check` passed with line-ending warnings only.

## web-migration / migration-integrity-resolved.md

### Finding 1: Migration tests and Alembic metadata omit live operation tables
**Status**: Resolved
**Resolution**: Added authoritative SQLAlchemy metadata for the live operation tracking tables created by Alembic head `f136c3f3ada6`: `operation_snapshots`, `operation_events`, `operation_idempotency_claims`, and `operation_retry_dispatches`. The metadata now includes their columns, primary keys, foreign keys, critical check constraints, and indexes so Alembic autogenerate no longer treats GameApi-owned persistence tables as unmanaged drift. While strengthening the parity guard, the already-migrated `token_usages` table was also added to `Base.metadata`, and the portable narrative harness created-at index metadata was normalized so a fresh SQLite migration can pass Alembic's diff check without generating a schema change. No sibling migration head was created; the Alembic graph remains linear at `f136c3f3ada6`.
**Verification**: `uv run --with-requirements requirements.txt --with-requirements requirements-dev.txt pytest tests/test_db_migrations.py` passed with 12 tests. A fresh SQLite database was upgraded from zero to `head`, then `uv run --with-requirements requirements.txt --with-requirements requirements-dev.txt alembic check` reported no new upgrade operations.

## web-replays / silent-fallbacks-resolved.md

### Finding 4: Malformed turn audit payloads are skipped while replay endpoints still return success
**Status**: Resolved
**Resolution**: SessionsController replay assembly now treats present malformed turn_records.payload values as replay integrity failures instead of partial success. Public share replay, session log, and owner replay catch ReplayAuditPayloadException separately, log a traceable endpoint/session/turn message internally without payload contents, and return a safe 500 ErrorResponse with replay_integrity_error/malformed_turn_audit_payload. Unexpected replay payload projection failures also fail closed instead of skipping embeds. Blank or missing payloads and missing turn records still preserve the legacy text-only path.
**Verification**: C:\Users\decay\.dotnet\dotnet.exe test src\Pinder.GameApi.Tests\Pinder.GameApi.Tests.csproj --no-restore --filter FullyQualifiedName~ShareControllerTests -v minimal passed 15 tests; C:\Users\decay\.dotnet\dotnet.exe build src\Pinder.GameApi\Pinder.GameApi.csproj --no-restore -v minimal passed with 0 warnings and 0 errors; git diff --check passed with CRLF working-copy warnings only; the PowerShell equivalent of scripts/check_version_bump.sh confirmed the GameApi version increased from origin/main 0.2.6 to 0.2.8.

## web-replay-schema / silent-fallbacks-resolved.md

### Finding 1: Durable replay mapper still invents result fields for present-but-incomplete audit payloads
**Status**: Resolved
**Resolution**: Added strict current-schema validation before replay audit payload projection. Current durable turn-result rows now require the top-level turn number plus option, roll, and interest fields needed to render a non-legacy replay result, with missing or wrong-typed required values raising `ReplayAuditPayloadException` instead of being defaulted into plausible replay data. The tolerant projection path remains available only for absent payloads or payloads explicitly marked with a pre-current `audit_schema_version`, preserving provenance for known legacy records while failing fast on corrupted current durable state. Chosen-option and option-list projections also reject malformed current option fields rather than fabricating index, stat, or intended-text values.
**Verification**: Ran focused mapper tests with .NET roll-forward: `ReplayTurnResultMapperSchemaValidationTests`, `Issue466FullOptionsArrayTests`, and `Issue518StructuredAttemptedBooleansTests` passed 33/33. `dotnet build src/Pinder.GameApi/Pinder.GameApi.csproj --no-restore` passed with 0 warnings/errors. `C:\Program Files\Git\bin\bash.exe scripts/check_version_bump.sh` passed. `git diff --check` passed with only CRLF working-copy warnings. Share replay controller regressions compile; runtime execution on this machine is blocked by missing Microsoft.NETCore.App 8.0 and ASP.NET serialization incompatibility when forcing .NET 10 roll-forward, but detailed controller logs show `ReplayAuditPayloadException` reaches the existing `ReplayAuditPayloadFailure` path before the environment-level serializer failure.

## web-simulation / silent-fallbacks-resolved.md

### Finding 1: Admin speculation silently chooses the first allow-listed model when session state has no model
**Status**: Resolved
**Resolution**: Admin speculation now treats a blank persisted session model as invalid durable state and fails before model selection can fall back to the allow-list or a request override. The service emits a structured `speculate.invalid_state` diagnostic with `reason=missing_persisted_model`, and the controller boundary preserves the public-safe internal-server-error response instead of exposing implementation details.
**Verification**: `C:\Users\decay\.dotnet\dotnet.exe test src\Pinder.GameApi.Tests\Pinder.GameApi.Tests.csproj --filter FullyQualifiedName~SpeculateSessionControllerTests -v:minimal` passed with 9 tests. `C:\Users\decay\.dotnet\dotnet.exe build src\Pinder.GameApi\Pinder.GameApi.csproj -v:minimal` passed with 0 warnings and 0 errors. `git diff --check` passed with line-ending normalization warnings only.

### Finding 2: Speculation continues with empty history and placeholder player text after repository history failure
**Status**: Resolved
**Resolution**: Admin speculation no longer converts repository conversation-history failures into an empty transcript and no longer uses placeholder player text when the persisted history lacks a player turn. Repository history load failures now raise a distinct dependency exception after a structured `speculate.history_load_failed` diagnostic, while missing player history raises invalid-state diagnostics; both paths stop before the LLM transport can produce fabricated speculative output.
**Verification**: `C:\Users\decay\.dotnet\dotnet.exe test src\Pinder.GameApi.Tests\Pinder.GameApi.Tests.csproj --filter FullyQualifiedName~SpeculateSessionControllerTests -v:minimal` passed with 9 tests. `C:\Users\decay\.dotnet\dotnet.exe build src\Pinder.GameApi\Pinder.GameApi.csproj -v:minimal` passed with 0 warnings and 0 errors. `git diff --check` passed with line-ending normalization warnings only.


