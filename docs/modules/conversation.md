# Conversation

## Overview

The Conversation module implements Pinder's turn-based dating conversation. Its current action model is Speak (`StartTurnAsync` followed by `ResolveTurnAsync`) or Wait. It coordinates dialogue options, rolls, interest, traps, combos, shadows, datee responses, progression events, and terminal outcomes.

## Main Types

| Type | Role |
|---|---|
| `GameSession` | Public session facade and state owner. |
| `GameSessionConfig` | Required session dependencies and optional rule/configuration values. |
| `GameSessionState` | Mutable state used by orchestration stages. |
| `TurnStart` | Options and state returned before the player chooses. |
| `TurnResult` | Roll, messages, effects, progression, and state returned after resolution. |
| `DialogueOption` | A complete selectable player message and its mechanical metadata. |
| `DialogueContext` | Input to dialogue-option generation. |
| `DateeContext` / `DateeResponse` | Input and output for the datee-response call. |
| `GameStateSnapshot` | Serializable public state snapshot. |
| `TurnProgressEvent` | Coarse progress emitted while a selected turn is resolving. |
| `InterestMeter` | Interest value and band calculation. |
| `ComboTracker` | Combo and Triple tracking. |
| `TrapManager` | Active trap lifecycle. |

## Action Flow

### Speak

1. Call `StartTurnAsync` once.
2. Present its `DialogueOption[]` to the player.
3. Call `ResolveTurnAsync` with one valid option index.
4. Consume the returned `TurnResult` and state snapshot.

Calling resolution without an active started turn, using an invalid index, or starting another turn before resolving the active one violates the lifecycle contract.

### Wait

`Wait()` is self-contained and does not require `StartTurnAsync`. It has no roll and makes no LLM call. It clears pending Speak state, applies its interest/trap effects, and advances the turn.

There is no current standalone Read or Recover action. Historical references to `ReadAsync`, `RecoverAsync`, `ReadResult`, or `RecoverResult` describe a retired ruleset.

## Roll And Interest State

`ResolveTurnAsync` delegates to the roll-resolution pipeline. The result carries the attacking stat, defending stat, dice, modifiers, DC, verdict, failure tier, interest breakdown, and applied effects needed by API consumers.

Interest is clamped to the supported game range and mapped to an `InterestState`. Interest state and other active mechanics can influence advantage, DC, and consequences according to the loaded rules. Callers should use the returned snapshots and result fields instead of reimplementing those rules.

## Conversation History

The engine owns semantic conversation history. Only canonical delivered player messages and visible datee responses belong in that history. Prompt documents, discarded options, and transient model output are not conversation messages.

The contextual adapter overload receives engine-owned history on each datee-response call. It remains stateless across sessions.

## Timing

The retired player-response-delay penalty subsystem is not part of the current action model. `TimingProfile` concerns datee/NPC response presentation timing; it does not penalize the player's real-world response latency.
