Topic: trivial-tests
Scope: full repositories at current commits pinder-core e96a75f4 and pinder-web 1a7acb38.

Inspected the test surfaces in both repositories for tautological assertions, assertion-free tests, boilerplate getter/setter tests, and tests that duplicate implementation without behavioral value. Coverage included C# xUnit tests under `pinder-core/tests` and `pinder-web/src/Pinder.GameApi.Tests`, Python pytest tests under `rules/tools`, `pinder-web/src/pinder-backend`, and `pinder-web/scripts`, plus Vitest and Playwright tests under `pinder-web/frontend`. No direct tautologies such as `Assert.True(true)`, `Assert.False(false)`, or `expect(true).toBe(true)` were found.

Findings: 3 total. U1: 0. U2: 3. U3: 0.

### Finding 1: Fixture-capture facts pass as no-op tests by default
**File**: `pinder-web/src/Pinder.GameApi.Tests/Infrastructure/CaptureFixtures.cs:53`
**Issue**: Five `[Fact]` methods in `CaptureFixtures` are registered as normal xUnit tests but immediately return when fixture recording is not enabled: `if (!ShouldCapture) return;` appears at lines 55, 74, 103, 128, and 154. The class comment explicitly says these are no-ops in default CI runs, so default test execution reports passing tests that exercise no behavior and assert nothing.
**Impact**: CI/test counts are inflated by five passing tests that do not validate the GameApi, fixtures, or recording path unless `RECORD_FIXTURES=1` is set. A broken capture harness can look green in normal runs, and future maintainers may trust these as behavioral coverage.
**Urgency**: U2 - topic default; these are assertion-free registered tests that degrade the reliability signal of the test suite.
**Fixer-Agent Action Plan**: Convert the capture methods to explicit manual/recording-only tests using an xUnit skip mechanism when `RECORD_FIXTURES` is unset, or move them out of the normal test project into a fixture-recording command/tool. Verify with `dotnet test src/Pinder.GameApi.Tests/ --filter "Category=RecordFixtures"` that default runs report skipped/manual status rather than passed no-ops, and with `RECORD_FIXTURES=1` that the capture path still runs intentionally.

### Finding 2: Narrative harness DTO tests only assert auto-property storage/defaults
**File**: `pinder-web/src/Pinder.GameApi.Tests/Controllers/AdminNarrativeHarnessControllerTests.cs:27`
**Issue**: `Dto_CarriesPursuerCharacter`, `Dto_PursuerCharacterDefaultsToNull`, `Dto_CarriesSeed`, `Dto_SeedDefaultsToNull`, `Dto_CarriesPlayerScript`, and `Dto_PlayerScriptDefaultsToNull` only instantiate `RunNarrativeHarnessRequest` and assert the assigned or default property value, for example `var req = new RunNarrativeHarnessRequest { PursuerCharacter = "velvet" }; Assert.Equal("velvet", req.PursuerCharacter);`. The same file already has behavioral tests for the load-bearing paths, such as `BuildArgs_IncludesPursuerCharacter_WhenSet`, `BuildArgs_IncludesSeed_WhenSet`, and `ParsePlayerScript_StripsCommentsAndBlanks`.
**Impact**: These tests duplicate C# auto-property behavior rather than controller/request behavior, increasing maintenance noise while giving little confidence about serialization, model binding, argument construction, or validation.
**Urgency**: U2 - topic default; these are boilerplate getter/setter/default tests with no meaningful behavioral assertion.
**Fixer-Agent Action Plan**: Remove the DTO-only carry/default tests or replace them with one focused request-binding/serialization test if wire compatibility is the concern. Keep and, if needed, extend the existing `BuildHarnessArgs` and `ParsePlayerScript` tests because those exercise observable behavior. Verify with `dotnet test src/Pinder.GameApi.Tests/ --filter AdminNarrativeHarnessControllerTests`.

### Finding 3: AnthropicOptions setter-only tests duplicate auto-property behavior
**File**: `pinder-core/tests/Pinder.LlmAdapters.Tests/Anthropic/AnthropicOptionsTests.cs:24`
**Issue**: `Properties_AreSettable` creates an `AnthropicOptions` object, assigns literal values to every public property, then asserts the same literals came back from the same object. `SpecAnthropicTests.AnthropicOptions_AllProperties_AreSettable` repeats the same setter-only pattern at `pinder-core/tests/Pinder.LlmAdapters.Tests/Anthropic/SpecAnthropicTests.cs:70`. These tests do not exercise configuration binding, validation, provider selection, or any code path consuming the options.
**Impact**: The suite carries duplicated boilerplate coverage that can pass while the real options behavior is broken, and it makes future option changes pay test-maintenance cost without adding behavioral confidence.
**Urgency**: U2 - topic default; this is a boilerplate getter/setter test duplicated in two files.
**Fixer-Agent Action Plan**: Delete the setter-only tests or replace them with a single options-binding/consumer test that loads representative configuration and verifies the Anthropic client/factory receives the intended values. Keep default-value tests only where the default is part of the runtime contract. Verify with `dotnet test tests/Pinder.LlmAdapters.Tests/Pinder.LlmAdapters.Tests.csproj --filter AnthropicOptions`.

No U1 findings were suppressed by the approved exceptions list.
