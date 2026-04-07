**Module**: docs/modules/session-runner.md

## Overview

The `session-runner` playtest log output will be enriched with inline rule explanations. Whenever a mechanical event occurs (a fail tier, trap activation, combo trigger, shadow growth, or Nat 1 / Nat 20), the log will insert a blockquote `> 📋 *Explanation*` line. This clarifies the game logic and consequences directly in the playtest transcript, pulling from the `RuleBook` (enriched YAML) where possible, with hard-coded fallbacks.

## Function Signatures

In `Pinder.SessionRunner` (e.g. within a new `RuleExplanationFormatter` class or as static methods added to `PlaytestFormatter`):

```csharp
namespace Pinder.SessionRunner
{
    public sealed class RuleExplanationFormatter
    {
        public RuleExplanationFormatter(Pinder.Rules.RuleBook? ruleBook);

        /// <summary>Formats the rule explanation blockquote for a fail tier.</summary>
        public string FormatFailTierExplanation(Pinder.Core.Rolls.FailureTier tier);

        /// <summary>Formats the rule explanation blockquote for a combo.</summary>
        public string FormatComboExplanation(string comboName);

        /// <summary>Formats the rule explanation blockquote for a trap.</summary>
        public string FormatTrapExplanation(Pinder.Core.Traps.TrapDefinition trap);

        /// <summary>Formats the rule explanation blockquote for shadow growth.</summary>
        public string FormatShadowGrowthExplanation(string shadowType);

        /// <summary>Formats the rule explanation blockquote for a Natural 1.</summary>
        public string FormatNat1Explanation();

        /// <summary>Formats the rule explanation blockquote for a Natural 20.</summary>
        public string FormatNat20Explanation();
    }
}
```

## Input/Output Examples

**Fail Tier (Misfire)**
- Input: `FailureTier.Misfire`
- Output: `"> 📋 *Misfire: The message goes sideways. Interest −1. No trap.*"`

**Combo (The Pivot)**
- Input: `"The Pivot"`
- Output: `"> 📋 *The Pivot (Honesty → Chaos): Emotional whiplash that lands. +1 bonus Interest on top of roll gain.*"`

**Trap (The Spiral)**
- Input: `new TrapDefinition { Id = "the_spiral", Stat = StatType.SelfAwareness }`
- Output: `"> 📋 *The Spiral: Self-awareness collapses inward. All options carry meta-commentary. Clear: Read action (SA vs DC 12, uses your turn, −1 Interest on fail).*"`

**Shadow Growth (Fixation)**
- Input: `"Fixation"`
- Output: `"> 📋 *Fixation penalizes Chaos. At 6: Chaos options feel calculated. At 12: Chaos disadvantage. At 18: forced to repeat last stat.*"`

**Nat 1**
- Input: *(none)*
- Output: `"> 📋 *Legendary Fail: Automatic fail. −4 Interest. Trap fires. Shadow +1 on paired stat. Unique disaster — the character's worst impulse surfaces fully.*"`

## Acceptance Criteria

1. **Roll outcome**: Roll outcome line is followed by a 📋 explanation blockquote (including fail tier name, interest delta, trap note).
2. **Combo fires**: Combo triggers are followed by a 📋 explanation blockquote (including the sequence, bonus, and meaning).
3. **Trap activation**: Trap activations are followed by a 📋 explanation blockquote (including effect, duration, and how to clear).
4. **Shadow growth**: Shadow growth events are followed by a 📋 explanation blockquote (detailing what the shadow does at current level and next thresholds).
5. **Nat 1 and Nat 20**: These critical events are followed by a 📋 explanation blockquote.
6. **Data Source**: Explanations load from `RuleBook` enriched YAML where possible:
   - Fail tier descriptions: `§5.fail.{tier}` → `description` field
   - Trap descriptions: `§14.trap.{trap_id}` or from `data/traps/traps.json`
   - Combo descriptions: `§15.combo.{combo_name}` → `description` + `outcome.interest_bonus`
   - Shadow growth: `§7.shadow.{shadow_type}` threshold descriptions
   - Fallback: hard-coded short descriptions if rule book not loaded.
7. **Build clean**: Solution compiles with zero warnings or errors.

## Edge Cases

- **Missing RuleBook**: If `RuleBook` is null (e.g. YAML file not loaded or found by runner), the formatter must degrade gracefully to hard-coded fallback strings.
- **Missing Entry in RuleBook**: If the `RuleBook` exists but `ruleBook.GetById(id)` returns null for a specific key, the formatter must fall back to the hard-coded string.
- **Unrecognized Combo/Shadow**: If an unknown `comboName` or `shadowType` is passed, the formatter should return a generic explanation or an empty string, rather than crashing.
- **Missing Trap Data**: If `TrapDefinition` lacks a clear condition or description, default formatting should handle nulls gracefully.

## Error Conditions

- **No Exceptions Thrown**: The formatter methods must be pure and safe. Under no circumstances should formatting a log output throw an exception (e.g. `NullReferenceException`, `KeyNotFoundException`). All dictionary and object lookups must be null-conditional and safely defaulted.

## Dependencies

- **`Pinder.Rules.RuleBook`**: For querying YAML rule descriptions.
- **`Pinder.Core.Traps.TrapDefinition`**: For trap details.
- **`Pinder.Core.Rolls.FailureTier`**: For switch branching on roll failures.
