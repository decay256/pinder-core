# Conversation

## Overview
The Conversation module implements the core game loop for Pinder's dating-conversation mechanic. It manages turn flow (Speak, Read, Recover, Wait), interest tracking, dice rolls with advantage/disadvantage, traps, combos, timing, and game outcome resolution. The central class is `GameSession`, which orchestrates all player actions and NPC responses.

## Key Components

| File | Description |
|------|-------------|
| `GameSession.cs` | Core session state machine — manages turns, rolls, interest, traps, and game outcome |
| `GameSessionConfig.cs` | Configuration parameters for a game session (starting interest, etc.) |
| `InterestMeter.cs` | Tracks NPC interest level (0–25) and computes interest state, advantage/disadvantage |
| `InterestState.cs` | Enum for interest bands: Unmatched, Bored, Lukewarm, Interested, VeryIntoIt, AlmostThere, DateSecured |
| `TurnStart.cs` | Data returned by `StartTurnAsync()` — dialogue options, current state |
| `TurnResult.cs` | Data returned by `ResolveTurnAsync()` — roll result, interest change, outcome |
| `ReadResult.cs` | Data returned by `ReadAsync()` — SA roll result, interest reveal |
| `RecoverResult.cs` | Data returned by `RecoverAsync()` — roll result, trap recovery |
| `DialogueOption.cs` | A selectable dialogue choice for a Speak turn (includes `IsUnhinged` flag for Madness T3) |
| `DialogueContext.cs` | Context passed to the LLM adapter for generating dialogue options |
| `DeliveryContext.cs` | Context for evaluating delivery/timing of player responses |
| `ComboTracker.cs` | Tracks consecutive successes for combo bonuses |
| `ComboResult.cs` | Result of a combo evaluation |
| `CallbackOpportunity.cs` | Represents a callback opportunity during conversation |
| `CallbackBonus.cs` | Bonus granted from callbacks |
| `DelayPenalty.cs` | Penalty applied for slow player responses |
| `GameClock.cs` | Tracks in-game time progression |
| `GameOutcome.cs` | Final outcome of a session (DateSecured, Ghosted, etc.) |
| `GameEndedException.cs` | Exception thrown when actions are attempted after game end |
| `OpponentContext.cs` | NPC opponent configuration and state |
| `OpponentResponse.cs` | NPC response data |
| `OpponentTimingCalculator.cs` | Calculates NPC response timing |
| `PlayerResponseDelayEvaluator.cs` | Evaluates player response delay for penalty calculation |
| `Tell.cs` | Represents a behavioral tell from the NPC |
| `TimingProfile.cs` | Timing configuration for opponent responses |
| `WeaknessWindow.cs` | Represents a window where the NPC is vulnerable |
| `NullLlmAdapter.cs` | No-op LLM adapter for testing |
| `GameStateSnapshot.cs` | Serializable snapshot of game state |

## API / Public Interface

### `GameSession`

- **`StartTurnAsync() → Task<TurnStart>`** — Begins a Speak turn. Computes advantage from interest state and `_pendingCritAdvantage`. Returns dialogue options.
- **`ResolveTurnAsync(int optionIndex) → Task<TurnResult>`** — Resolves the selected dialogue option with a dice roll. Sets `_pendingCritAdvantage` if the roll is a Nat 20. Applies Denial +1 shadow growth if an Honesty option was available but the player chose a different stat (§7).
- **`ReadAsync() → Task<ReadResult>`** — Self-contained action: rolls SA against DC 12 to reveal interest. Consumes and sets `_pendingCritAdvantage` independently.
- **`RecoverAsync() → Task<RecoverResult>`** — Self-contained action: rolls to recover from an active trap. Consumes and sets `_pendingCritAdvantage` independently.
- **`Wait()`** — Skips a turn: applies −1 interest, advances trap timers. Does **not** consume `_pendingCritAdvantage`.

### `DialogueOption`

- **Constructor:** `DialogueOption(StatType stat, string intendedText, int? callbackTurnNumber = null, string? comboName = null, bool hasTellBonus = false, bool hasWeaknessWindow = false, bool isUnhingedReplacement = false)`
- **`IsUnhingedReplacement`** — `true` when Madness T3 (≥18) has replaced this option with unhinged text. The option's `Stat` and `IntendedText` are preserved; only the flag changes.

### `InterestMeter`

- **`GetState() → InterestState`** — Maps current value to a state: 0 = Unmatched, 1–4 = Bored, 5–9 = Lukewarm, 10–15 = Interested, 16–20 = VeryIntoIt, 21–24 = AlmostThere, 25 = DateSecured.
- **`GrantsAdvantage`** — `true` when interest state is VeryIntoIt or AlmostThere.
- **`GrantsDisadvantage`** — `true` when interest state is Bored.
- **`StartingValue`** — `10` (Interested state).

### `GameSessionConfig`

- Constructor accepts `startingInterest` and other session parameters.

## Architecture Notes

