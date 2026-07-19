### Finding 1: Deploy Template Ships Active Weak Secret Defaults
**File**: `pinder-web/.env.template:43`
**Issue**: The deploy template contains active, copyable credential assignments instead of empty/placeholder-commented values: `SECRET_KEY=changeme` at line 43, `GAMEAPI_SHARED_SECRET=change-me-before-deploy` at line 67, and `PINDER_DATABASE_URL=postgresql+asyncpg://pinder:change-me@127.0.0.1:5432/pinder` at line 189. The same template instructs operators to `cp .env.template .env`, and both FastAPI/GameApi only require these values to be non-empty, so the weak `SECRET_KEY` and shared secret can pass startup checks if copied unchanged.
**Impact**: A deployed or staging environment that keeps these defaults would have guessable session/JWT signing material and a guessable FastAPI -> GameApi shared secret, allowing forged sessions or direct internal API calls wherever the network boundary is reachable. The database URL also normalizes a credential-bearing pattern into the committed template.
**Urgency**: U1 - topic default; the values are active assignments for authentication/secrets in the deployment template, not inert documentation.
**Fixer-Agent Action Plan**: Change secret-bearing entries in `.env.template` to empty values or commented examples that fail startup when copied unchanged; add startup/config validation rejecting known placeholders such as `changeme`, `change-me-before-deploy`, and `change-me`; add tests covering `SECRET_KEY`, `GAMEAPI_SHARED_SECRET`, and `PINDER_DATABASE_URL` placeholder rejection in both FastAPI and GameApi config paths.

Suppressed by exception: Unsafe Leakage of Raw C# Exception Messages (ex.Message) in public GameApi endpoints - would be U1. Encountered committed test-output evidence at `pinder-web/test_output.txt:128` showing a raw `Pinder.Core.Interfaces.LlmTransportException` stack for public `POST /sessions/{id}/turn`; not raised because the pattern is explicitly approved.

Suppressed by exception: Exposure of Internal low-level Library Exceptions and Socket Errors to End-Users - would be U1. Encountered committed test-output evidence at `pinder-web/test_output.txt:1939` showing `Pinder.LlmAdapters.Anthropic.AnthropicApiException` provider-body text and stack frames; not raised because the pattern is explicitly approved.

Suppressed by exception: Direct Leakage of Internal System Errors and File Paths in Admin Controller Error Payloads - would be U1. Encountered committed test-output evidence at `pinder-web/test_output.txt:286` showing `System.Text.Json.JsonReaderException` with `/root/projects/pinder-web/.../SessionsController.Queries.cs:line 293`; not raised because the pattern is explicitly approved.

Counts: U1=1, U2=0, U3=0.
Mirror hash verification: identical SHA256 confirmed after write.
