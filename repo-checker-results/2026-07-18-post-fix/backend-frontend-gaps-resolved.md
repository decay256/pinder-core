# backend-frontend-gaps resolutions

### Finding 1: Existing-character smart randomize is wired in React and GameApi but missing from the FastAPI proxy
**Status**: Resolved
**Resolution**: FastAPI now exposes the admin-only `POST /api/characters/{slug}/randomize` proxy, forwards to the existing GameApi randomize route with the shared secret headers, preserves timeout and upstream error behavior, and documents the route in the backend API contract. The existing React API client route already matched this backend shape, so no additional frontend page/component edits were needed.
**Verification**: `tests/test_characters_randomize.py` passed and verifies admin gating, GameApi forwarding, and generic 502 detail without leaking low-level exception text; `tests/test_openapi.py` passed and verifies the contract route; `npm test -- src/api/characterMutations.test.ts` passed with 6 tests.

### Finding 2: Backend OpenAPI still advertises removed setup-status endpoints
**Status**: Resolved
**Resolution**: The stale setup-status polling and stream paths, along with the obsolete `SetupStatus` schema, were removed from `contracts/backend-api.yaml`. Backend OpenAPI regression coverage now asserts those endpoints and schema remain absent.
**Verification**: `tests/test_openapi.py` passed; `rg "setup-status|SetupStatus" contracts/backend-api.yaml src/pinder-backend -g "*.py" -g "*.yaml"` found only regression-test assertions and historical comments, not active contract or route definitions.

