> Scope: current sprint 22 implementation files from `018ce0e` (`feat: orchestrate DATEE emotional reactions`).

No concrete migration-integrity findings were found for the DATEE emotional-director/performance changes.

Inspected compatibility surfaces:

- Serialized structured output: `src/Pinder.LlmAdapters/EmotionalDirectorContract.cs` defines `SchemaName = "emotional_director"` and `SchemaVersion = "emotional_director.v1"`, emits a closed JSON object schema (`additionalProperties = false`), and rejects missing, unexpected, duplicate, malformed, blank, oversized, symbolic-only, drafted-reply, meta-language, and raw-mechanics values before constructing `EmotionalDirectorDirection`.
- Prompt catalog schema: `data/prompts/emotional-reactions.yaml` remains `schema_version: 1`; the new `emotional-reaction-performance-direction` key is additive under the existing prompt catalog shape. `src/Pinder.LlmAdapters/EmotionalReactionPromptCatalog.cs` validates the new key, all seven placeholders, and the protected structural lines fail-fast at catalog/runtime validation.
- Version bump: `Directory.Build.props` moves the package version from `0.2.20` to `0.2.21`; no public DTO schema or API handshake version is changed by this sprint.
- Old characters/configs: the new runtime path consumes the existing flat `TherapistDiagnosisContract.RequiredFields` through `DateeEmotionalTurnEvent`; this sprint does not add new persisted character fields or require character-schema migration. `DateeContext.EmotionalTurnEvent` remains constructor-optional for legacy callers, while the production `PinderLlmAdapter` DATEE path now fails closed when the event is absent.
- Public API compatibility: `PinderLlmAdapter.GetDateeResponseAsync(...)`, `DateeContext`, and prompt-catalog loading signatures remain unchanged. The new `SessionDocumentBuilder.BuildDateePerformancePromptEx(...)` and `EmotionalDirectorDirection` are internal.
- Source attribution: `EmotionalReactionEventCompiler` JSON-quotes delivered message/history as runtime spans; `SessionDocumentBuilder.BuildDateePerformancePromptEx(...)` attributes the YAML wrapper to `data/prompts/emotional-reactions.yaml` and each director field to `runtime:EmotionalDirectorDirection`.
- Database/schema migrations: this sprint adds no DB migration and touches no ORM/database schema files, so there are no Alembic/EF sibling-head, reversibility, or ORM-vs-schema drift findings in scope.

Prior report de-duplication checked against `dry-violations.md`, `doc-code-mismatches.md`, `unwired-code.md`, `anti-patterns.md`, `trivial-tests.md`, `prompt-hardcoding.md`, `silent-fallbacks.md`, and `model-id-drift.md`; no prior finding is repeated here.
