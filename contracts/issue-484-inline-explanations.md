# Contract: Issue #484 — Session Runner Inline Explanations

## Component: `Pinder.SessionRunner`

### 1. `RuleExplanationProvider` (New static class)
- Parses descriptions out of the `RuleBook` loaded from YAML.
- `GetExplanationForFailTier(FailureTier tier)` -> `§5.fail.{tier}`
- `GetExplanationForCombo(string comboName)` -> `§15.combo.{comboName}`
- `GetExplanationForTrap(string trapName)`
- `GetExplanationForShadowGrowth(ShadowStatType shadow)`

### 2. `Program.cs`
- Check for mechanical events (combos, traps, shadow growth, fail tiers) on turn resolution.
- Print blockquotes `> 📋 *Explanation*` inline under the event in the playtest transcript.
