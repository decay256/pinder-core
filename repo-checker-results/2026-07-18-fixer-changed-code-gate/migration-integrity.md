> Scope: pinder-core 7962415f750de354b53d4f9b953eaa3e37b3575b..5dd4274b82e0e2d1c78471909341323eafb84856 (91 existing changed files; no migration/DB schema files in scope); pinder-web c7a465bb67fb86ee6b5ab1105955b7c0717eddca..3d5d279c520b596560368b0d2afaaa650e270d68 (88 existing changed files; migration-related changed files inspected: `src/pinder-backend/db/__init__.py`, `src/pinder-backend/db/models.py`, `src/pinder-backend/tests/test_db_migrations.py`). Topic 21 migration-integrity.

# Repo-Checker Topic: migration-integrity

Repositories audited as one Pinder system:
- `A:\Data\ClaudeCodex\pinder-core`
- `A:\Data\ClaudeCodex\pinder-web`

Topic focus: divergent/sibling migration heads, irreversible migrations without documented reason, and ORM-model-vs-schema drift. Default urgency: U1.

Summary counts: U1=0, U2=0, U3=0. Suppressions=0.

No concrete migration-integrity findings remain in the scoped post-fixer changed files.

Verification notes:
- Prior report read and not duplicated: the stale U1 in `2026-07-18-fixer-changed-code-gate/migration-integrity.md` said Alembic metadata/tests omitted `token_usages` and `operation_*` tables.
- The final range now adds authoritative SQLAlchemy metadata for `TokenUsage`, `OperationSnapshot`, `OperationEvent`, `OperationIdempotencyClaim`, and `OperationRetryDispatch` in `pinder-web/src/pinder-backend/db/models.py`, and exports those models from `pinder-web/src/pinder-backend/db/__init__.py`.
- `pinder-web/src/pinder-backend/tests/test_db_migrations.py` now imports those models, asserts the complete `Base.metadata.tables` set includes `token_usages` plus all four `operation_*` tables, asserts `alembic upgrade head` creates exactly the metadata table set, asserts a single Alembic head `f136c3f3ada6`, and checks operation-table columns/indexes against model metadata.
- Independent verification upgraded a scratch SQLite database to Alembic head and ran Alembic `compare_metadata` against `Base.metadata`; migrated tables matched metadata tables and `diff_count=0`.
- Targeted test run passed: `.\.venv\Scripts\python.exe -m pytest tests/test_db_migrations.py -q` -> `12 passed`.
