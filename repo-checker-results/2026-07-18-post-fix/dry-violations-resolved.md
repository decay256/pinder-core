# dry-violations resolutions

### Finding 1: Single-text LLM response normalization is duplicated outside the overlay helper
**Status**: Resolved
**Resolution**: PinderLlmAdapter now routes interest beats, success improvements, steering questions, and horniness questions through a shared single-text normalization helper that handles trimming, quote removal, empty output telemetry, and ellipsis rejection where applicable.
**Verification**: Focused LLM adapter tests passed: AnthropicTransportTests, IssuePromptHardcodingHorninessQuestionTests, Issue1243_SuccessImprovementEnvelopeTests, and Issue1216_ExplicitOverlayFallbackTests; 36 passed, 0 failed.

### Finding 2: Operational diagnostic terminal emission is copied across conversation stages
**Status**: Resolved
**Resolution**: Terminal diagnostic construction is centralized behind OperationalDiagnostics helpers for succeeded, cancelled, and failed terminal events. DateeResponseStage and DeliveryStage now call the shared helpers while preserving operation kind, phase, call id, failure classification, and turn correlation hints.
**Verification**: Focused Core tests passed: Issue788_SnapshotRoundTripTests, Issue907_TextingStyleConflictMatrixTests, EmotionStemSelectorTests, Issue1311_RestoreLlmDeliveryTests, Issue768_InterestBreakdownTests, and ComboGameSessionTests; 74 passed, 0 failed.

### Finding 3: Core tests duplicate ILlmAdapter no-op boilerplate despite shared stubs
**Status**: Resolved
**Resolution**: ComboGameSessionTests now uses the shared StubLlmAdapter test double and keeps only the option-queue behavior needed by the combo scenarios. Local duplicate ILlmAdapter no-op methods were removed from the test fixture.
**Verification**: Focused Core tests passed: Issue788_SnapshotRoundTripTests, Issue907_TextingStyleConflictMatrixTests, EmotionStemSelectorTests, Issue1311_RestoreLlmDeliveryTests, Issue768_InterestBreakdownTests, and ComboGameSessionTests; 74 passed, 0 failed.

### Finding 4: A core test file reimplements repository/data path discovery already in TestRepoLocator
**Status**: Resolved
**Resolution**: The duplicated data discovery logic was removed from the affected core tests. Tests now use the established shared test locator/helper for repository-root and data-file lookup, preserving behavior while centralizing path discovery.
**Verification**: `dotnet test tests\Pinder.Core.Tests\Pinder.Core.Tests.csproj --filter "FullyQualifiedName~LevelTable|FullyQualifiedName~CharacterDefinitionLoaderTests|FullyQualifiedName~Issue1176_RealUnityItemsTests|FullyQualifiedName~ArchitectureRuleTests|FullyQualifiedName~DataFileLocatorTests|FullyQualifiedName~Issue840_SingleLoaderPipelineTests|FullyQualifiedName~Issue843_PromptCatalogPhase1Tests|FullyQualifiedName~Issue1223_SetupGeneratorObservableFailureTests|FullyQualifiedName~CharacterSystemTests|FullyQualifiedName~Issue836_TextingStyleAggregationRuleTests" --no-restore --verbosity minimal` passed with 144 tests.

### Finding 5: TurnResult test fixtures are copied across frontend component and e2e tests
**Status**: Resolved
**Resolution**: The targeted TurnResultDisplay component suites and the message timeline e2e fixture now build payloads through the shared `turnResultBuilder` helpers, keeping only test-specific roll, state, text, and breakdown overrides in each file. The new terminal-fetch regression test also uses the shared TurnResult builder so future contract expansion remains centralized.
**Verification**: Focused Vitest suites passed: 8 files and 42 tests covering the migrated TurnResultDisplay suites plus related frontend behavior. Changed-file ESLint passed, including the migrated component and e2e fixture files. `npm run build` passed. The required `npm run lint` was executed and remains blocked by pre-existing repository-wide lint violations outside the changed files.

### Finding 6: FastAPI admin tests repeat auth override and cleanup fixtures
**Status**: Resolved
**Resolution**: Admin FastAPI tests now use a shared auth override helper and centralized autouse cleanup instead of per-file `_override_user` and cleanup fixture copies. The shared helper also handles tests that reload `auth` or `main` by applying overrides to every mounted `require_auth` dependency key across known FastAPI app instances.
**Verification**: The focused admin/backend pytest set passed with 223 tests; `tests/test_prompt_staging_gate.py` and `tests/test_db_migrations.py::test_proxy_endpoint_uses_db_backed_ownership` passed together; `rg "def _override_user|def _clear_overrides_between_tests|_override_user\(" src/pinder-backend/tests -g "*.py"` found no remaining duplicate helper definitions or calls.

### Finding 7: Prompt editors duplicate custom save/warning state outside the shared admin editor hook
**Status**: Resolved
**Resolution**: `useSavePipeline` and `useAdminDraftEditor` now support save-success response handling and reload-warning state, while `AdminEditorSaveControls` owns the shared warning slot alongside saved and error states. `PromptsEditorInner` and `DramaticArcPromptEditor` now use the shared draft editor hook and save controls, preserving prompt-specific fields and reload-warning UX. The prompt payload type guard was moved into a small model module so the editor component file remains compatible with Fast Refresh rules.
**Verification**: Focused Vitest suites passed: 8 files and 42 tests, including `AdminPage.promptsEditor.test.tsx` and `DramaticArcPromptEditor.test.tsx` for normal saves, save errors, and reload warnings. Changed-file ESLint passed. `npm run build` passed. The required `npm run lint` was executed and remains blocked by pre-existing repository-wide lint violations outside the changed files.

