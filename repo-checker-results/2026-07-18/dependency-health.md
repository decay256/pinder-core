# Repo-Checker Topic Report: dependency-health

Repositories audited as one Pinder system:
- `A:\Data\ClaudeCodex\pinder-core`
- `A:\Data\ClaudeCodex\pinder-web`

Inspected dependency manifests, lockfiles, Docker install/restore paths, and direct import evidence for NuGet, npm, and Python requirements. External checks run read-only: `npm audit --json` in `pinder-web/frontend`, `dotnet list ... package --vulnerable --include-transitive --no-restore` for `pinder-core/Pinder.Core.sln` and `pinder-web/Pinder.sln`, and OSV queries for pinned Python requirements where no local `pip-audit` module was available.

### Finding 1: Frontend lockfile contains known-vulnerable npm packages
**File**: `pinder-web/frontend/package-lock.json:5227`
**Issue**: `package-lock.json` resolves `react-router-dom` to `7.14.1` while `package.json` declares `"react-router-dom": "^7.14.1"` at `pinder-web/frontend/package.json:32`. `npm audit --json` reports high-severity advisories for `react-router`/`react-router-dom` at this locked version, and the same audit reports additional vulnerable locked packages including `vite@8.0.8` (`pinder-web/frontend/package-lock.json:5989`), `js-yaml@4.1.1` (`pinder-web/frontend/package-lock.json:3458`), `monaco-editor@0.55.1` pulling `dompurify@3.2.7` (`pinder-web/frontend/package-lock.json:4809`, `pinder-web/frontend/package-lock.json:2715`), plus transitive `undici`, `@babel/core`, and `brace-expansion`.
**Impact**: The browser/admin frontend is shipping from a lockfile with 9 current npm advisories, including high-severity direct dependency entries. Even when some advisories are dev-server or framework-mode specific, the repo has no suppression/override record explaining why these vulnerable resolutions are acceptable.
**Urgency**: U2 - topic default; vulnerable packages are present in a committed lockfile and include direct frontend dependencies, but the highest-risk advisories need framework/production-exposure triage before treating them as immediate production compromise.
**Fixer-Agent Action Plan**: Run `npm audit` from `pinder-web/frontend`, upgrade or override the vulnerable packages to patched versions, verify `npm ci`, `npm run build`, and `npm test`, and record any remaining advisory-specific non-applicability in the accepted exceptions file rather than leaving raw audit noise in the lockfile.

### Finding 2: Python backend production install is driven by ranged and unpinned requirements
**File**: `pinder-web/src/pinder-backend/requirements.txt:5`
**Issue**: Production requirements mix broad ranges and a bare unpinned package: `PyJWT>=2.8.0` at line 5, `cryptography` with no version constraint at line 6, `PyYAML>=6.0,<7` at line 11, `ruamel.yaml>=0.18,<1` at line 21, and database/runtime dependencies such as `sqlalchemy>=2.0,<3`, `alembic>=1.13,<2`, `asyncpg>=0.29,<1`, and `psycopg2-binary>=2.9,<3` at lines 38-41. The runtime Dockerfile then executes `pip install --no-cache-dir --prefix=/deps -r requirements.txt` at `pinder-web/src/pinder-backend/Dockerfile:24`; only `requirements.txt` and `requirements-dev.txt` exist in the backend folder, with no `poetry.lock`, `uv.lock`, `Pipfile.lock`, or equivalent constraints file found.
**Impact**: Rebuilding the same backend image on different days can silently resolve different auth, crypto, database, and YAML parser code. That makes incident replay, staging/prod parity, and vulnerability remediation harder because the deployed dependency set is not reproducible from repository bytes.
**Urgency**: U2 - topic default; this is production install drift affecting auth/crypto/database packages, but no known vulnerable resolved production Python package was confirmed from the pinned exact-version OSV queries run in this audit.
**Fixer-Agent Action Plan**: Introduce a generated constraints/lock workflow for the Python backend, pin `cryptography` and the ranged production dependencies to reviewed versions, update Docker to install from the locked/constraints file, and document the refresh command used for vulnerability updates.

### Finding 3: GameApi production NuGet references float patch versions without lock-mode restore
**File**: `pinder-web/src/Pinder.GameApi/Pinder.GameApi.csproj:14`
**Issue**: The production GameApi project declares floating patch references: `<PackageReference Include="Npgsql" Version="8.0.*" />` at line 14, `<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="8.0.*" />` at line 15, and `<PackageReference Include="Microsoft.EntityFrameworkCore" Version="8.0.*" />` at line 16. The GameApi Dockerfile restores with `dotnet restore src/Pinder.GameApi/Pinder.GameApi.csproj` at `pinder-web/src/Pinder.GameApi/Dockerfile:28`, and a recursive search found no `packages.lock.json` under either audited repo.
**Impact**: Every container rebuild is allowed to pick newer Npgsql/EF Core patch releases without a reviewed commit. Because GameApi persistence and operation tracking directly use EF Core and Npgsql, patch-level behavior changes can land in staging/prod outside normal code review.
**Urgency**: U2 - topic default; this is a production dependency reproducibility problem on the database path.
**Fixer-Agent Action Plan**: Replace `8.0.*` with explicit reviewed versions or enable NuGet lock files with locked-mode restore for deploy builds; commit the resulting lockfiles and rerun GameApi persistence/operation tests against the locked graph.

