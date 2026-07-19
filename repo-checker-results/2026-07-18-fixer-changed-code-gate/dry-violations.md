> Scope: pinder-core files changed in 7962415f750de354b53d4f9b953eaa3e37b3575b..ae95e83cf40909337ccb0e9651b86bacae24972d; pinder-web files changed in c7a465bb67fb86ee6b5ab1105955b7c0717eddca..271b22301c0dbad598b55a3089b47ba6c3f49b78. Findings are limited to duplications with at least one endpoint in those changed-file scopes.

### Finding 1: SessionSetup data locator repeats the harness resolver contract
**File**: `pinder-core/src/Pinder.SessionSetup/DataFileLocator.cs:22`, `pinder-core/src/Pinder.NarrativeHarness/HarnessDataLocator.cs:16`
**Issue**: The new `Pinder.SessionSetup.DataFileLocator.FindDataFile(...)` repeats the existing `HarnessDataLocator.FindDataFile(...)` pattern: read `PINDER_DATA_PATH`, try path/case variants, then walk parent directories until a match is found. The new copy uses `TryResolveInDirectory(...)` / `FlipFirstSegmentCase(...)`, while the harness copy uses `GetPathVariations(...)`, so the two implementations already differ on directory support and second-segment casing.
**Impact**: Future fixes to data-file discovery, Linux case handling, or directory lookup have to be remembered in two resolver implementations, and the narrative harness can keep passing with lookup behavior that session setup does not share.
**Urgency**: U3 - topic default; production/tooling duplication, but no immediate wrong behavior is proven in the changed range.
**Fixer-Agent Action Plan**: Make the narrative harness consume `Pinder.SessionSetup.DataFileLocator` if project references allow it, or extract the shared resolver into a small common project with explicit file-vs-directory lookup options. Delete `HarnessDataLocator` once harness callers are migrated and keep the existing data-locator tests pointed at the shared implementation.

### Finding 2: Rule numeric coercion is copied across three rule components
**File**: `pinder-core/src/Pinder.Rules/ConditionEvaluator.cs:111`, `pinder-core/src/Pinder.Rules/OutcomeDispatcher.cs:161`, `pinder-core/src/Pinder.Rules/RuleBookResolver.cs:451`
**Issue**: `ConditionEvaluator`, `OutcomeDispatcher`, and `RuleBookResolver` each implement the same integer coercion ladder: null check, `int`, `long`, `double`, `float`, invariant-culture `int.TryParse(...)`, then a `FormatException`. `OutcomeDispatcher` and `RuleBookResolver` also duplicate the same `ToDouble(...)` shape.
**Impact**: This changed range tightened malformed-rule handling, so duplicated coercion can drift and make conditions, live outcomes, and resolved rule-book projections accept or reject different YAML values.
**Urgency**: U2 - escalated from U3 because this is production rule-validation logic and divergence can change gameplay rule behavior.
**Fixer-Agent Action Plan**: Extract a shared internal rule value coercion helper, for example `RuleValueCoercion.ToInt(value, "Rule condition", context)` and `ToDouble(...)`. Update the three callers to pass only their message prefix/context, then run the `Pinder.Rules.Tests` spec-compliance and condition/outcome tests.

### Finding 3: LLM terminal diagnostic wrappers are repeated in every stage
**File**: `pinder-core/src/Pinder.Core/Conversation/DateeResponseStage.cs:152`, `pinder-core/src/Pinder.Core/Conversation/DeliveryStage.cs:488`, `pinder-core/src/Pinder.Core/Conversation/TurnOrchestrator.cs:274`
**Issue**: The changed code replaces manual `OperationalDiagnosticEvent` construction with helper calls, but each LLM stage still repeats the same terminal pattern: success emits `EmitSucceededTerminal(...)`, `catch (OperationCanceledException ex)` emits `EmitCancelledTerminal(...)` then `throw`, and `catch (Exception ex)` emits `EmitFailedTerminal(...)` then `throw`.
**Impact**: Diagnostic lifecycle changes still require synchronized edits across dialogue options, delivery, and datee response. A future agent can easily update one stage's cancellation/failure telemetry and leave the others inconsistent.
**Urgency**: U3 - topic default; duplicated production plumbing, but the copies currently preserve behavior.
**Fixer-Agent Action Plan**: Add a shared operation wrapper such as `OperationalDiagnostics.RunTerminalAsync(...)` that accepts component name, event-name stems/messages, operation kind, phase code, call id, turn number, and the async body. Replace the three try/catch blocks with calls to that wrapper and rerun the conversation/diagnostics tests.

