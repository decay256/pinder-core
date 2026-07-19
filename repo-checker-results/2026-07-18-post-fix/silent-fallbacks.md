> Scope: full pinder-core and pinder-web repositories at finalized commits (`pinder-core` 7962415f750de354b53d4f9b953eaa3e37b3575b, `pinder-web` c7a465bb67fb86ee6b5ab1105955b7c0717eddca).

### Finding 1: Duplicate rule IDs overwrite earlier gameplay rules at load time
**File**: `pinder-core/src/Pinder.Rules/RuleBook.cs:25`
**Issue**: `RuleBook` builds the ID index with `_byId[entry.Id] = entry;` inside the load constructor. A later YAML entry with the same `id` silently replaces the earlier one in `GetById(...)`, while both entries still remain in `All` and type lists.
**Impact**: A duplicated production rule ID can make failure-tier, interest-state, XP, progression, or shadow-threshold lookups use the last copy with no startup failure. Operators see a valid rulebook and gameplay continues with whichever duplicate happened to appear later in the YAML.
**Urgency**: U1 - topic default; duplicate production rule data can silently change core gameplay resolution.
**Fixer-Agent Action Plan**: Reject duplicate non-empty rule IDs during `RuleBook` construction with a `FormatException` that includes the duplicated ID and both entry positions. Update the duplicate-ID tests to assert fail-fast behavior, then run `Pinder.Rules.Tests` and the GameApi startup/config tests.

### Finding 2: Unknown rule condition keys are treated as successful matches
**File**: `pinder-core/src/Pinder.Rules/ConditionEvaluator.cs:23`
**Issue**: `ConditionEvaluator.Evaluate(...)` iterates every condition key, but the `default` branch at lines 85-87 does nothing. The class comment states unknown keys are "treated as matching", so a condition containing only an unrecognized key returns `true`.
**Impact**: A typo such as `interest_ranges` or a not-yet-supported rule condition can make a rule less constrained or always matching. Because `RuleBookResolver` uses this evaluator for failure tiers, success scales, interest states, and momentum rules, malformed YAML can silently select the wrong gameplay rule.
**Urgency**: U1 - topic default; malformed rule config can become active gameplay behavior with no failure signal.
**Fixer-Agent Action Plan**: Replace the default branch with a validation failure for unknown condition keys, or validate all condition dictionaries during `RuleBook.LoadFrom(...)`. Add regression tests for unknown-only and mixed known/unknown condition dictionaries.

### Finding 3: Malformed numeric rule condition values are coerced to zero
**File**: `pinder-core/src/Pinder.Rules/ConditionEvaluator.cs:110`
**Issue**: `ToInt(...)` returns `0` for `null`, non-numeric strings, and unsupported value types at lines 112-119. Range and threshold checks then use that zero as if it were an authored rule value, for example `miss_range: ["oops", 5]` becomes `0..5`.
**Impact**: Bad YAML values do not fail validation; they alter match boundaries. A malformed `natural_roll`, `streak_minimum`, `miss_range`, or `interest_range` can silently make rules fire too often or not at all.
**Urgency**: U1 - topic default; invalid rule data is converted into valid-looking gameplay thresholds.
**Fixer-Agent Action Plan**: Change condition parsing to return a typed validation result or throw `FormatException` on non-numeric values. Include the rule ID, condition key, and raw value in the error, and add tests for bad scalar and bad range elements.

### Finding 4: Outcome dispatch ignores unknown effects and coerces bad values to zero
**File**: `pinder-core/src/Pinder.Rules/OutcomeDispatcher.cs:25`
**Issue**: `OutcomeDispatcher.Dispatch(...)` silently drops unknown outcome keys in the `default` branch at lines 69-71, while `ToInt(...)` and `ToDouble(...)` return `0`/`0.0` for null, non-numeric strings, and unsupported value types at lines 86-107.
**Impact**: A misspelled outcome such as `interest_dleta` is accepted and does nothing, and malformed values such as `roll_bonus: high` dispatch as `+0`. Any caller using the rule dispatcher can report success while rule effects are missing or neutralized.
**Urgency**: U2 - de-escalated from U1 because the current GameSession path primarily uses `RuleBookResolver` helper lookups rather than this dispatcher, but this public rules component still encodes fail-open rule execution.
**Fixer-Agent Action Plan**: Make dispatch validate the full outcome dictionary before applying effects. Throw on unknown outcome keys and non-coercible numeric values, then update `OutcomeDispatcherTests` and `SpecComplianceTests.OutcomeDispatcher` to pin fail-fast behavior.

