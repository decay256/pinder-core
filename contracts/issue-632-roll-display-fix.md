# Contract: Issue #632 — Roll Display Output Fix

## Component
`session-runner/Program.cs` (Playtest UI output)

## Description
The session runner currently displays all roll margins as "Miss: -N" or "Miss: +N". It must distinguish between a successful roll ("Beat by N") and a failed roll ("Miss by N"), and provide an explicit override message when a Natural 1 causes a failure despite the total exceeding the DC.

## Interface Changes
**File**: `session-runner/Program.cs` (near line 503)

**Change**:
Modify the string interpolation logic for the roll output.
Given `roll.FinalTotal`, `roll.DC`, `roll.UsedDieRoll`, and `rollResult`:
- If `roll.UsedDieRoll == 1`: Format as `"NAT 1 💀 (would have succeeded by {margin})" ` or similar override text. (Wait, it could be a miss even without Nat 1. Just `"NAT 1 💀 (Total {FinalTotal} vs DC {DC})"` or similar).
- Else if `roll.FinalTotal >= roll.DC`: Format as `"Beat by {roll.FinalTotal - roll.DC}"`.
- Else: Format as `"Miss by {roll.DC - roll.FinalTotal}"`.

**Output Example**:
`**🎲 Roll:** d20(1) + WIT(+10) = **17** vs DC 14 → **NAT 1 💀 (would have succeeded by 3) → Legendary**`
`**🎲 Roll:** d20(14) + CHARM(+2) = **16** vs DC 14 → **Beat by 2 → +1 Interest**`
`**🎲 Roll:** d20(4) + RIZZ(+0) = **4** vs DC 14 → **Miss by 10 → Catastrophe**`

**Constraints**:
- This is purely a UI display change in the console application. No changes to `Pinder.Core.Rolls.RollEngine`.
