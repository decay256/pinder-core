# Repo-Checker Topic: performance-pathologies

Repositories audited:
- `A:\Data\ClaudeCodex\pinder-core`
- `A:\Data\ClaudeCodex\pinder-web`

Scope: performance pathologies across the combined Pinder system, including N+1 behavior, unbounded queries, quadratic hot paths, hot-path client construction, and adjacent concrete performance hazards.

Suppression notes:
- Loaded the four approved exceptions for raw upstream/C# exception and internal error leakage. None overlap this performance-pathologies audit.
- Suppression count: 0.

### Finding 1: FastAPI character and catalog proxy routes create a new HTTPX client per request
**File**: `pinder-web/src/pinder-backend/routes/characters.py:57`
**Issue**: The public/authenticated character and content proxy routes construct and dispose `httpx.AsyncClient` inside each request handler instead of reusing the existing shared-client lifecycle. Examples include `/api/characters` at `routes/characters.py:57`, `/api/characters/{slug}` at `routes/characters.py:131`, `/api/characters/generate-random` at `routes/characters.py:189`, `/api/models` at `routes/characters.py:306`, `/api/items` at `routes/characters.py:463`, and `/api/anatomy` at `routes/characters.py:509`. The app already has a reusable client facility in `pinder-web/src/pinder-backend/main.py:436` through `pinder-web/src/pinder-backend/main.py:459`, and the session stream path uses the equivalent shared-client pattern in `pinder-web/src/pinder-backend/session_services.py:150` through `pinder-web/src/pinder-backend/session_services.py:156`.
**Impact**: These endpoints sit on character selection, setup, item/anatomy browsing, and random generation flows. Creating a new async client per request discards connection pooling and forces avoidable socket/TLS churn, increasing latency and file-descriptor/ephemeral-port pressure under concurrent browsing or play. The existing session-service pattern shows the codebase already treats shared client reuse as the desired hot-path behavior.
**Urgency**: U2 - topic default. This is a user-facing hot path with concrete per-request client construction and an established lower-overhead pattern available in the same service.
**Fixer-Agent Action Plan**: Move the character/catalog proxy routes onto a shared `httpx.AsyncClient` provider keyed by timeout/follow-redirects needs, reusing the app lifespan shutdown path in `main.py`. Add regression tests for `/api/characters`, `/api/items`, `/api/anatomy`, and one write/generate route that monkeypatch the shared provider and assert the route no longer constructs `httpx.AsyncClient` directly per request.

### Finding 2: Remote character asset startup performs full-catalog N+1 payload hydration
**File**: `pinder-web/src/Pinder.GameApi/Services/CharacterRepository.cs:155`
**Issue**: When remote character assets are enabled, the GameApi startup/load path first lists every remote asset id and then sequentially loads each character payload one-by-one. `CharacterRepository` starts the load task at `pinder-web/src/Pinder.GameApi/Services/CharacterRepository.cs:132`, obtains all ids at `pinder-web/src/Pinder.GameApi/Services/CharacterRepository.cs:140` through `pinder-web/src/Pinder.GameApi/Services/CharacterRepository.cs:147`, and then loops through all ids with `await _store.LoadAsync(id, ...)` at `pinder-web/src/Pinder.GameApi/Services/CharacterRepository.cs:155` through `pinder-web/src/Pinder.GameApi/Services/CharacterRepository.cs:161`. In remote mode, `QueryBackedRemoteCharacterStore.ListIdsAsync` pages metadata at `pinder-web/src/Pinder.GameApi/Services/CharacterStoreFactory.cs:72` through `pinder-web/src/Pinder.GameApi/Services/CharacterStoreFactory.cs:98`, with a max page size of 500 from `pinder-core/src/Pinder.Core/Characters/CharacterAssetQuery.cs:18`. Each metadata page is one HTTP request in `pinder-core/src/Pinder.RemoteAssets/EigencoreCharacterStoreRead.cs:135` through `pinder-core/src/Pinder.RemoteAssets/EigencoreCharacterStoreRead.cs:168`, and each `LoadAsync` payload fetch is another HTTP request in `pinder-core/src/Pinder.RemoteAssets/EigencoreCharacterStoreRead.cs:220` through `pinder-core/src/Pinder.RemoteAssets/EigencoreCharacterStoreRead.cs:244`.
**Impact**: Remote-asset mode scales as `ceil(N/500) + N` upstream HTTP calls before the in-memory repository is ready, and the payload loads are sequential. As the character catalog grows, GameApi readiness and first-use latency become proportional to the entire remote catalog, and a single service start can hammer the Eigencore asset service with one request per character. Existing tests cover paged id enumeration in `pinder-web/src/Pinder.GameApi.Tests/Services/CharacterStoreFactoryTests.cs:33` through `pinder-web/src/Pinder.GameApi.Tests/Services/CharacterStoreFactoryTests.cs:64`, but they do not assert a bounded, batched, lazy, or concurrent payload-load behavior.
**Urgency**: U2 - topic default. This is a concrete N+1 remote fetch pattern on a production opt-in startup/readiness path.
**Fixer-Agent Action Plan**: Replace full sequential hydration with a bounded strategy: either return list-ready DTO fields from the remote query page, batch-fetch payloads, or lazy-load full definitions on detail/use. If full hydration remains required, use bounded concurrency with backpressure and a configured maximum catalog size. Add integration tests with more than one page of fake remote assets that assert startup performs bounded or batched work and does not issue one sequential payload request per id for list-only readiness.

