> Scope: current #1338/#1339 working-tree changes only: 29 modified/untracked files in `pinder-core` (including the six bundled character JSON files; audit reports excluded) plus `pinder-web/src/Pinder.GameApi.Tests/Services/CharacterSynthesisServiceTests.cs` (30 files total).

### Finding 1: Diagnosis fixtures hand-roll JSON without escaping
**File**: `tests/Pinder.Core.Tests/Issue1253_SequentialSynthesisTests.cs:477`
**Issue**: `ToJsonObject` builds JSON with `values.Select(pair => $"\"{pair.Key}\": \"{pair.Value}\"")` instead of `JsonSerializer.Serialize`. The same scoped change introduces equivalent concatenation in `tests/Pinder.Core.Tests/TherapistDiagnosisContractTests.cs:215` and `pinder-web/src/Pinder.GameApi.Tests/Services/CharacterSynthesisServiceTests.cs:796`.
**Impact**: These helpers accept arbitrary keys and values but do not escape quotes, backslashes, control characters, or newlines. A future diagnosis fixture containing realistic prompt prose with any of those characters will produce malformed JSON and can make a parser/retry test fail for the fixture builder rather than the behavior under test.
**Urgency**: U3 - topic default; this is a test-only structured-data construction smell and current fixture values happen to be JSON-safe.
**Fixer-Agent Action Plan**: Replace the concatenation helpers with `System.Text.Json.JsonSerializer.Serialize` over the diagnosis dictionary. Where only property text is needed, build the complete object with `JsonObject` or serialize the dictionary and insert the resulting object as a parsed node rather than splicing raw properties.

### Finding 2: Contract parity test parses an embedded JSON object with regex
**File**: `tests/Pinder.Core.Tests/TherapistDiagnosisContractTests.cs:286`
**Issue**: `ReadDiagnosisPromptObjectFields` locates the first brace pair with `Regex.Match(systemPrompt, "\\{(?<body>[\\s\\S]*?)\\}")` and then extracts fields with a second regex. This treats nested braces, escaped quotes, or an earlier prompt placeholder as structure even though the asserted content is a JSON object contract.
**Impact**: Harmless prompt wording changes can make the parity test inspect the wrong fragment or truncate the object while the runtime prompt remains valid. Conversely, regex acceptance can diverge from actual JSON syntax and give false confidence that the prompt example is parseable.
**Urgency**: U3 - topic default; the smell is isolated to a contract test and the current prompt shape is simple enough for the regex to pass.
**Fixer-Agent Action Plan**: Move the diagnosis field contract into structured metadata consumed by both prompt rendering and the test, or delimit the example explicitly and parse the delimited text with `JsonDocument`. Assert field order from parsed properties rather than regex captures.

### Finding 3: YAML mutation helper depends on indentation and key-prefix text
**File**: `tests/Pinder.LlmAdapters.Tests/Issue1339_EmotionalReactionPromptCatalogTests.cs:263`
**Issue**: `DeleteLineBlock` edits YAML as raw lines, finding a block with `line.Trim() == key + ":"` and ending it only when a line starts with exactly `"  emotional-reaction-"`. The blank-system-prompt test at lines 157-162 similarly relies on an exact multi-line text replacement.
**Impact**: Valid YAML formatting changes such as different indentation, comments between entries, quoted keys, or reordered prompt families can break the fixture mutation before runtime validation is exercised. Failures then report text-layout drift rather than the missing-key or missing-field behavior the tests intend to cover.
**Urgency**: U3 - topic default; this is test-only fixture brittleness with no production-path effect.
**Fixer-Agent Action Plan**: Parse the copied YAML into a mutable mapping with YamlDotNet, remove or alter the target entry structurally, and serialize it back. Keep the assertions on `ValidateRuntimeCatalog` unchanged so the tests continue to exercise the production validation boundary.
