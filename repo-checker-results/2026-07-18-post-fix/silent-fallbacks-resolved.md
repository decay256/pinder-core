# silent-fallbacks resolutions

### Finding 1: Duplicate rule IDs overwrite earlier gameplay rules at load time
**Status**: Resolved
**Resolution**: RuleBook construction now validates rule IDs before indexing and rejects duplicate non-empty IDs case-insensitively with a format error identifying the conflicting entries. Later rules can no longer silently replace earlier gameplay rules.
**Verification**: `dotnet test tests\Pinder.Rules.Tests\Pinder.Rules.Tests.csproj --no-restore --verbosity minimal` passed with 264 tests.

### Finding 2: Unknown rule condition keys are treated as successful matches
**Status**: Resolved
**Resolution**: Condition evaluation now treats unknown condition keys as malformed rule configuration and raises a format error instead of ignoring them. Regression coverage pins the fail-fast behavior for unknown keys.
**Verification**: `dotnet test tests\Pinder.Rules.Tests\Pinder.Rules.Tests.csproj --no-restore --verbosity minimal` passed with 264 tests.

### Finding 3: Malformed numeric rule condition values are coerced to zero
**Status**: Resolved
**Resolution**: Numeric condition parsing now rejects null, non-numeric strings, and malformed range values with format errors. The evaluator no longer fabricates zero values for corrupted conditions.
**Verification**: `dotnet test tests\Pinder.Rules.Tests\Pinder.Rules.Tests.csproj --no-restore --verbosity minimal` passed with 264 tests.

### Finding 4: Outcome dispatch ignores unknown effects and coerces bad values to zero
**Status**: Resolved
**Resolution**: Outcome dispatch now validates every outcome key and numeric value before applying any handler mutation. Unknown effect keys, invalid numeric values, and malformed shadow effects raise format errors, while recognized metadata keys remain explicit no-op metadata.
**Verification**: `dotnet test tests\Pinder.Rules.Tests\Pinder.Rules.Tests.csproj --no-restore --verbosity minimal` passed with 264 tests. `PYTHONUTF8=1 python rules\tools\rules_pipeline.py check` and `python rules\tools\generate_tests.py --check` passed.

### Finding 5: Random character generation invents zero shadow allocations when the LLM omits them
**Status**: Resolved
**Resolution**: Character generation now validates generated allocation contracts before wrapping LLM output into the canonical character envelope. Missing shadow blocks, partial shadow blocks, unknown allocation keys, and non-integer allocation values are rejected as validation failures so the existing retry loop can request a corrected model response instead of persisting fabricated zero-shadow state.
**Verification**: Focused GameApi regression tests passed for missing and partial generated shadow allocation rejection; `dotnet build Pinder.sln --no-restore` succeeded with 0 warnings and 0 errors.

### Finding 6: Character update drops unknown allocation keys instead of rejecting the payload
**Status**: Resolved
**Resolution**: Character update allocation parsing now rejects unknown stat and shadow keys with explicit validation errors before any save occurs. The update path was moved into a store-neutral character definition update service that loads existing definitions through the repository, validates items and allocation data, merges authored fields, serializes canonically, and saves through the repository instead of letting the controller drop unknown keys.
**Verification**: Focused GameApi controller tests passed for unknown spent and shadow allocation keys returning 400 without saving, plus valid remote-store update compatibility; `dotnet build Pinder.sln --no-restore` succeeded with 0 warnings and 0 errors.

### Finding 7: Character assembly treats missing item/anatomy definitions as reduced stats
**Status**: Resolved
**Resolution**: Character assembly now rejects unknown item IDs and anatomy parameter IDs with format errors instead of skipping them and producing plausible reduced-stat characters. Loader and assembler tests now assert fail-fast behavior for corrupted references.
**Verification**: `dotnet test tests\Pinder.Core.Tests\Pinder.Core.Tests.csproj --filter "FullyQualifiedName~CharacterDefinitionLoaderTests|FullyQualifiedName~Issue1176_RealUnityItemsTests|FullyQualifiedName~CharacterSystemTests|FullyQualifiedName~Issue836_TextingStyleAggregationRuleTests" --no-restore --verbosity minimal` passed as part of the 144-test focused core/session run.

### Finding 8: Missing character shadow block loads as a full zero-shadow allocation
**Status**: Resolved
**Resolution**: Character definition parsing now requires `allocation.shadows` to be an object containing every supported shadow stat. Missing, null, partial, unknown, or malformed shadow data now fails during load rather than manufacturing a zero-shadow allocation.
**Verification**: `dotnet test tests\Pinder.Core.Tests\Pinder.Core.Tests.csproj --filter "FullyQualifiedName~CharacterDefinitionLoaderTests|FullyQualifiedName~Issue415_CharacterDefinitionLoaderSpecTests" --no-restore --verbosity minimal` passed within the focused core/session verification, and the full focused run passed with 144 tests.

