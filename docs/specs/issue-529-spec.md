# Specification: Issue #529

**Module**: docs/modules/session-runner.md

## Overview
The `session-runner` console application currently prints dialogue options using bracketed text strings (e.g., `[Safe]`, `[Medium]`) to denote the risk tier of a roll based on the required margin. This issue restores the visual `🟢🟡🟠🔴` emoji prefixes for these risk tiers by reusing the existing `RiskLabel()` helper method instead of maintaining an inline string assignment. This change improves at-a-glance readability of dialogue options during automated playtesting.

## Function Signatures
No new function signatures are introduced. The existing helper in `session-runner/Program.cs` is used:
```csharp
static string RiskLabel(int need);
```

The inline assignment in `session-runner/Program.cs`:
```csharp
string riskColor = need <= 5 ? "[Safe]" : need <= 10 ? "[Medium]" : need <= 15 ? "[Hard]" : "[Bold]";
```
will be replaced with a call to the helper:
```csharp
string riskColor = RiskLabel(need);
```

## Input/Output Examples
**Input:** `need = 4`
**Output before change:** `[Safe]`
**Output after change:** `🟢 Safe`

**Input:** `need = 9`
**Output before change:** `[Medium]`
**Output after change:** `🟡 Medium`

**Input:** `need = 15`
**Output before change:** `[Hard]`
**Output after change:** `🟠 Hard`

**Input:** `need = 17`
**Output before change:** `[Bold]`
**Output after change:** `🔴 Bold`

## Acceptance Criteria
- **Emoji Risk Colors Displayed:** 🟢🟡🟠🔴 emojis are shown before the stat name in the option header in playtest output.
- **Build Clean:** The application continues to build without compilation warnings or errors.

## Edge Cases
- **Negative `need` values:** If `need` drops below 0 (high stat mod, low DC), the condition `need <= 5` will match, resulting correctly in `🟢 Safe`.
- **Extremely large `need` values:** Handled securely by the final fallback in the ternary tree, yielding `🔴 Bold` for any `need > 15`.
- **Terminal Emoji Support:** Terminals running `session-runner` without full emoji rendering might show fallback characters, but the text label (e.g., "Safe", "Medium") is retained to guarantee accessibility.

## Error Conditions
- No logical error states can be thrown from this operation as it strictly relies on synchronous integer evaluations.

## Dependencies
- This feature is completely contained within the `session-runner` environment.
- No external libraries or `Pinder.Core` engine modifications are required.