### Finding 4: Core test projects resolve vulnerable transitive NuGet packages
**File**: `pinder-core/tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj:13`
**Issue**: Four core test projects pin an old test stack, for example `Microsoft.NET.Test.Sdk` `17.6.0`, `xunit` `2.4.2`, `xunit.runner.visualstudio` `2.4.5`, and `coverlet.collector` `6.0.0` in `Pinder.Core.Tests` at lines 13-19; the same versions appear in `Pinder.LlmAdapters.Tests` at lines 14-21, `Pinder.Rules.Tests` at lines 12-18, and `Pinder.RemoteAssets.Tests` at lines 13-19. `dotnet list A:\Data\ClaudeCodex\pinder-core\Pinder.Core.sln package --vulnerable --include-transitive --no-restore` reports high-severity transitive `System.Net.Http 4.3.0` (GHSA-7jgj-8wvc-jh57) and `System.Text.RegularExpressions 4.3.0` (GHSA-cmhx-cq75-c4mj) for those test projects.
**Impact**: Test-only exposure reduces production risk, but the standard local validation path still restores known-vulnerable assemblies. It also leaves future dependency updates noisier because vulnerability tooling will continue to fail on the test graph.
**Urgency**: U3 - de-escalated from U2 because the confirmed vulnerable NuGet graph is limited to test projects, not production packages.
**Fixer-Agent Action Plan**: Upgrade the test package stack across the four projects, regenerate restore assets, rerun `dotnet list package --vulnerable --include-transitive --no-restore`, and then run the relevant core test suites.

### Finding 5: React Router v5 type package is declared alongside React Router v7 runtime
**File**: `pinder-web/frontend/package.json:27`
**Issue**: The frontend declares `"@types/react-router-dom": "^5.3.3"` at line 27 while declaring `"react-router-dom": "^7.14.1"` at line 32. The lockfile confirms `@types/react-router-dom` resolves to `5.3.3` and pulls `@types/react-router` at `pinder-web/frontend/package-lock.json:1694-1702`, while `react-router-dom` resolves to `7.14.1` at `pinder-web/frontend/package-lock.json:5225-5227`. `tsconfig.app.json` also has `"skipLibCheck": true` at `pinder-web/frontend/tsconfig.app.json:8`, which can hide library type conflicts.
**Impact**: React Router v7 ships its own types; keeping the old DefinitelyTyped v5 package creates a stale/conflicting type surface and an unnecessary production dependency entry. Future route API changes can be hidden or misrepresented by the obsolete type package.
**Urgency**: U2 - topic default; this is an active direct dependency conflict in the frontend manifest.
**Fixer-Agent Action Plan**: Remove `@types/react-router-dom` from `dependencies`, regenerate `package-lock.json`, run TypeScript/build/tests, and only re-add route typings if a current package actually requires them.

### Finding 6: Backend sourcemap parser is an explicitly stale production dependency
**File**: `pinder-web/src/pinder-backend/requirements.txt:34`
**Issue**: The backend pins `sourcemap==0.2.1` at line 34, with adjacent comments recording that its last PyPI release is `0.2.1 (2017)` at line 31. Production code imports it directly in `pinder-web/src/pinder-backend/client_error_sourcemap.py:50` via `import sourcemap  # type: ignore[import-untyped]`.
**Impact**: The dependency is unmaintained and untyped on a production diagnostics path that parses client error source maps. Even without a confirmed current OSV vulnerability for `sourcemap==0.2.1`, stale parser code increases the chance that future source map formats or malformed inputs fail in surprising ways.
**Urgency**: U3 - de-escalated from U2 because the stale dependency is documented in-place and OSV returned no known vulnerability for the pinned version during this audit.
**Fixer-Agent Action Plan**: Evaluate maintained source-map parser alternatives or isolate this dependency behind a narrow adapter with malformed-input tests; if the team intentionally accepts the stale package, move the rationale into the accepted exceptions process with an owner and review date.

### Finding 7: Python dev requirements pin a vulnerable pytest release
**File**: `pinder-web/src/pinder-backend/requirements-dev.txt:1`
**Issue**: The backend dev requirements pin `pytest==8.3.5` at line 1. OSV reports `GHSA-6w46-j5rx-g56g` / `PYSEC-2026-1845` for pytest before `9.0.3`, summarized as vulnerable tmpdir handling.
**Impact**: This is test/dev-only, but local and CI-like validation can run with a known-vulnerable test runner. It also means Python vulnerability checks will fail even if production requirements are clean.
**Urgency**: U3 - de-escalated from U2 because the vulnerable package is only in `requirements-dev.txt`.
**Fixer-Agent Action Plan**: Upgrade pytest to a patched release compatible with the backend tests, rerun the Python backend test suite, and add Python dependency vulnerability checking to the same refresh process used for production requirements.

Suppression notes:
- Loaded 4 approved exception bullets from the user request and `A:\Data\Obsidian\Eigen\Pinder Design\Audit\Currently acceptable exceptions.md`.
- 0 dependency-health findings were suppressed; the approved exceptions concern error leakage patterns rather than manifests, lockfiles, or dependency graphs.

Summary counts:
- U1: 0
- U2: 4
- U3: 3
- Suppressed findings: 0