- **Turn flow:** The player calls `StartTurnAsync()` → `ResolveTurnAsync()` for Speak actions, or calls `ReadAsync()` / `RecoverAsync()` / `Wait()` as standalone actions.
- **Advantage sources:** Advantage is boolean (not cumulative). Sources include interest-based (`InterestMeter.GrantsAdvantage`) and crit-based (`_pendingCritAdvantage`). When both advantage and disadvantage are active, they cancel out to a normal roll.
- **Crit advantage (`_pendingCritAdvantage`):** A private boolean flag on `GameSession`. Set to `true` after any roll produces a Nat 20 (`RollResult.IsNatTwenty`). Consumed (grants advantage, then cleared to `false`) at the start of the next roll in `StartTurnAsync`, `ReadAsync`, or `RecoverAsync`. `Wait()` does not consume it. The flag is per-session and does not persist across sessions.
- **Roll mechanics:** Delegated to `RollEngine.Resolve()` and `RollEngine.ResolveFixedDC()` in the Rolls module. When advantage is active, two dice are rolled and the higher is used.
- **Madness T3 unhinged option (§7):** In `StartTurnAsync()`, after the Denial T3 block and before the Horniness T3 block, if Madness ≥18, one random option is replaced with a new `DialogueOption` that has `IsUnhingedReplacement = true`. The random index is selected via `_dice.Roll(options.Length) - 1`. The option's `Stat` and `IntendedText` are preserved so the roll is mechanically unchanged. Empty option lists are safely skipped. The Horniness T3 block (which runs after) preserves the `IsUnhingedReplacement` flag when reconstructing options as Rizz. `DialogueOption` is immutable, so the replacement creates a new instance copying all properties.
- **Denial shadow growth on skipped Honesty (§7):** In `ResolveTurnAsync()`, after determining `chosenOption`, if `_playerShadows` is non-null, `chosenOption.Stat != StatType.Honesty`, and any option in `_currentOptions` has `Stat == StatType.Honesty`, then `_playerShadows.ApplyGrowth(ShadowStatType.Denial, 1, "Skipped Honesty option")` is called. This is a boolean check (exactly +1 per turn regardless of how many Honesty options exist). The growth event flows into `TurnResult.ShadowGrowthEvents` via the existing `DrainGrowthEvents()` call. When Honesty is absent from the lineup (e.g., removed by Denial T3 threshold or Horniness-forced Rizz), no Denial growth occurs.

## Change Log
| Date | Issue | Summary |
|------|-------|---------|
| 2026-04-03 | #271 | Initial creation — Added `_pendingCritAdvantage` flag to `GameSession`: Nat 20 on any roll grants advantage on the next roll (§4). Consumed in `StartTurnAsync`, `ReadAsync`, `RecoverAsync`; persists through `Wait()`. Tests cover Speak→Speak, Speak→Read, Read→Speak, Recover→Speak, consecutive Nat 20s, Wait persistence, and advantage+disadvantage cancellation. |
| 2026-04-03 | #272 | Denial +1 when player skips available Honesty option (§7). Added shadow growth check in `ResolveTurnAsync()` — if Honesty is in the lineup and player picks a different stat, `ApplyGrowth(Denial, 1)` is called. Null-guarded for sessions without shadow tracking. Existing shadow-reduction tests updated to use Honesty-free option lineups to isolate from this new trigger. |
| 2026-04-03 | #273 | Madness T3 (≥18) replaces one random dialogue option with unhinged text (§7). Added `IsUnhingedReplacement` bool property to `DialogueOption` (default `false`, backward-compatible). In `StartTurnAsync()`, Madness T3 block selects a random option via `IDiceRoller` and replaces it with `IsUnhingedReplacement=true`. Horniness T3 block updated to preserve `IsUnhingedReplacement` when reconstructing options. Processing order: Fixation T3 → Denial T3 → Madness T3 → Horniness T3. |
| 2026-04-03 | #310 | Corrected property name from `IsUnhinged` to `IsUnhingedReplacement` in docs (matching actual code). Added comprehensive test coverage via `MadnessT3UnhingedSpecTests.cs`. |
| 2026-04-03 | #313 | Added `Lukewarm` (5–9) as a distinct `InterestState` per rules §6. Previously, Interested covered 5–15; now Lukewarm covers 5–9 and Interested covers 10–15. Lukewarm grants neither advantage nor disadvantage. `InterestState` enum now has 7 values. Tests in `Issue313_LukewarmInterestStateTests.cs`. |
| 2026-04-03 | #352 | `InterestChangeContext` gains `OpponentPrompt` property (`string?`, default `null`) so interest change beats (§3.8) can be generated in the opponent's character voice. `GameSession.ResolveTurnAsync` now passes `_opponent.AssembledSystemPrompt` when constructing `InterestChangeContext`. Backward-compatible — null prompt falls back to generic beats. |
| 2026-04-06 | #573 | Removed LLM API call for NarrativeBeat generation to maintain stateless generation. `GameSession` now sets `TurnResult.NarrativeBeat` to a simple UI string signal on interest state changes. Deleted `InterestChangeContext`. |
