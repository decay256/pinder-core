# Contract: Sprint 15 Test & CI Tasks

## 1. Issue #470: RuleResolverWiringTests Fix
- Target: `tests/Pinder.Core.Tests/RulesSpec/Issue463_RuleResolverWiringTests.cs`
- Change `if (!result.Roll.IsSuccess)` to `Assert.False(result.Roll.IsSuccess);`.

## 2. Issue #447: Rules DSL CI Integration
- Target: `.github/workflows/rules-accuracy-check.yml`
- Trigger on push/pull_request affecting `design/systems/*.md`, `design/settings/*.md`, `rules/extracted/*.yaml`.
- Run `python rules/tools/accuracy_check.py`.
- Require `ANTHROPIC_API_KEY` secret.

## 3. Issue #442: Rules DSL Roundtrip Epic
- Validate that `rules/tools/roundtrip_test.sh` runs successfully. Ensure no `unstructured_prose` properties are left unmapped.
- Fix Python tooling if there are bugs, then close the issue.

## 4. Issues #385 - #375: Test Quality Audit
- Standard test re-organization and deletion. Follow instructions exactly as defined in the issue bodies. Delete duplicate SpecTests, create targeted `SuccessScaleTests.cs`.
