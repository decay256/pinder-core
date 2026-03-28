# Contract: Issue #49 — Weakness Windows

## Component
Integration of `WeaknessWindow` into `GameSession` turn flow

## Maturity
Prototype

---

## Behavioral Contract

Weakness windows are one-turn DC reductions triggered by the LLM detecting "cracks" in the opponent's last response. The mechanic works as follows:

### Flow

1. **OpponentResponse** from `ILlmAdapter.GetOpponentResponseAsync()` already contains `WeaknessWindow?` field.
2. `GameSession` stores the `WeaknessWindow` from the current turn's opponent response.
3. On the **next** `StartTurnAsync`, if a weakness window is active:
   - The matching `DialogueOption` is annotated with `HasWeaknessWindow = true` (new bool on `DialogueOption`).
   - The `DialogueContext` carries the weakness window info so the LLM can reference it.
4. On `ResolveTurnAsync`, if the chosen option's stat matches the weakness window's `DefendingStat`:
   - The DC is reduced by `WeaknessWindow.DcReduction` (applied via `RollEngine.Resolve(..., dcAdjustment: window.DcReduction)` from Wave 0).
5. The weakness window is consumed (cleared) after the turn, regardless of whether it was used.

### DialogueOption Addition

```csharp
// Add to DialogueOption constructor:
public bool HasWeaknessWindow { get; }

public DialogueOption(
    StatType stat,
    string intendedText,
    int? callbackTurnNumber = null,
    string? comboName = null,
    bool hasTellBonus = false,
    bool hasWeaknessWindow = false);  // NEW
```

### Crack → DC Reduction Table

| Opponent Behaviour | Defending Stat | DC Reduction |
|---|---|---|
| Contradicts themselves | Honesty | -2 |
| Laughs genuinely | Charm | -2 |
| Shares something personal unprompted | SA | -2 |
| Gets flustered | Rizz | -2 |
| Makes a risky joke | Wit | -2 |
| Asks a personal question | Honesty | -2 |

All reductions are -2 (uniform for prototype).

### State Tracking in GameSession

```csharp
// Private field:
private WeaknessWindow? _pendingWeaknessWindow;

// Set at end of ResolveTurnAsync (from opponent response):
_pendingWeaknessWindow = opponentResponse.WeaknessWindow;

// Used in next StartTurnAsync to annotate options
// Consumed in ResolveTurnAsync (set to null after use)
```

## Dependencies
- `WeaknessWindow` (already exists)
- `OpponentResponse.WeaknessWindow` (already exists)
- `RollEngine.Resolve(..., dcAdjustment)` (from Wave 0 #139)

## Consumers
- `GameSession` (internal state management)
- UI host (reads `HasWeaknessWindow` on options to show 🔓 icon)
