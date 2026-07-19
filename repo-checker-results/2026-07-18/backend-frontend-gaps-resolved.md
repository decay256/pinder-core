### Finding 1: Narrative Testbed Calls a GameApi Reload Route That FastAPI Never Exposes
**Status**: Resolved
**Resolution**: Removed the direct call to the nonexistent reload route. NarrativeTestbed now saves through putAdminContent and surfaces the returned reload_status and reload_error, matching the established prompt-editor workflow. The integrated pinder-web commit is cb26d188.
**Verification**: Thirteen focused NarrativeTestbed tests and the strict frontend production build passed on the integration branch.

### Finding 2: GameApi Media Upload and Fetch Endpoints Are Hidden Behind the FastAPI /api Proxy
**Status**: Resolved
**Resolution**: Added authenticated FastAPI upload and fetch routes at the public /api/v1/media boundary. Uploads validate authentication, media type, non-empty bodies, and size before forwarding to GameApi, and returned URLs are rewritten to the public fetchable route. The integrated pinder-web commit is c3f00473.
**Verification**: Six FastAPI media-proxy tests and seven GameApi media tests passed on the integration branch; the version guard passed.

