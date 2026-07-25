> Scope: current #1338/#1339 sprint changed files against 218b9c8 (14 files)

### Finding 1: Interest-state prompt slugs are mapped in a third switch
**File**: `src/Pinder.LlmAdapters/EmotionalReactionPromptCatalog.cs:31`
**Issue**: `GetInterestStateMeaningKey` repeats the complete seven-case `InterestState`-to-kebab-case mapping already encoded by `PromptTemplates.GetInterestNarrativeKey` and `PromptTemplates.GetResistanceKey` (`PromptTemplates.cs:218` and `PromptTemplates.cs:251`). All three switches independently translate `VeryIntoIt` to `very-into-it`, `AlmostThere` to `almost-there`, and `DateSecured` to `date-secured`, differing only in the prompt-key prefix.
**Impact**: Adding or renaming an interest state requires coordinated edits to three production switches. A missed update is detected only when the affected catalog is validated or requested, and each prompt family can drift to a different slug convention.
**Urgency**: U3 — topic default; this is duplicated production mapping logic, but current exhaustive switches and runtime catalog validation make immediate wrong behavior unlikely.
**Fixer-Agent Action Plan**: Introduce one internal `InterestState` key-segment mapper in `Pinder.LlmAdapters`, reuse it from all three key builders, and retain focused tests that lock the full prefixed keys for each prompt family.

### Finding 2: Emotional prompt validation duplicates PromptCatalog primitives
**File**: `src/Pinder.LlmAdapters/EmotionalReactionPromptCatalog.cs:124`
**Issue**: `RequireSystemPrompt` duplicates `PromptCatalog.RequireField`'s missing-entry and blank-`system_prompt` checks (`PromptCatalog.cs:289`), including parallel error text. `RequirePlaceholder` at line 139 also repeats the `{token}` construction and `IndexOf` validation performed by `PromptCatalog.ValidateRuntimeCatalog` at lines 264-272.
**Impact**: Required-field and placeholder semantics now have two implementations in the same assembly. Changes to error wording, whitespace policy, or token matching can make emotional-reaction prompts validate differently from every other runtime prompt.
**Urgency**: U3 — topic default; the duplicate paths currently enforce equivalent rules and fail loudly, so the present cost is maintenance and future drift.
**Fixer-Agent Action Plan**: Expose a narrowly scoped internal required-field/token validator from `PromptCatalog` (or register emotional-reaction entries as declarative runtime token contracts), then remove `RequireSystemPrompt` and `RequirePlaceholder` from `EmotionalReactionPromptCatalog`. Keep the missing-key, blank-field, and missing-placeholder regression tests.

### Finding 3: Complete diagnosis fixtures are implemented twice
**File**: `tests/Pinder.Core.Tests/Issue1253_SequentialSynthesisTests.cs:451`
**Issue**: `CompleteDiagnosisWith` is the same fixture builder as the newly added method in `TherapistDiagnosisContractTests.cs:202`: both enumerate `TherapistDiagnosisContract.RequiredFields`, assign `"specific formulation for {field}"`, and apply tuple overrides. The sprint therefore introduced two local implementations even though both test projects already reference `Pinder.Core.TestCommon`, which also owns `TestHelpers.MakePsychiatricDiagnosis`.
**Impact**: Future diagnosis-contract changes or fixture semantics must be updated in multiple test classes. Divergent defaults can make failures depend on which local fixture a test happened to use and add repetitive repair work to every contract extension.
**Urgency**: U3 — topic default; this duplication is test-only and does not affect runtime behavior.
**Fixer-Agent Action Plan**: Add one override-capable diagnosis factory to `Pinder.Core.TestCommon` and use it from both changed test classes. Preserve `ExpectedDiagnosisFields` as an independent explicit assertion in `TherapistDiagnosisContractTests` so the contract-order test does not become tautological.

### Finding 4: Prompt-catalog filesystem fixtures are copied again
**File**: `tests/Pinder.LlmAdapters.Tests/Issue1339_EmotionalReactionPromptCatalogTests.cs:236`
**Issue**: The new test class adds local `FindPromptsRoot` and `CopyPromptsToTemp` implementations that duplicate the same ancestor search and YAML-directory copy in `PromptCatalogValidationTests.cs:187-213`; similar copies also exist in `Issue1126_SlimPromptConfigTests` and `AnthropicTransportTests`. The new tests additionally repeat `try/finally` recursive cleanup around each temporary copy.
**Impact**: Prompt-catalog tests have multiple subtly different path discovery, temporary naming, copying, and cleanup policies. Changes to prompt asset layout or fixture cleanup must be propagated manually, and failed tests can leave directories behind when a local variant omits or mishandles disposal.
**Urgency**: U3 — topic default; this is test infrastructure duplication with no production-path effect.
**Fixer-Agent Action Plan**: Move prompt-root resolution to `TestRepoLocator` or a dedicated `Pinder.Core.TestCommon` helper and add an `IDisposable` temporary prompt-catalog fixture that copies YAML files and owns cleanup. Migrate the new #1339 tests first, then replace the existing local copies without changing their assertions.
