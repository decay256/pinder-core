# Contract: Issue #52 — Trap Taint Injection

## Component
`Pinder.Core.Conversation.GameSession` (modified — plumb LLM instructions through contexts)
`Pinder.Core.Data.JsonTrapRepository` (new — load traps from JSON)

## Maturity
Prototype

---

## What Changes

### GameSession: Plumb LLM Instructions

Currently, `GameSession` passes trap names to `DialogueContext.ActiveTraps` and trap LLM instructions to `DeliveryContext.ActiveTraps`. This is inconsistent.

After this issue:
1. `DialogueContext` receives `ActiveTrapInstructions` (the full LLM instruction text from `TrapDefinition.LlmInstruction`) via the new field added in #63
2. All three LLM context types (Dialogue, Delivery, Opponent) receive trap instructions
3. The `NullLlmAdapter` doesn't need to change — it ignores instructions

### How to populate

In `GameSession`, when building any LLM context:
```csharp
var trapInstructions = _traps.AllActive
    .Select(t => t.Definition.LlmInstruction)
    .Where(s => !string.IsNullOrEmpty(s))
    .ToList();
```

Pass as `activeTrapInstructions: trapInstructions` to the new context fields from #63.

### JsonTrapRepository (optional for prototype)

**File**: `src/Pinder.Core/Data/JsonTrapRepository.cs`

```csharp
public sealed class JsonTrapRepository : ITrapRegistry
{
    private readonly Dictionary<StatType, TrapDefinition> _traps;

    public JsonTrapRepository(string json) { /* parse with JsonParser */ }

    public TrapDefinition? GetTrap(StatType stat) => _traps.TryGetValue(stat, out var t) ? t : null;
}
```

If `data/traps/traps.json` doesn't exist yet in the repo, skip the JSON repository and document the expected format. The mechanical plumbing through ILlmAdapter contexts is the core deliverable.

---

## Behavioural Contract
- When traps are active, ALL LLM calls receive the trap's `LlmInstruction` text
- When no traps are active, instruction lists are empty (not null)
- Trap instructions apply to ALL messages — not just rolls using the trapped stat
- This is a plumbing change — no new game mechanics

## Dependencies
- #63 (context types must have `ActiveTrapInstructions` fields)

## Consumers
LLM adapter implementations (they read `ActiveTrapInstructions` to inject trap flavor)
