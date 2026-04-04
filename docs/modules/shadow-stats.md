# Shadow Stats

## Overview
The shadow stats system tracks six hidden "dark side" stats (`ShadowStatType`) that accumulate during gameplay and influence dialogue prompts, mechanical effects, and player options. `GameSession` stores **raw** shadow values (not tier indices) in the context dictionaries passed to `SessionDocumentBuilder`, which uses threshold comparisons on those raw values to inject shadow taint blocks into LLM prompts.

## Key Components

| File / Class | Description |
|---|---|
| `src/Pinder.Core/Stats/ShadowStatType.cs` | Enum: `Dread`, `Denial`, `Fixation`, `Madness`, `Overthinking`, `Horniness`. |
| `src/Pinder.Core/Stats/SessionShadowTracker.cs` | Tracks per-session shadow stat values; constructed from a `StatBlock`. |
| `src/Pinder.Core/Stats/StatBlock.cs` | Holds both primary stats and shadow stats as dictionaries. |
| `src/Pinder.Core/Conversation/GameSession.cs` | Populates `ShadowThresholds` on `DialogueContext`, `DeliveryContext`, and `OpponentContext` with **raw** shadow values (not tiers). |
| `src/Pinder.Core/Conversation/GameSessionConfig.cs` | Accepts optional `playerShadows` and `opponentShadows` (`SessionShadowTracker?`). |
| `src/Pinder.Core/Conversation/DialogueContext.cs` | Carries `ShadowThresholds` (`Dictionary<ShadowStatType, int>?`) to prompt builders. |
| `src/Pinder.Core/Conversation/DeliveryContext.cs` | Carries `ShadowThresholds` for delivery prompt generation. |
| `src/Pinder.Core/Conversation/OpponentContext.cs` | Carries `ShadowThresholds` for opponent prompt generation. |
| `src/Pinder.LlmAdapters/SessionDocumentBuilder.cs` | `BuildShadowTaintBlock` — emits a `SHADOW STATE` section in prompts when raw shadow values exceed thresholds (e.g., most stats > 5, Horniness > 6). |
| `tests/Pinder.Core.Tests/Issue307_ShadowTaintRawValueTests.cs` | Tests that `GameSession` passes raw shadow values (not tiers 0-3) in context dictionaries. |
| `tests/Pinder.LlmAdapters.Tests/Issue307_ShadowTaintFiringTests.cs` | Tests that `SessionDocumentBuilder` fires shadow taint blocks at correct raw-value thresholds. |
| `tests/Pinder.Core.Tests/MadnessT3UnhingedSpecTests.cs` | Tests for Madness T3 (≥18) unhinged option replacement — covers threshold boundary, stat/text preservation, single/empty options, Fixation T3 interaction. |
| `tests/Pinder.Core.Tests/Issue308_ShadowThresholdWiringSpecTests.cs` | Tests that `GameSession` wires player shadow thresholds to `DeliveryContext` and opponent shadow thresholds to `OpponentContext`. Covers cross-wiring guards, null when unconfigured, all 6 stat types, and zero-value passthrough. |
| `tests/Pinder.Core.Tests/ShadowGrowthSpecTests.cs` | Tests for per-turn and end-of-game shadow growth triggers, including Fixation growth from repeated same-stat and highest-probability-option picks. |

## API / Public Interface

### ShadowStatType (enum)
```csharp
public enum ShadowStatType { Dread, Denial, Fixation, Madness, Overthinking, Horniness }
```

### Context Dictionaries
All three LLM context types expose:
```csharp
public Dictionary<ShadowStatType, int>? ShadowThresholds { get; }
```
- `null` when no `SessionShadowTracker` is configured.
- Contains **raw** integer values (e.g., 8 for Madness), not tier indices (0-3).

### SessionDocumentBuilder Taint Thresholds
- Most shadow stats: taint fires when raw value **> 5**.
- `Horniness`: taint fires when raw value **> 6**.
- When fired, a `SHADOW STATE` section is injected into dialogue, delivery, and opponent prompts.

### Mechanical Effects (T3)
- `Denial ≥ 18` (T3): Honesty dialogue options are removed from the player's choices.
- `Madness ≥ 18` (T3): One random dialogue option is replaced with an unhinged variant (`IsUnhingedReplacement = true`). The option's `Stat` and `IntendedText` are preserved; only the flag changes. Selection index is `_dice.Roll(options.Length) - 1`. Empty option lists are safely skipped.

### Shadow Growth — IsHighestProbabilityOption (private)
```csharp
private bool IsHighestProbabilityOption(DialogueOption chosen, DialogueOption[] options)
```
Computes success probability margins (`statMod + levelBonus - defenceDC`) for each option and returns `true` if the chosen option has the highest (or tied-highest) margin. Used by per-turn Fixation growth: picking the highest-probability option 3 turns in a row contributes to Fixation accumulation.

## Architecture Notes
- **Raw values, not tiers**: Prior to issue #307, `GameSession` stored tier indices (0-3) in `ShadowThresholds`. Since `SessionDocumentBuilder.BuildShadowTaintBlock` compares against raw thresholds (> 5), the taint block never fired. The fix ensures raw values flow end-to-end from `SessionShadowTracker` through context objects to the prompt builder.
- Shadow stats are orthogonal to primary stats (`StatType`). They affect prompt generation (taint blocks) and have discrete mechanical effects at tier boundaries (e.g., Denial T3 removes Honesty options, Overthinking T2+ applies SA disadvantage on Read/Recover).
- `SessionShadowTracker` is optional — when absent, all `ShadowThresholds` are `null` and no taint processing occurs.
- **Separate player/opponent trackers**: `GameSessionConfig` accepts both `playerShadows` and `opponentShadows`. `DeliveryContext` receives the **player's** shadow thresholds; `OpponentContext` receives the **opponent's** shadow thresholds. When only one tracker is configured, the other context's `ShadowThresholds` is `null`. All-zero shadow values still produce a non-null dictionary (not treated as absent).

## Change Log
| Date | Issue | Summary |
|------|-------|---------|
| 2026-04-03 | #307 | Initial creation — documented shadow taint raw-value fix. GameSession now stores raw shadow values instead of tiers (0-3), allowing BuildShadowTaintBlock threshold checks (> 5) to fire correctly. |
| 2026-04-03 | #310 | Added Madness T3 (≥18) mechanical effect documentation and test file (`MadnessT3UnhingedSpecTests.cs`). Tests cover threshold boundary (17 vs 18), stat/text preservation, single/empty option edge cases, and Fixation T3 interaction. |
| 2026-04-03 | #308 | Verified shadow threshold wiring to `DeliveryContext` (player) and `OpponentContext` (opponent). `GameSessionConfig` accepts `opponentShadows`. New test file `Issue308_ShadowThresholdWiringSpecTests.cs` (443 lines) — 13 tests covering correct routing, cross-wiring guards, null safety, all 6 stat types, and zero-value passthrough. |
| 2026-04-04 | #349 | Added mutation tests in `ShadowGrowthSpecTests.cs` to verify `IsHighestProbabilityOption` uses actual stat margins (not option index) when determining Fixation shadow growth. Two new tests: highest-prob at non-zero index triggers Fixation; picking lower-prob option does not trigger highest-% Fixation. No production code changes — existing implementation was already correct. |
