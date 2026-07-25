> Scope: current #1338/#1339 modified and untracked files in `A:\Data\ClaudeCodex\pinder-web\pinder-core` (30 files) plus `A:\Data\ClaudeCodex\pinder-web\src\Pinder.GameApi.Tests\Services\CharacterSynthesisServiceTests.cs` (1 file), excluding audit reports and the parent repository's `pinder-core` dirty marker (31 files total).

### Finding 1: Relationship transitions are a public string domain
**File**: `src/Pinder.LlmAdapters/EmotionalReactionPromptCatalog.cs:17`
**Issue**: The four closed transition values are published as `IReadOnlyList<string>` containing `"strengthened"`, `"preserved"`, `"damaged"`, and `"transformed"`, and both `GetRelationshipTransitionInstructionKey(string transitionKey)` and `GetRelationshipTransitionInstruction(..., string transitionKey)` accept arbitrary strings. The hash-set guard catches invalid values only at runtime; callers and tests receive no compiler-enforced transition coverage.
**Impact**: The downstream #1340 path can misspell a transition, pass a display/config string from the wrong domain, or fail to handle a newly added transition without a compile error. That turns an engine-derived relationship concept into an exception-prone prompt-key convention at the module boundary.
**Urgency**: U3 - topic default; current values are validated before lookup, so this is type-safety and future-change risk rather than incorrect behavior today.
**Fixer-Agent Action Plan**: Introduce a Core-owned `RelationshipTransitionKind` enum (or equivalent closed type), make the public catalog accessors accept that type, and keep the YAML slug conversion in one exhaustive switch inside the adapter. Have #1340 derive the typed transition from the typed before/after relationship state and update tests to enumerate the type rather than duplicate string literals.

### Finding 2: Outcome coverage is manually curated behind string APIs
**File**: `src/Pinder.LlmAdapters/StatDeliveryInstructions.cs:27`
**Issue**: `OutcomeTierKeys` manually creates ten strings from magic success margins (`0`, `5`, `10`, `15`, plus nat-20) and five selected `FailureTier` members. `EmotionalReactionPromptCatalog.GetEventMeaningKey(StatType stat, string outcomeKey)` then exposes the outcome as an arbitrary string. Catalog validation and the new 60-entry test both iterate this same list, so neither independently proves that all engine outcome variants are represented.
**Impact**: Adding or changing a success/failure tier can leave `OutcomeTierKeys` unchanged while catalog validation and its Cartesian-product tests remain green. A new engine outcome can consequently collapse to an old slug or reach the public lookup as an unrecognized string instead of forcing an exhaustive code update.
**Urgency**: U3 - topic default; all ten current outcomes are present and validated, but the contract does not make future enum/tier drift compile-visible.
**Fixer-Agent Action Plan**: Define one typed canonical delivery-outcome domain, convert resolved success margins/nat-20 and `FailureTier` to it through exhaustive mappings, and expose typed values from `StatDeliveryInstructions`. Accept that type in emotional-reaction accessors and translate it to the existing YAML strings only at the catalog boundary; add a test comparing the typed domain to every supported engine outcome source.

### Finding 3: Stat reaction fields are not exhaustively tied to StatType
**File**: `src/Pinder.Core/Characters/TherapistDiagnosisContract.cs:20`
**Issue**: The six new stat-specific diagnosis fields are independent string constants (`CharmReactionKey` through `SelfAwarenessReactionKey`) manually inserted into `RequiredFieldsArray`. There is no typed `StatType`-to-reaction-field mapping or coverage assertion. By contrast, emotional event prompts enumerate every `StatType`, so the two halves of the planned director input use different completeness mechanisms.
**Impact**: A new or renamed stat can acquire global event prompts while the required character formulation silently lacks a corresponding field. The downstream consumer must also repeat a string switch or construct `"<stat>_reaction"` itself, spreading the dictionary convention into another module.
**Urgency**: U3 - topic default; the current six stats and six fields align, but that alignment is maintained manually and is not compiler-enforced.
**Fixer-Agent Action Plan**: Add an exhaustive `GetReactionFieldKey(StatType stat)` mapping and a typed accessor that retrieves the validated reaction formulation for a stat. Build or validate the stat-reaction portion of `RequiredFields` from all `StatType` values while preserving the existing flat persisted dictionary and stable wire keys, then add a coverage test over `Enum.GetValues(typeof(StatType))`.

## Summary

Findings: 3 total, all U3 type-safety erosion at newly expanded string-key boundaries.

No additional dictionary-contract, nullability, cast, or suppression findings were found. The eleven diagnosis keys are centrally required by `TherapistDiagnosisContract`, checked against schema/prompt metadata, and rejected when absent or blank. The new production `SystemPrompt!` use follows an explicit nonblank guard; the remaining null-forgiving operators are assertion-backed test accesses. No `dynamic`, warning suppression, unchecked production cast, `# type: ignore`, or `@ts-ignore` was introduced in scope.

Focused verification passed: 19 adapter/catalog tests and 19 diagnosis-contract tests. `git diff --check` passed for both repositories.

## Counts

U1: 0
U2: 0
U3: 3