### Finding 4: Backend admin tests still copy required environment setup after adding shared auth helpers
**File**: `pinder-web/src/pinder-backend/tests/test_admin_content.py:30`, `pinder-web/src/pinder-backend/tests/test_admin_content_game_definition.py:28`, `pinder-web/src/pinder-backend/tests/test_prompt_staging_gate.py:24`
**Issue**: The changed tests now import `_test_helpers.override_user`, and both `src/pinder-backend/_test_helpers.py:29` and `src/pinder-backend/tests/conftest.py:19` set the safe test defaults, but many changed admin test modules still repeat the same four lines: `os.environ.setdefault("GAME_API_URL", ...)`, `GAMEAPI_SHARED_SECRET`, `SECRET_KEY`, and `EIGENCORE_URL`.
**Impact**: Any future required env var or test default has to be updated across many modules even though this sprint introduced central helpers for exactly this bootstrap/auth surface.
**Urgency**: U3 - topic default; test-only duplication with maintainability risk.
**Fixer-Agent Action Plan**: Keep import-order safety in one place by moving all required defaults into `_test_helpers` or package `conftest.py`. Remove the repeated module-level `setdefault(...)` blocks from changed admin tests, preserving only imports needed by each file, then run the affected `src/pinder-backend/tests/test_admin_*` suites.

### Finding 5: TurnResultDisplay issue tests duplicate base result builders
**File**: `pinder-web/frontend/src/components/TurnResultDisplay.974.test.tsx:14`, `pinder-web/frontend/src/components/TurnResultDisplay.975.test.tsx:14`, `pinder-web/frontend/src/components/TurnResultDisplay.976.test.tsx:19`, `pinder-web/frontend/src/components/TurnResultDisplay.unifiedStack.test.tsx:40`
**Issue**: The changed TurnResultDisplay tests each define local `baseRoll(...)` and `baseResult(...)` wrappers around `makeRollResult(...)` / `makeTurnResult(...)`. The defaults vary by file, but the duplicated structure and common fields now live beside an existing shared `frontend/src/test-fixtures/turnResultBuilder` fixture module.
**Impact**: When `TurnResult` or `RollResult` defaults change, agents must patch several issue-specific test files instead of one fixture helper, increasing the chance that one scenario silently keeps stale defaults.
**Urgency**: U3 - topic default; frontend test duplication only.
**Fixer-Agent Action Plan**: Add named fixture presets to `turnResultBuilder`, such as `makeDisplayRoll(...)`, `makeDisplayTurnResult(...)`, or scenario-specific presets for nat-20/math/XP displays. Replace the four local builder pairs with those shared presets and rerun the TurnResultDisplay Vitest files.

### Finding 6: Generated-character JSON fixture is duplicated across GameApi test layers
**File**: `pinder-web/src/Pinder.GameApi.Tests/Controllers/CharactersControllerTests.cs:1108`, `pinder-web/src/Pinder.GameApi.Tests/Services/CharacterGeneratorTests.cs:392`
**Issue**: `BuildValidGeneratedCharacterJson(...)` in the controller tests and `BuildValidCharacterJson(...)` in the service tests both embed the same large JSON payload: `name`, `gender_identity`, `level`, the same four item ids, the full anatomy numeric block, `build_points`, and shadows.
**Impact**: Character-generation validation fixtures can drift between controller and service tests when bundled item/anatomy requirements change, causing one layer to test a stale "known good" payload.
**Urgency**: U3 - topic default; duplicated test fixture data with no direct production impact.
**Fixer-Agent Action Plan**: Move the valid generated-character JSON builder into a shared GameApi test fixture/helper, parameterized by name and any fields individual tests need to vary. Replace both private helpers with the shared builder and run `Pinder.GameApi.Tests` character controller/service tests.