### Finding 5: Random character generation invents zero shadow allocations when the LLM omits them
**File**: `pinder-web/src/Pinder.GameApi/Services/CharacterGenerator.cs:239`
**Issue**: `WrapInCanonicalEnvelope(...)` writes the LLM-provided `shadows` block when present, but otherwise creates a full `allocation.shadows` object with `madness`, `despair`, `denial`, `fixation`, `dread`, and `overthinking` all set to `0` at lines 245-253.
**Impact**: A model response that omits shadow allocations is accepted, validated, and saved as a canonical character instead of being retried as an incomplete contract. Randomized characters can silently lose intended shadow starting state while the operation reports success.
**Urgency**: U1 - topic default; missing generated contract data is invented and persisted as real character state.
**Fixer-Agent Action Plan**: Require the LLM output to include `shadows` with every supported shadow key before wrapping. Treat missing or partial shadows as a validation failure that counts toward the existing retry loop, and add `CharacterGeneratorTests` coverage for missing and partial shadow blocks.

### Finding 6: Character update drops unknown allocation keys instead of rejecting the payload
**File**: `pinder-web/src/Pinder.GameApi/Controllers/CharactersController.cs:417`
**Issue**: `PutCharacter(...)` parses `payload.Allocation.Spent` and `.Shadows` with `Enum.TryParse(...)`, but only adds keys that parse at lines 417-435. Unknown stat or shadow keys are skipped with no error before the controller creates a new `AllocationBlock`.
**Impact**: An admin/API caller can submit a typo or stale allocation key and receive a successful character update while that field is discarded. Because the writer later serializes missing enum entries as zero/defaults, the saved character can silently lose authored build points or starting shadow values.
**Urgency**: U1 - topic default; invalid persisted character stats are accepted as successful writes.
**Fixer-Agent Action Plan**: Collect unknown allocation keys and return a 400 validation error before constructing the `AllocationBlock`. Reuse the strict parsing behavior from `CharacterDefinitionLoader.ParseSpent(...)` / `ParseShadowsBlock(...)`, and add controller tests for unknown stat and shadow keys.

### Finding 7: Character assembly treats missing item/anatomy definitions as reduced stats
**File**: `pinder-core/src/Pinder.Core/Characters/CharacterAssembler.cs:71`
**Issue**: `Assemble(...)` records unknown equipped item IDs in `unknownIds` and applies no modifiers at lines 71-82; unknown anatomy parameter IDs are simply skipped with `if (param == null) continue;` at lines 86-89. The resulting `FragmentCollection` still contains a valid `StatBlock`, and the GameApi `CharacterDetail` DTO does not expose `UnknownItemIds`.
**Impact**: If character JSON references an item or anatomy parameter removed from the data catalogs, sessions can start with missing modifiers, missing texting/personality fragments, and altered active archetypes while the character loads successfully. Unknown anatomy references are not even retained for diagnostics.
**Urgency**: U1 - topic default; catalog drift silently changes runtime character mechanics and prompt inputs.
**Fixer-Agent Action Plan**: Make production assembly fail on unknown item IDs and anatomy parameters, or return a typed assembly result that GameApi must surface as a load/setup failure. If admin authoring still needs lenient preview behavior, gate that mode behind an explicit option and expose all unknown IDs in the admin DTO.

### Finding 8: Missing character shadow block loads as a full zero-shadow allocation
**File**: `pinder-core/src/Pinder.SessionSetup/CharacterDefinitionLoader.cs:445`
**Issue**: `ParseShadowsBlock(...)` initializes every `ShadowStatType` to `0` at lines 447-450, then returns that zero-filled dictionary when `allocation.shadows` is missing or not an object at lines 452-456.
**Impact**: A malformed or old character file with no shadow allocation is accepted as a complete character with zero starting shadows. That masks data drift and changes gameplay setup, sheet display, and prompt context without a loader error.
**Urgency**: U1 - topic default; required gameplay state is silently defaulted during persistent character loading.
**Fixer-Agent Action Plan**: Require `allocation.shadows` to be present and object-shaped for the current schema version, and require every supported shadow key unless an explicit migration/backfill mode is active. Add loader tests for missing, null, non-object, and partial shadow blocks.

Suppressed by exception: Unsafe Leakage of GameApi Raw Exception/HTML Content (resp.text) to public-facing HTTP responses - would be U1
Suppressed by exception: Unsafe Leakage of Raw C# Exception Messages (ex.Message) in public GameApi endpoints - would be U1
Suppressed by exception: Exposure of Internal low-level Library Exceptions and Socket Errors to End-Users - would be U1
Suppressed by exception: Direct Leakage of Internal System Errors and File Paths in Admin Controller Error Payloads - would be U1

Counts: 8 findings total; U1=7, U2=1, U3=0. Suppressed would-be U1 by approved exception=4.
