### Finding 1: FastAPI GameApi proxy client/error handling is implemented twice
**Status**: Resolved
**Resolution**: Consolidated cached async clients, URL construction, forwarding, request headers, request-ID propagation, and downstream error translation into the shared gameapi_proxy module. Main and session services now use the same implementation, with only a compatibility subclass for the historical base-URL override. The integrated pinder-web commit is 2a74d372.
**Verification**: Six focused integration-branch tests passed, and the worker's broader Linux FastAPI proxy/session/operation/auth/stream suite passed 194 tests.

### Finding 2: Overlay degradation/refusal logic is copied across LLM overlay methods
**Status**: Resolved
**Resolution**: Extracted shared overlay rewrite normalization so horniness, trap, failure-corruption, and shadow-corruption paths use one empty-output/refusal policy and one degradation-event builder while preserving trap metadata. The integrated pinder-core commit is 513e3dd.
**Verification**: Thirteen focused overlay tests passed on the integration branch and on Linux; the worker also passed ten core LlmDispatcher tests.

### Finding 3: Markdown sanitization is duplicated in GameApi and Pinder.Core
**Status**: Resolved
**Resolution**: Removed the GameApi-local sanitizer and routed GameApi consumers through the canonical Pinder.Core markdown sanitizer. The integrated pinder-web commit is 7ef03a37.
**Verification**: The GameApi and GameApi test projects compile; the worker passed 32 GameApi sanitizer tests and 24 Pinder.Core sanitizer tests.

### Finding 5: Admin prompt API repeats authenticated JSON request plumbing
**Status**: Resolved
**Resolution**: Replaced the prompt editor's local request and error-handling helpers with the shared adminJsonFetch API client while preserving response validation and typed error behavior. The integrated pinder-web commit is 44708f86.
**Verification**: All six focused admin prompt API tests passed and the production frontend build succeeded.

### Finding 7: Core tests duplicate LLM adapter test doubles
**Status**: Resolved
**Resolution**: Extended the shared test-only StubLlmAdapter and migrated six test fixtures to it, removing repeated no-op implementations without changing production behavior. The integrated pinder-core commit is a40ab5f.
**Verification**: The test project compiles locally; the worker passed 90 affected tests on Windows and Linux.

### Finding 4: Data file discovery is duplicated between GameApi and session-runner
**Status**: Resolved
**Resolution**: Added the canonical Pinder.Core.Data.DataFileLocator with environment override, ancestor walking, and case-flex behavior; session-runner and GameApi now consume it, and the GameApi-local implementation was removed. Integrated commits are pinder-core 61db951 and pinder-web c668d9db, with core pinned by 3f82b670.
**Verification**: On the integrated branches, 22 core locator/prompt tests and four GameApi locator tests passed; both test projects compiled.

### Finding 6: Admin content editors repeat the same draft/save/reset UI shell
**Status**: Resolved
**Resolution**: Extracted shared AdminEditorState and AdminEditorShell modules and migrated all five identified content editors while keeping field-specific rendering local. The integrated pinder-web commit is 1955e6b7.
**Verification**: Fifteen focused editor tests passed, scoped lint passed, and the production frontend build succeeded.

### Finding 8: Prompt-related tests duplicate repo-subdirectory discovery
**Status**: Resolved
**Resolution**: Added shared TestRepoLocator.FindRepoSubdir helpers and replaced all six private prompt/session-setup lookup implementations. The integrated pinder-core commit is 880b3d5.
**Verification**: The solution compiled and 18 affected Linux tests passed; two existing prompt-content assertions remained unrelated failures after lookup succeeded.

### Finding 9: Text-layer noop diagnostic hashing is duplicated in two core helpers
**Status**: Resolved
**Resolution**: Extracted TextLayerNoopDiagnostics as the single internal emitter and stable-hash implementation, then routed dispatcher, delivery, and orchestrator paths through it. The integrated pinder-core commit is b753a07.
**Verification**: Thirteen focused diagnostic and dispatcher tests passed on the integrated branch, and the test project compiled.
