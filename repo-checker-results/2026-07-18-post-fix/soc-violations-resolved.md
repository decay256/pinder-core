# soc-violations resolutions

### Finding 1: Core kernel owns runtime filesystem discovery
**Status**: Resolved
**Resolution**: Runtime data-file discovery was moved out of the core kernel into the session setup layer, with the session runner delegating to that outer-layer implementation. Core no longer exposes the runtime filesystem locator while tests retain coverage for the locator contract through the outer layer.
**Verification**: `dotnet test tests\Pinder.Core.Tests\Pinder.Core.Tests.csproj --filter "FullyQualifiedName~ArchitectureRuleTests|FullyQualifiedName~DataFileLocatorTests|FullyQualifiedName~Issue840_SingleLoaderPipelineTests" --no-restore --verbosity minimal` passed within the 144-test focused core/session run.

### Finding 2: Core interface layer reflects into LlmAdapters for default rules
**Status**: Resolved
**Resolution**: The core default rule resolver no longer reflects into adapter assemblies. Core now owns a minimal default resolver for embedded progression and XP defaults, while the adapter package explicitly wires YAML-backed defaults when it is present, preserving the override contract without hardcoded reflection coupling.
**Verification**: `dotnet test tests\Pinder.Core.Tests\Pinder.Core.Tests.csproj --filter "FullyQualifiedName~ArchitectureRuleTests|FullyQualifiedName~LevelTable" --no-restore --verbosity minimal` passed within the 144-test focused core/session run.

### Finding 3: Core delivery retry policy knows HTTP exception types and wire error text
**Status**: Resolved
**Resolution**: DeliveryStage retry classification now depends only on TimeoutException and the Core-owned LlmTransportException abstraction with retryable RateLimited or Network classifications. OpenAI and Anthropic transports normalize provider HTTP and network failures into LlmTransportException so Core no longer inspects HTTP types, status text, or provider message substrings.
**Verification**: Focused Core tests passed: Issue788_SnapshotRoundTripTests, Issue907_TextingStyleConflictMatrixTests, EmotionStemSelectorTests, Issue1311_RestoreLlmDeliveryTests, Issue768_InterestBreakdownTests, and ComboGameSessionTests; 74 passed, 0 failed. Focused LLM adapter tests passed: AnthropicTransportTests, IssuePromptHardcodingHorninessQuestionTests, Issue1243_SuccessImprovementEnvelopeTests, and Issue1216_ExplicitOverlayFallbackTests; 36 passed, 0 failed.

### Finding 4: Session state controller assembles persisted/live state semantics itself
**Status**: Resolved
**Resolution**: Session state assembly now lives in a dedicated session state query service. The service owns the live-session versus persisted-state lookup, setup-status mapping, placement-state fallback, and resume-token construction, leaving the controller responsible only for invoking the service and translating null into a 404 response.
**Verification**: Focused GameApi service regression tests passed for persisted session-state assembly; `dotnet build Pinder.sln --no-restore` succeeded with 0 warnings and 0 errors.

### Finding 5: Character update controller bypasses repository/store ownership for definition loading
**Status**: Resolved
**Resolution**: Character definition update loading, identity preservation, payload merge, validation, serialization, and persistence now live in a character definition update service built on the repository contract. The controller no longer reads character files, probes filesystem paths, parses stored JSON, or constructs allocation dictionaries directly, so directory-backed and remote-store implementations share the same repository-owned path.
**Verification**: Focused GameApi controller tests passed for valid remote-store update compatibility, invalid item IDs, and invalid allocation keys; `dotnet build Pinder.sln --no-restore` succeeded with 0 warnings and 0 errors.

### Finding 6: FastAPI admin router reimplements GameApi proxy protocol inline
**Status**: Resolved
**Resolution**: The admin router delegates session debug, speculation, prompt trace, JSONL, token usage, temporary chat, narrative harness, and prompt compile proxy behavior to a focused `AdminProxyService` built on `GameApiClient`. Route handlers now retain request binding, admin authorization, and response return while GameApi protocol ownership lives in the service.
**Verification**: Focused admin proxy tests covering speculate, token usage, prompt trace, narrative harness, and related admin routes passed; full FastAPI pytest completed with 774 passed and only two unrelated Unity visual tests failing outside this backend scope.

### Finding 7: FastAPI admin prompt editor owns catalog parsing and YAML mutation in the route file
**Status**: Resolved
**Resolution**: Prompt catalog listing and single-prompt YAML mutation moved into `AdminPromptContentService`, which owns prompt file resolution, YAML parsing, prompt entry updates, staging/write semantics, and commit/reload response handling. The admin router now delegates `/api/admin/prompts`, `/api/admin/prompts/save`, and prompt compile behavior to focused services.
**Verification**: Focused admin prompt catalog/update/content tests passed, including staging gate coverage; full FastAPI pytest completed with 774 passed and only two unrelated Unity visual tests failing outside this backend scope.

### Finding 8: Admin operations UI builds backend artifact URLs directly
**Status**: Resolved
**Resolution**: Backend artifact URL construction for session audit, token usage, prompt-trace JSONL, and client-error artifacts now lives in `frontend/src/api/operations.ts` as named helper functions. `AdminOperationsPanel` consumes those helpers while retaining its UI-only responsibility for choosing which artifact links to show.
**Verification**: Focused Vitest suites passed: 8 files and 42 tests, including API helper coverage for audit, token usage, prompt JSONL, client-error, and operation export URLs plus AdminOperationsPanel link rendering. Changed-file ESLint passed. `npm run build` passed. The required `npm run lint` was executed and remains blocked by pre-existing repository-wide lint violations outside the changed files.

