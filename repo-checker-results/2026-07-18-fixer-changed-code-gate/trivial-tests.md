> Scope: pinder-core 7962415f750de354b53d4f9b953eaa3e37b3575b..ae95e83cf40909337ccb0e9651b86bacae24972d (90 existing changed files, 32 changed test files inspected); pinder-web c7a465bb67fb86ee6b5ab1105955b7c0717eddca..271b22301c0dbad598b55a3089b47ba6c3f49b78 (82 existing changed files, 41 changed test files inspected). Current HEAD.

### Finding 1: GameSessionConfig tests only assert constructor pass-through
**File**: `pinder-core/tests/Pinder.Core.Tests/RuleResolverIntegrationTests.cs:102`
**Issue**: `GameSessionConfig_AcceptsNullRules` constructs `new GameSessionConfig(..., rules: null)` and only asserts `Assert.Null(config.Rules)`. The adjacent `GameSessionConfig_AcceptsMockRules` constructs `new GameSessionConfig(..., rules: resolver)` and only asserts `Assert.Same(resolver, config.Rules)`. Both tests prove the constructor stores an argument in a getter, not that rule resolver wiring affects gameplay behavior.
**Impact**: These tests inflate the rule-resolver integration suite while leaving the meaningful contract to later tests in the same file. A constructor/getter regression would already be caught by behavior tests that use `config.Rules`; these two tests add maintenance noise without protecting a separate user-visible path.
**Urgency**: U2 - topic default.
**Fixer-Agent Action Plan**: Delete the two pass-through tests, or replace them with one behavior-focused test that proves a supplied resolver changes a `GameSession` outcome while a null resolver follows the documented host-boundary behavior.

### Finding 2: AggregationResult constructor test only restates collection storage
**File**: `pinder-core/tests/Pinder.Core.Tests/Issue907_TextingStyleConflictMatrixTests.cs:385`
**Issue**: `AggregationResult_Constructor_StoresArguments` creates `lines` and `drops`, calls `new TextingStyleAggregator.AggregationResult(lines, drops)`, then only asserts `Assert.Single(result.Lines)` and `Assert.Empty(result.Drops)`. It does not exercise aggregation, conflict handling, audit attribution, or any observable behavior beyond property assignment.
**Impact**: The test can pass even if conflict-aware texting-style aggregation is wrong, and it duplicates the implementation's trivial constructor mechanics instead of protecting the issue-907 behavior this file is meant to cover.
**Urgency**: U2 - topic default.
**Fixer-Agent Action Plan**: Remove this constructor-only test. If coverage for `AggregationResult` shape is still desired, fold it into an `AggregateWithAudit(...)` behavior test that asserts the returned `Lines`, `Drops`, and `AttributedLines` are produced by real conflicting inputs.

### Finding 3: GameState constructor test duplicates a data-holder initializer
**File**: `pinder-core/tests/Pinder.Rules.Tests/SpecComplianceTests.cs:167`
**Issue**: `GameState_Constructor_StoresAllValues` builds `new GameState(...)` with every constructor parameter set, then asserts each getter returns the same literal value. This is a boilerplate data-holder test; it does not evaluate conditions, dispatch outcomes, or verify a rule scenario.
**Impact**: The test adds a large block of low-signal assertions to the spec compliance suite. If a `GameState` value stops flowing correctly, the surrounding `ConditionEvaluator_*` and YAML integration tests should catch the behavior; this test mainly repeats the constructor implementation.
**Urgency**: U2 - topic default.
**Fixer-Agent Action Plan**: Delete the constructor-storage test, or replace it with a compact parameterized condition-evaluator test that proves each relevant `GameState` field affects a real rule predicate.
