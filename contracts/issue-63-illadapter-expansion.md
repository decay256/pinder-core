# Contract: Issue #63 — ILlmAdapter Expansion (Prerequisite)

## Component
`Pinder.Core.Interfaces.ILlmAdapter` (modified)
`Pinder.Core.Conversation.OpponentResponse` (new)
`Pinder.Core.Conversation.Tell` (new stub)
`Pinder.Core.Conversation.WeaknessWindow` (new stub)
`Pinder.Core.Conversation.CallbackOpportunity` (new stub)
`Pinder.Core.Conversation.DialogueContext` (modified — new optional fields)
`Pinder.Core.Conversation.DeliveryContext` (modified — new optional fields)
`Pinder.Core.Conversation.OpponentContext` (modified — new optional fields)
`Pinder.Core.Conversation.NullLlmAdapter` (modified)
`Pinder.Core.Conversation.GameSession` (modified — use `.MessageText`)

## Maturity
Prototype

## NFR
- Latency: N/A (data types only)

## Platform Constraints
- netstandard2.0, LangVersion 8.0. **No `record` types.** Use `sealed class`.
- Nullable enabled
- Zero NuGet dependencies

---

## New Types

### OpponentResponse
**File**: `src/Pinder.Core/Conversation/OpponentResponse.cs`

```csharp
public sealed class OpponentResponse
{
    public string MessageText { get; }
    public Tell? DetectedTell { get; }
    public WeaknessWindow? DetectedWeakness { get; }

    public OpponentResponse(string messageText, Tell? detectedTell = null, WeaknessWindow? detectedWeakness = null)
    {
        MessageText = messageText ?? throw new ArgumentNullException(nameof(messageText));
        DetectedTell = detectedTell;
        DetectedWeakness = detectedWeakness;
    }
}
```

### Tell
**File**: `src/Pinder.Core/Conversation/Tell.cs`

```csharp
public sealed class Tell
{
    public StatType Stat { get; }
    public string Description { get; }

    public Tell(StatType stat, string description)
    {
        Stat = stat;
        Description = description ?? throw new ArgumentNullException(nameof(description));
    }
}
```

### WeaknessWindow
**File**: `src/Pinder.Core/Conversation/WeaknessWindow.cs`

```csharp
public sealed class WeaknessWindow
{
    public StatType DefendingStat { get; }
    public int DcReduction { get; }

    public WeaknessWindow(StatType defendingStat, int dcReduction)
    {
        DefendingStat = defendingStat;
        DcReduction = dcReduction;
    }
}
```

### CallbackOpportunity
**File**: `src/Pinder.Core/Conversation/CallbackOpportunity.cs`

```csharp
public sealed class CallbackOpportunity
{
    public string TopicKey { get; }
    public int TurnIntroduced { get; }

    public CallbackOpportunity(string topicKey, int turnIntroduced)
    {
        TopicKey = topicKey ?? throw new ArgumentNullException(nameof(topicKey));
        TurnIntroduced = turnIntroduced;
    }
}
```

---

## Interface Change

### ILlmAdapter — BEFORE
```csharp
Task<string> GetOpponentResponseAsync(OpponentContext context);
```

### ILlmAdapter — AFTER
```csharp
Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context);
```

**All other methods unchanged.**

---

## Context Type Modifications

All new fields use **constructor parameter defaults** so existing callers compile without changes.

### DialogueContext — New Fields
```csharp
// Added to constructor with defaults:
Dictionary<ShadowStatType, int>? shadowThresholds = null,   // for #44/#45
List<CallbackOpportunity>? callbackOpportunities = null,     // for #47
int horninessLevel = 0,                                       // for #51
bool requiresRizzOption = false,                              // for #51
List<string>? activeTrapInstructions = null                   // for #52 (full LLM instructions, not just names)
```

**Important**: The existing `ActiveTraps` field (trap names as `IReadOnlyList<string>`) stays. `ActiveTrapInstructions` is a separate field carrying full LLM instruction text. Feature #52 will populate it.

### DeliveryContext — New Fields
```csharp
Dictionary<ShadowStatType, int>? shadowThresholds = null,
List<string>? activeTrapInstructions = null
```

**Note**: `DeliveryContext` already has `ActiveTraps` as `IReadOnlyList<string>` carrying LLM instructions. The new `ActiveTrapInstructions` field on `DialogueContext` is the one that replaces names-only. For `DeliveryContext`, `ActiveTraps` already carries instructions, so `ShadowThresholds` is the only genuinely new field. Adding `ActiveTrapInstructions` on `DeliveryContext` for consistency is fine but redundant.

### OpponentContext — New Fields
```csharp
Dictionary<ShadowStatType, int>? shadowThresholds = null,
List<string>? activeTrapInstructions = null
```

---

## NullLlmAdapter Update

```csharp
public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
{
    return Task.FromResult(new OpponentResponse("..."));
}
```

---

## GameSession Update

In `ResolveTurnAsync`, change:
```csharp
// BEFORE
string opponentMessage = await _llm.GetOpponentResponseAsync(opponentContext);
_history.Add((_opponent.DisplayName, opponentMessage));
// ...
return new TurnResult(..., opponentMessage: opponentMessage, ...);

// AFTER
var opponentResponse = await _llm.GetOpponentResponseAsync(opponentContext);
_history.Add((_opponent.DisplayName, opponentResponse.MessageText));
// Store opponentResponse.DetectedTell and .DetectedWeakness for next turn
// ...
return new TurnResult(..., opponentMessage: opponentResponse.MessageText, ...);
```

---

## Behavioural Contract
- `OpponentResponse.MessageText` MUST be non-null, non-empty
- `Tell` and `WeaknessWindow` are nullable on OpponentResponse — null means "none detected"
- All new context fields default to null/0/false — existing callers unaffected
- All 98 existing tests must pass after this change

## Dependencies
None (prerequisite — goes first)

## Consumers
All sprint issues that touch ILlmAdapter: #49, #50, #51, #52
All sprint issues that need context fields: #44, #45, #47
