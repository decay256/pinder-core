# unwired-code resolutions

### Finding 1: Shadow-aware datee timing calculator is bypassed
**Status**: Resolved
**Resolution**: The obsolete DateeTimingCalculator production class and its dedicated tests were removed under the product decision that legacy timing behavior no longer matters. Turn results and interest breakdown metadata no longer carry the retired delay-penalty contract.
**Verification**: Focused Core tests passed: Issue788_SnapshotRoundTripTests, Issue907_TextingStyleConflictMatrixTests, EmotionStemSelectorTests, Issue1311_RestoreLlmDeliveryTests, Issue768_InterestBreakdownTests, and ComboGameSessionTests; 74 passed, 0 failed. Symbol scans found no remaining DateeTimingCalculator, ComputeDelayMinutes, DelayPenalty, delayPenalty, or delay_penalty references in source, tests, data, or contracts.

### Finding 2: Player response delay penalties are modeled but hardwired off
**Status**: Resolved
**Resolution**: The obsolete PlayerResponseDelayEvaluator production class and delay-spec tests were removed under the product decision that delay behavior should not be wired back into sessions. The retired delay penalty source was also removed from TurnResult and UI interest-breakdown data.
**Verification**: Focused Core tests passed: Issue788_SnapshotRoundTripTests, Issue907_TextingStyleConflictMatrixTests, EmotionStemSelectorTests, Issue1311_RestoreLlmDeliveryTests, Issue768_InterestBreakdownTests, and ComboGameSessionTests; 74 passed, 0 failed. Symbol scans found no remaining PlayerResponseDelayEvaluator, DelayPenalty, delayPenalty, or delay_penalty references in source, tests, data, or contracts.

### Finding 3: Background-story generator and prompt catalog entry have no execution path
**Status**: Resolved
**Resolution**: The unused background generator interface and implementation were removed, along with the obsolete prompt catalog entry, related test assertions, and current documentation references. The prompt catalog verifier now reflects the active catalog files.
**Verification**: `dotnet test tests\Pinder.Core.Tests\Pinder.Core.Tests.csproj --filter "FullyQualifiedName~Issue843_PromptCatalogPhase1Tests|FullyQualifiedName~Issue1223_SetupGeneratorObservableFailureTests" --no-restore --verbosity minimal` passed within the 144-test focused core/session run. `python scripts\verify-issue-1227.py` passed.

### Finding 4: GameApi session query helper is never imported or called
**Status**: Resolved
**Resolution**: The current file state already resolves this finding: GameSessionRepository delegates its read-side methods through GameSessionQueryHelper for state loading, frozen sheet loading, turn records, user and admin session lists, debug rows, share-token reads, conversation history, and token usage. Because the helper is wired as the active query implementation, it was preserved rather than deleted.
**Verification**: Current source inspection confirmed GameSessionRepository delegates to GameSessionQueryHelper for the query-helper surfaces; focused GameApi tests passed for persisted session-state assembly through the repository-facing contract; `dotnet build Pinder.sln --no-restore` succeeded with 0 warnings and 0 errors.

### Finding 5: C# failure-tier display helper is not used by any production projection
**Status**: Resolved
**Resolution**: The unused C# failure-tier display helper, its dedicated tests, and its current spec document were removed. Active source, tests, and current docs no longer reference the helper.
**Verification**: `dotnet test tests\Pinder.Core.Tests\Pinder.Core.Tests.csproj --filter "FullyQualifiedName~CharacterSystemTests|FullyQualifiedName~Issue1176_RealUnityItemsTests|FullyQualifiedName~ArchitectureRuleTests" --no-restore --verbosity minimal` passed within the 144-test focused core/session run, and fixed-string reference checks found no active source, test, current spec, or module-doc references.

