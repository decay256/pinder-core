> Scope: uncommitted files changed for Core #1342/#1343 (22 files)

### Finding 1: Emotional-director field list is maintained in multiple production places
**File**: `src/Pinder.LlmAdapters/EmotionalDirectionLeakGuard.cs:12`
**Issue**: The seven emotional-director fields are duplicated as independent lists across the new leak guard (`FieldPrefixes` / `FieldPlaceholders`), runtime prompt validation in `src/Pinder.LlmAdapters/EmotionalReactionPromptCatalog.cs:169`, prompt rendering in `src/Pinder.LlmAdapters/SessionDocumentBuilder.Trace.cs:591`, and the existing contract schema/parser in `src/Pinder.LlmAdapters/EmotionalDirectorContract.cs:51`. The same set appears as `"primary_emotion", "intensity", "underlying_feeling", "interpretation", "impulse", "restraint", "response_posture"` in several forms.
**Impact**: A future director-field rename/addition can update the JSON contract but miss the leak guard or rendered performance prompt, creating false leak failures, missing validation, or a production prompt that silently drops a director field.
**Urgency**: U3 - topic default; this is maintainability drift risk, not an immediate behavioral defect.
**Fixer-Agent Action Plan**: Promote a small internal descriptor list from `EmotionalDirectorContract` (wire name, display label, `EmotionalDirectorDirection` accessor, trace key) and reuse it in schema construction, parsing, `EmotionalReactionPromptCatalog.ValidateRuntimeCatalog`, `EmotionalDirectionLeakGuard.ValidatePerformanceTemplate`, and `SessionDocumentBuilder.BuildDateePromptCore`.

### Finding 2: Prompt-catalog test fixtures duplicate temp-copy and discovery helpers
**File**: `tests/Pinder.LlmAdapters.Tests/Issue1342_1343_EmotionalDirectorPerformanceTests.cs:473`
**Issue**: The new performance test file redefines `BuiltInCatalog`, `FindPromptsRoot`, and `CopyPromptsToTemp` even though the same helper trio already exists in scoped emotional-reaction tests at `tests/Pinder.LlmAdapters.Tests/Issue1339_EmotionalReactionPromptCatalogTests.cs:229`, `tests/Pinder.LlmAdapters.Tests/Issue1340_EmotionalReactionEventCompilerTests.cs:474`, and `tests/Pinder.LlmAdapters.Tests/Issue1341_EmotionalDirectorContractTests.cs:351`. The temp-copy helper body is effectively repeated: create a temp folder, enumerate `*.yaml`, copy each file by name, then delete the folder in callers.
**Impact**: Tests that mutate `data/prompts` copies can diverge in path discovery, validation, or cleanup behavior as more emotional-director cases are added, making prompt-catalog failures inconsistent and harder to fix.
**Urgency**: U3 - topic default; duplication is in test support code and does not affect runtime behavior directly.
**Fixer-Agent Action Plan**: Move the shared helpers into a test-only utility such as `PromptCatalogTestFixture` under `tests/Pinder.LlmAdapters.Tests`, with `LoadBuiltInValidatedCatalog()` and `CopyPromptsToTemp()` methods, then update the four scoped test classes to call the common fixture.