### Finding 3: Admin operation detail and export materialize unbounded event histories
**File**: `pinder-web/src/Pinder.GameApi/Data/OperationRepository.cs:83`
**Issue**: `OperationRepository.ListEventsAsync` loads every event for an operation with `Where(e => e.OperationId == operationId).OrderBy(e => e.Sequence).ToListAsync(ct)` at `pinder-web/src/Pinder.GameApi/Data/OperationRepository.cs:83` through `pinder-web/src/Pinder.GameApi/Data/OperationRepository.cs:96`. The same repository already has a bounded cursor API, `ListEventPageAsync`, which clamps `limit` to 1..500 at `pinder-web/src/Pinder.GameApi/Data/OperationRepository.cs:108` and applies `.Take(boundedLimit)` at `pinder-web/src/Pinder.GameApi/Data/OperationRepository.cs:114`. The bounded API is used by owner/admin timeline endpoints, but admin detail still calls the unbounded method at `pinder-web/src/Pinder.GameApi/Controllers/OperationsController.cs:233`, and admin export calls it at `pinder-web/src/Pinder.GameApi/Controllers/OperationsController.cs:277`. `OperationDtos.ToAdminDetail` then materializes the complete event list into the response array at `pinder-web/src/Pinder.GameApi/Models/OperationDtos.cs:325` through `pinder-web/src/Pinder.GameApi/Models/OperationDtos.cs:343`.
**Impact**: Long-running LLM/setup operations can accumulate large event histories. The detail endpoint can allocate and serialize all rows into one JSON response, while export loads all rows before producing NDJSON. That can pin memory, slow admin diagnostics, and compete with gameplay traffic even though the codebase already has a paged timeline shape for the same data. Tests cover clamped timeline paging in `pinder-web/src/Pinder.GameApi.Tests/Controllers/OperationControllerTests.cs:171` through `pinder-web/src/Pinder.GameApi.Tests/Controllers/OperationControllerTests.cs:183`, while the export test at `pinder-web/src/Pinder.GameApi.Tests/Controllers/OperationControllerTests.cs:201` through `pinder-web/src/Pinder.GameApi.Tests/Controllers/OperationControllerTests.cs:216` only proves all three fixture rows are returned and does not guard large histories.
**Urgency**: U2 - topic default. The endpoint is admin-scoped, but it is an unbounded database materialization path for operational data that is expected to grow with retries, streaming progress, and audit logging.
**Fixer-Agent Action Plan**: Keep admin detail bounded by returning the first event page plus a next cursor, or omit inline events and require the existing timeline endpoint. Change export to stream from the database in sequence order or require explicit cursor/range windows instead of `ToListAsync` over all rows. Add tests with more than 500 events asserting detail remains bounded and export either streams/pages without full pre-materialization or rejects unbounded export requests.

## Inspected Non-Findings

- Session list endpoints are capped in repository queries and use in-memory character-id resolution after repository load, so no request-time database N+1 was raised for session listings.
- Operation list and timeline endpoints already use bounded page sizes; only admin detail/export still use the unbounded event-list path.
- Media proxy code uses `IHttpClientFactory`; no hot-path `new HttpClient` finding was raised there.

## Counts

- U1: 0
- U2: 3
- U3: 0
- Suppressions: 0
