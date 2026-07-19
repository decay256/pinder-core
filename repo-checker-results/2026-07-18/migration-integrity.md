# Repo-Checker Topic: migration-integrity

Repositories audited as one Pinder system:
- `A:\Data\ClaudeCodex\pinder-core`
- `A:\Data\ClaudeCodex\pinder-web`

Topic focus: divergent/sibling migration heads, irreversible migrations without documented reason, and ORM/schema drift. Default urgency: U1.

Summary counts: U1=1, U2=0, U3=0. Suppressions=0.

Head/reversibility notes:
- Alembic revision graph is linear with one head: `f136c3f3ada6`.
- The destructive `0008_datee_data_reset` migration is intentionally irreversible and documented in the migration docstring and runbook (`pinder-web/src/pinder-backend/alembic/versions/0008_datee_data_reset.py:60`, `pinder-web/docs/runbooks/881-datee-data-reset.md:147`), so it is not raised as an undocumented irreversible migration.
- Approved exception list reviewed: the four raw/internal error leakage exceptions do not match migration head, downgrade, or ORM/schema drift issues, so no findings were suppressed under those exceptions.

### Finding 1: Alembic metadata omits live C# persistence tables, so autogenerate proposes dropping them
**File**: `pinder-web/src/pinder-backend/alembic/env.py:33`
**Issue**: Alembic is configured with `target_metadata = Base.metadata`, but `pinder-web/src/pinder-backend/db/models.py` only declares SQLAlchemy tables through `PlayerProgressionDelta` (`__tablename__ = "player_progression_deltas"` at line 194) and has no SQLAlchemy models for `token_usages`, `operation_snapshots`, `operation_events`, `operation_idempotency_claims`, or `operation_retry_dispatches`. Those tables are real migrated schema: `0005_token_usages.py` creates `token_usages` at lines 20-31, and `f136c3f3ada6_add_operation_tracking.py` creates operation tables at lines 28, 78, 110, and 131. They are also live C# persistence models in `PinderDbContext` (`DbSet<TokenUsage>` and operation `DbSet`s at lines 33-39; mappings for `token_usages` at lines 108-121 and operation tables at lines 166-273). The architecture doc states that "Alembic owns their schema; EF maps and writes them" at `pinder-web/docs/architecture/operations.md:110`. I verified the drift by upgrading a scratch SQLite database to Alembic head with `src/pinder-backend/.venv/Scripts/python.exe` and running Alembic `compare_metadata` against `Base.metadata`; the generated diffs included `remove_table` for `token_usages`, `operation_snapshots`, `operation_events`, `operation_idempotency_claims`, and `operation_retry_dispatches`.
**Impact**: The next Alembic autogenerate/check flow can treat production tables written by GameApi as schema garbage and generate a destructive migration that drops token/cost history plus operation snapshots, events, idempotency claims, retry dispatches, leases, and failure/debug state. That is direct persistence corruption in a shared Python/C# database boundary.
**Urgency**: U1 - topic default; accepting the generated drift would drop live production persistence tables.
**Fixer-Agent Action Plan**: Add SQLAlchemy models or explicit Alembic metadata table declarations for `token_usages` and the four operation tables so `Base.metadata` matches Alembic head. Then add a migration drift test that upgrades a scratch database to `head`, runs `compare_metadata` against `Base.metadata`, and fails on any `remove_table`/`remove_index` diff for managed tables. Extend `test_db_migrations.py` to assert the full current table set, not only the older core/progression tables, and keep the EF mapping tests as a separate read/write contract check.
