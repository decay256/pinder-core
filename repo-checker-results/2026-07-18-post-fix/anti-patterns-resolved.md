# anti-patterns resolutions

### Finding 1: Persisted conversation history restore silently drops invalid message roles
**Status**: Resolved
**Resolution**: Persisted datee and avatar conversation histories now restore through a shared fail-fast helper. Empty or unsupported roles raise an InvalidOperationException with the affected history kind, entry index, and role instead of silently dropping malformed messages.
**Verification**: Focused Core tests passed: Issue788_SnapshotRoundTripTests, Issue907_TextingStyleConflictMatrixTests, EmotionStemSelectorTests, Issue1311_RestoreLlmDeliveryTests, Issue768_InterestBreakdownTests, and ComboGameSessionTests; 74 passed, 0 failed.

### Finding 2: Missing progression threshold keys are treated as the end of the level table
**Status**: Resolved
**Resolution**: Level progression lookup now distinguishes intentional end-of-table from malformed configuration. Resolvers return null only when the requested level is beyond the configured progression table, while missing in-range thresholds raise a configuration error instead of being interpreted as a clean termination.
**Verification**: `dotnet test tests\Pinder.Rules.Tests\Pinder.Rules.Tests.csproj --no-restore --verbosity minimal` passed with 264 tests. `dotnet test tests\Pinder.Core.Tests\Pinder.Core.Tests.csproj --filter "FullyQualifiedName~LevelTable|FullyQualifiedName~CharacterDefinitionLoaderTests|FullyQualifiedName~Issue1176_RealUnityItemsTests|FullyQualifiedName~ArchitectureRuleTests|FullyQualifiedName~DataFileLocatorTests|FullyQualifiedName~Issue840_SingleLoaderPipelineTests|FullyQualifiedName~Issue843_PromptCatalogPhase1Tests|FullyQualifiedName~Issue1223_SetupGeneratorObservableFailureTests|FullyQualifiedName~CharacterSystemTests|FullyQualifiedName~Issue836_TextingStyleAggregationRuleTests" --no-restore --verbosity minimal` passed with 144 tests. `python rules\tools\generate_tests.py --check` passed, and `PYTHONUTF8=1 python rules\tools\rules_pipeline.py check` passed with acceptable round-trip drift.

### Finding 3: Replay audit JSON parse failures disappear as missing turn data
**Status**: Resolved
**Resolution**: Replay audit mapping now distinguishes legacy-missing payload data from malformed persisted payloads. Empty payloads still project as absent for compatibility, while invalid JSON or non-object audit roots raise a typed replay audit payload exception that the existing replay controller catch paths log with session and turn context before rendering a degraded entry.
**Verification**: Focused GameApi regression tests passed for malformed replay mapper failures and legacy-missing compatibility; `dotnet build Pinder.sln --no-restore` succeeded with 0 warnings and 0 errors.

### Finding 4: GameScreen silently ignores terminal turn fetch failures
**Status**: Resolved
**Resolution**: The terminal end-state fetch path in `GameScreen` now routes rejected `getTurn` calls through the existing client error reporter with session and bounded-attempt context, then sets the existing game-screen load error state so the failure reaches the established error UI instead of being silently swallowed. The prior single delayed retry for an end-state payload missing a cost summary remains bounded to one retry, while actual fetch failures are recorded immediately.
**Verification**: Focused Vitest suites passed: 8 files and 42 tests covering `GameScreen`, operations API helpers, prompt editors, and TurnResultDisplay fixture reuse. Changed-file ESLint passed. `npm run build` passed. The required `npm run lint` was executed and remains blocked by pre-existing repository-wide lint violations outside the changed files.

### Finding 5: Backend version probe hides GameApi dependency failures
**Status**: Resolved
**Resolution**: The FastAPI version endpoint now reports an explicit GameApi version probe status alongside the version fields and logs upstream status, request, JSON, and unexpected failures with safe context including request id, URL, duration, and failure kind. Successful probes return `game_api_version_status: ok`; failures remain non-fatal for the metadata endpoint but are observable to operators.
**Verification**: `python -m compileall .` passed in `src/pinder-backend`; focused FastAPI tests including `tests/test_version.py` passed; full FastAPI pytest completed with 774 passed and only two unrelated Unity visual tests failing outside this backend scope.

### Finding 6: Texting-style conflict YAML is parsed with substring state machines
**Status**: Resolved
**Resolution**: TextingStyleConflicts now uses the established YamlDotNet structured YAML stack with underscored naming, DTO binding, unmatched-property tolerance, axis validation, and explicit malformed-YAML errors. The former substring parser and inline flow-map parsing were removed.
**Verification**: Focused Core tests passed: Issue788_SnapshotRoundTripTests, Issue907_TextingStyleConflictMatrixTests, EmotionStemSelectorTests, Issue1311_RestoreLlmDeliveryTests, Issue768_InterestBreakdownTests, and ComboGameSessionTests; 74 passed, 0 failed.

### Finding 7: Emotion-stem selection hides gameplay tuning in unexplained literals
**Status**: Resolved
**Resolution**: Emotion-stem phase thresholds, hysteresis bounds, registry sizes, deterministic seed salt, favored target indices, weighting, stat bounds, and trap adjustment are now named on EmotionStemSelectionRules and injected into EmotionStemSelector. Turn orchestration uses the named seed salt instead of embedding the literal at call sites.
**Verification**: Focused Core tests passed: Issue788_SnapshotRoundTripTests, Issue907_TextingStyleConflictMatrixTests, EmotionStemSelectorTests, Issue1311_RestoreLlmDeliveryTests, Issue768_InterestBreakdownTests, and ComboGameSessionTests; 74 passed, 0 failed.

