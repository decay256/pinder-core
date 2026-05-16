# FailureTierDisplay: Per-Kind Player-Facing Label

**Issue:** pinder-core #904  
**Added in:** `Pinder.Core/Rolls/FailureTierDisplay.cs`  
**Status:** Shipped

## Summary

`FailureTierDisplay.Label(FailureTier tier, RollCheckKind kind)` returns the
player-facing string for a failure tier in the context of a specific check kind.

## Why Enum and Wire Stay Unchanged

`FailureTier.TropeTrap` is kept as-is (enum name, YAML key `trope_trap`, wire DTO
field) because it is the correct mechanical identity — **a miss by 6–9 points**.
The enum value, serialization key, and any persistence / network contract are all
literal `TropeTrap` / `trope_trap` and must stay that way.

Only the **player-facing display string** is kind-dependent.

## Why the Display Diverges Per Kind

On an **OptionRoll**, a `TropeTrap` result actually fires a stat-specific social
trap (the trope mechanic). Showing "TropeTrap" to the player is accurate — it
tells them the trap activated.

On a **Horniness / Shadow / ShadowGrowth / Steering** check, no trap fires. The
tier just means "miss margin 6–9, more damaging than Misfire but not catastrophic".
Showing "TropeTrap" implies a trap fired when one did not; it is confusing and
breaks narrative coherence. The chosen label **"Severe"** is:
- Consistent with the rest of the tier scale (None / Fumble / Misfire / **Severe** / Catastrophe / Legendary).
- Unambiguous: no trap connotation.
- Crisp: one word, same visual weight as the other labels.

## API Contract

```csharp
// src/Pinder.Core/Rolls/FailureTierDisplay.cs
public static string Label(FailureTier tier, RollCheckKind kind);
```

| Inputs | Return value |
|---|---|
| `TropeTrap`, `OptionRoll` | `"TropeTrap"` |
| `TropeTrap`, `Horniness` | `"Severe"` |
| `TropeTrap`, `Shadow` | `"Severe"` |
| `TropeTrap`, `ShadowGrowth` | `"Severe"` |
| `TropeTrap`, `Steering` | `"Severe"` |
| Any other tier, any kind | `tier.ToString()` |

All tier values other than `TropeTrap` pass through `tier.ToString()` — they are
unambiguous across all check kinds.

## SessionDocumentBuilder — No Routing Required

`SessionDocumentBuilder.GetFailureTierName()` (line 554) and `GetTierInstruction()`
(line 567) return LLM-internal prompt codes (`"TROPE_TRAP"` with underscore, all-caps),
not player-facing labels. These are exclusively called from `BuildDeliveryPrompt` and
`BuildOpponentPrompt`, both of which are pure option-roll paths (`DeliveryContext`
carries `ChosenOption`; `OpponentContext` carries `DeliveryTier` from a completed
option roll). Routing these through `FailureTierDisplay.Label()` would change the
LLM instruction format (from `"TROPE_TRAP"` to `"TropeTrap"` / `"Severe"`) and
would be a behavior change for the option-roll prompt, not a display fix.
Neither site needs modification.

## Frontend Follow-Up

pinder-web must mirror this helper when rendering failure-tier labels in the game UI
(turn history, live result banners, etc.). Frontend ticket: see pinder-web#TBD.

## Do Not

- Do not rename `FailureTier.TropeTrap` enum value.
- Do not change YAML keys (`trope_trap`).
- Do not change wire DTO fields.
- Do not change `GetFailureTierName()` in `SessionDocumentBuilder` — it returns
  LLM prompt codes, not display labels, and must remain `"TROPE_TRAP"`.
