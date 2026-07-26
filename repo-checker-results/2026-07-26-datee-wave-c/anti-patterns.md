> Scope: #1340 uncommitted changed files only (17 files): CHANGELOG.md, Directory.Build.props, agent.log, data/prompts/emotional-reactions.yaml, docs/data-architecture.md, docs/prompts.md, src/Pinder.Core/Conversation/DateeContext.cs, src/Pinder.Core/Conversation/DateeEmotionalTurnEvent.cs, src/Pinder.Core/Conversation/DateeResponseStage.cs, src/Pinder.Core/Rolls/RollOutcomeIntensity.cs, src/Pinder.Core/Text/AnnotatedStringBuilder.cs, src/Pinder.LlmAdapters/EmotionalReactionEventCompiler.cs, src/Pinder.LlmAdapters/EmotionalReactionPromptCatalog.cs, src/Pinder.LlmAdapters/StatDeliveryInstructions.cs, tests/Pinder.Core.Tests/Issue1340_EmotionalTurnEventForwardingTests.cs, tests/Pinder.LlmAdapters.Tests/Issue1339_EmotionalReactionPromptCatalogTests.cs, tests/Pinder.LlmAdapters.Tests/Issue1340_EmotionalReactionEventCompilerTests.cs.

No U1 swallowed or bare exception findings were found in the scoped diff.

### Finding 1: Relationship transition classification relies on enum ordinal ordering
**File**: `src/Pinder.LlmAdapters/EmotionalReactionPromptCatalog.cs:77`
**Issue**: The transition classifier uses enum ordering as behavior: `return after > before ? "strengthened" : "damaged";`. This makes the emotional reaction catalog depend on the declaration order of `InterestState` instead of an explicit progression/rank contract.
**Impact**: If a future change inserts, reorders, or gives explicit values to `InterestState`, this can silently classify relationship movement incorrectly. That would feed the wrong emotional direction prompt while the method still compiles.
**Urgency**: U3 - topic default for style smells; current tests exercise the existing state matrix, so this is maintainability risk rather than an immediate behavior defect.
**Fixer-Agent Action Plan**: Replace the ordinal comparison with an explicit rank map or helper owned by the relationship-state contract, and add a test that pins a representative strengthened/damaged/preserved/transformed matrix without deriving expected values from the implementation under test.

### Finding 2: Recent-history window is an unexplained magic number
**File**: `src/Pinder.LlmAdapters/EmotionalReactionEventCompiler.cs:178`
**Issue**: `CompileHistory` hardcodes the window with `history.Skip(Math.Max(0, history.Count - 6))`. The value `6` has no named constant, config entry, or nearby rationale tying it to the emotional director prompt budget.
**Impact**: Future prompt-budget or emotional-context tuning requires code archaeology and a code edit. Because this compiler is intended to produce concise private prompt input, the history window is a meaningful policy value rather than incidental formatting.
**Urgency**: U3 - topic default for magic-value style smells; it does not currently cause wrong behavior.
**Fixer-Agent Action Plan**: Introduce a named constant such as `RecentVisibleHistoryMessageLimit` with a short rationale, or route the value through the same prompt/session configuration surface if the team expects operators to tune it without recompilation.
