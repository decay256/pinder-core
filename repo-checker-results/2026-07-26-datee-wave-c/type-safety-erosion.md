> Scope: current #1340 uncommitted changed files only.

# Topic 25: Type-Safety Erosion

No concrete type-safety erosion findings were found in the #1340 changed-code scope.

Inspected the nullable optional event compatibility, enum validation, string outcome keys, dictionary contracts, casts/suppressions, and trace/template maps across the #1340 changed files:

- `src/Pinder.Core/Conversation/DateeContext.cs`
- `src/Pinder.Core/Conversation/DateeResponseStage.cs`
- `src/Pinder.Core/Conversation/DateeEmotionalTurnEvent.cs`
- `src/Pinder.Core/Rolls/RollOutcomeIntensity.cs`
- `src/Pinder.Core/Text/AnnotatedStringBuilder.cs`
- `src/Pinder.LlmAdapters/EmotionalReactionEventCompiler.cs`
- `src/Pinder.LlmAdapters/EmotionalReactionPromptCatalog.cs`
- `src/Pinder.LlmAdapters/StatDeliveryInstructions.cs`
- `tests/Pinder.Core.Tests/Issue1340_EmotionalTurnEventForwardingTests.cs`
- `tests/Pinder.LlmAdapters.Tests/Issue1339_EmotionalReactionPromptCatalogTests.cs`
- `tests/Pinder.LlmAdapters.Tests/Issue1340_EmotionalReactionEventCompilerTests.cs`

Relevant checks:

- `DateeEmotionalTurnEvent` preserves backwards compatibility with a nullable optional event on `DateeContext`, validates `StatType` and `RollOutcomeIntensity` with `Enum.IsDefined`, and snapshots the diagnosis map into a read-only dictionary.
- `RollOutcomeIntensityContract` centralizes roll-intensity-to-key conversion and fails closed for unknown intensity values or non-failure `FailureTier` values; this replaces the previous implicit fallback path in `StatDeliveryInstructions.FailureTierKey`.
- `EmotionalReactionPromptCatalog` validates relationship transition keys and outcome keys against ordinal `HashSet<string>` contracts before composing prompt keys.
- `EmotionalReactionEventCompiler` uses nullable suppression only after `TherapistDiagnosisContract.ValidateRequiredFields` has established non-null, required-key, nonblank diagnosis contents. The resulting indexers are contract-backed rather than unchecked boundary reads.
- `AnnotatedStringBuilder.Append(PromptTraceResult?)` preserves nested trace spans with explicit offset rebasing, and #1340 tests assert rendered substrings for nested spans.

U1/U2/U3 totals: 0 / 0 / 0.
