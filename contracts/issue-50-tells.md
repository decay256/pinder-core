# Contract: Issue #50 — Tells

## Component
Integration of `Tell` into `GameSession` turn flow

## Maturity
Prototype

---

## Behavioral Contract

Tells are hidden +2 roll bonuses triggered when the player picks a stat that matches a tell detected in the previous opponent response.

### Flow

1. **OpponentResponse** from `ILlmAdapter.GetOpponentResponseAsync()` already contains `Tell?` field (`DetectedTell`).
2. `GameSession` stores the `Tell` from the current turn's opponent response.
3. On the **next** `StartTurnAsync`, if a tell is active:
   - The matching `DialogueOption` is annotated with `HasTellBonus = true` (already exists on `DialogueOption`).
4. On `ResolveTurnAsync`, if the chosen option's stat matches the tell's `Stat`:
   - Add +2 to the roll via `rollResult.AddExternalBonus(2)`.
   - Set `TurnResult.TellReadBonus = 2`.
   - Set `TurnResult.TellReadMessage = "📖 You read the moment. +2 bonus."`.
5. The tell is consumed (cleared) after the turn, regardless of whether it was matched.

### Tell Table

| Opponent Does | Tell Stat |
|---|---|
| Compliments you | Honesty |
| Asks a personal question | Honesty or SA |
| Makes a joke | Wit or Chaos |
| Shares something vulnerable | Honesty |
| Pulls back / gets guarded | Charm |
| Sends something flirty | Rizz |

Note: The LLM chooses which tell to return. The engine only needs to match `Tell.Stat` against the chosen option's stat. Multi-stat tells (e.g., "Honesty or SA") are resolved by the LLM returning ONE specific stat.

### State Tracking in GameSession

```csharp
// Private field:
private Tell? _pendingTell;

// Set at end of ResolveTurnAsync (from opponent response):
_pendingTell = opponentResponse.DetectedTell;

// Used in next ResolveTurnAsync to check for match
// Consumed after use (set to null)
```

## Dependencies
- `Tell` (already exists)
- `OpponentResponse.DetectedTell` (already exists)
- `RollResult.AddExternalBonus()` (from #135, merged)
- `DialogueOption.HasTellBonus` (already exists)
- `TurnResult.TellReadBonus`, `TurnResult.TellReadMessage` (already exist)

## Consumers
- `GameSession` (internal state management)
- UI host (reads `HasTellBonus` on options — tells are invisible pre-roll, revealed post-roll via TurnResult)
