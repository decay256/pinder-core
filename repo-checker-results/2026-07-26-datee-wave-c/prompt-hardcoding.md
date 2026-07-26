> Scope: #1340 uncommitted changed files in `A:\Data\ClaudeCodex\pinder-web\pinder-core`; topic 10 `prompt-hardcoding`.

### Finding 1: Horniness catastrophe reinforcement remains hardcoded in C#
**File**: `src/Pinder.LlmAdapters/StatDeliveryInstructions.cs:275`
**Issue**: `CatastropheReinforcement` embeds reusable model-facing prose directly in C#: `"The structure is a normal Tinder question. The content is the joke. The character is utterly unaware."` The adjacent comments state this text is appended to `horniness_overlay.catastrophe` so "the philosophy text reaches the LLM on every call."
**Impact**: This bypasses the repo's prompt-catalog/YAML/admin-editing pattern. It creates a second prompt editing surface in source code, so designers/admins cannot update this instruction with the rest of `data/delivery-instructions.yaml` / `data/prompts/*.yaml`, and future prompt audits will keep rediscovering it.
**Urgency**: U3 - topic default; maintainability/configurability issue, not an immediate behavior break.
**Fixer-Agent Action Plan**: Move the catastrophe-specific reinforcement into the data prompt configuration, preferably under `data/delivery-instructions.yaml` near `horniness_overlay.catastrophe` or a named overlay template key; update `StatDeliveryInstructions.LoadFrom` to compose configured text rather than a source constant; add/update a regression test that fails if the configured reinforcement is absent while keeping C# limited to keys, diagnostics, and composition logic.

No additional topic-10 findings were found in the #1340 emotional-reaction compiler path. The new reusable DATEE emotional reaction prose is in `data/prompts/emotional-reactions.yaml`, while `EmotionalReactionEventCompiler.cs` and `EmotionalReactionPromptCatalog.cs` contain catalog keys, placeholder validation, diagnostics, and trace composition only.
