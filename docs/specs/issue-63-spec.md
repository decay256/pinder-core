# Specification: Issue #63 — ILlmAdapter Expansion

## Overview

This issue adds new data types (`OpponentResponse`, `Tell`, `WeaknessWindow`, `CallbackOpportunity`) and extends existing LLM context types (`DialogueContext`, `DeliveryContext`, `OpponentContext`) with optional fields required by downstream Sprint 3 features. It also changes the return type of `ILlmAdapter.GetOpponentResponseAsync` from `Task<string>` to `Task<OpponentResponse>`. This is a **prerequisite** for the rest of Sprint 3 — it must land first, and all 98 existing tests must continue to pass.

## Function Signatures

### New Types

All types live in `Pinder.Core.Conversation`. All are `sealed class` (no `record` — netstandard2.0, LangVersion 8.0).

#### `OpponentResponse`

```csharp
namespace Pinder.Core.Conversation
{
    public sealed class OpponentResponse
    {
        public string MessageText { get; }
        public Tell? DetectedTell { get; }
        public WeaknessWindow? DetectedWeakness { get; }

        public OpponentResponse(
            string messageText,
            Tell? detectedTell = null,
            WeaknessWindow? detectedWeakness = null);
    }
}
```

- `messageText` must be non-null; throw `ArgumentNullException` if null.
- `detectedTell` and `detectedWeakness` are nullable and default to `null`.

#### `Tell`

```csharp
namespace Pinder.Core.Conversation
{
    public sealed class Tell
    {
        public StatType Stat { get; }
        public string Description { get; }

        public Tell(StatType stat, string description);
    }
}
```

- `description` must be non-null; throw `ArgumentNullException` if null.
- `Stat` is a `Pinder.Core.Stats.StatType` enum value (Charm, Rizz, Honesty, Chaos, Wit, SelfAwareness).

#### `WeaknessWindow`

```csharp
namespace Pinder.Core.Conversation
{
    public sealed class WeaknessWindow
    {
        public StatType DefendingStat { get; }
        public int DcReduction { get; }

        public WeaknessWindow(StatType defendingStat, int dcReduction);
    }
}
```

- `DefendingStat` is a `Pinder.Core.Stats.StatType` enum value.
- `DcReduction` is a positive integer representing how much the DC is lowered (expected values: 2 or 3 per §15 rules).

#### `CallbackOpportunity`

```csharp
namespace Pinder.Core.Conversation
{
    public sealed class CallbackOpportunity
    {
        public string TopicKey { get; }
        public int TurnIntroduced { get; }

        public CallbackOpportunity(string topicKey, int turnIntroduced);
    }
}
```

- `topicKey` must be non-null; throw `ArgumentNullException` if null.
- `TurnIntroduced` is the 1-based turn number when the topic first appeared.

### Modified Interface

#### `ILlmAdapter`

Only one method signature changes:

```csharp
// BEFORE
Task<string> GetOpponentResponseAsync(OpponentContext context);

// AFTER
Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context);
```

All other methods (`GetDialogueOptionsAsync`, `DeliverMessageAsync`, `GetInterestChangeBeatAsync`) remain unchanged.

### Modified Context Types

All new fields are added as **optional constructor parameters with defaults**, so existing callers compile without changes. Existing fields and their order are unchanged.

#### `DialogueContext` — New Fields

| Property | Type | Default | Purpose |
|----------|------|---------|---------|
| `ShadowThresholds` | `Dictionary<ShadowStatType, int>?` | `null` | Shadow threshold data for #44/#45 |
| `CallbackOpportunities` | `List<CallbackOpportunity>?` | `null` | Callback topics for #47 |
| `HorninessLevel` | `int` | `0` | Current horniness shadow value for #51 |
| `RequiresRizzOption` | `bool` | `false` | Whether at least one Rizz option must be included, for #51 |
| `ActiveTrapInstructions` | `List<string>?` | `null` | Full LLM trap instruction text for #52 (separate from existing `ActiveTraps` which carries trap names only) |

The existing `ActiveTraps` property (`IReadOnlyList<string>` of trap names) is retained unchanged.

#### `DeliveryContext` — New Fields

| Property | Type | Default | Purpose |
|----------|------|---------|---------|
| `ShadowThresholds` | `Dictionary<ShadowStatType, int>?` | `null` | Shadow threshold data |
| `ActiveTrapInstructions` | `List<string>?` | `null` | Full LLM trap instruction text |

Note: `DeliveryContext` already has `ActiveTraps` as `IReadOnlyList<string>` carrying LLM instructions (per existing code). The new `ActiveTrapInstructions` field is added for consistency with `DialogueContext`.

#### `OpponentContext` — New Fields

| Property | Type | Default | Purpose |
|----------|------|---------|---------|
| `ShadowThresholds` | `Dictionary<ShadowStatType, int>?` | `null` | Shadow threshold data |
| `ActiveTrapInstructions` | `List<string>?` | `null` | Full LLM trap instruction text |

### Modified Implementations

#### `NullLlmAdapter`

`GetOpponentResponseAsync` changes return type:

```csharp
// BEFORE
public Task<string> GetOpponentResponseAsync(OpponentContext context)
    => Task.FromResult("...");

// AFTER
public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
    => Task.FromResult(new OpponentResponse("..."));
```

All other methods remain identical.

#### `GameSession`

In `ResolveTurnAsync`, the call to `GetOpponentResponseAsync` changes from consuming a `string` to consuming an `OpponentResponse`:

```
// BEFORE
string opponentMessage = await _llm.GetOpponentResponseAsync(opponentContext);
_history.Add((_opponent.DisplayName, opponentMessage));
// ... uses opponentMessage in TurnResult

// AFTER
var opponentResponse = await _llm.GetOpponentResponseAsync(opponentContext);
_history.Add((_opponent.DisplayName, opponentResponse.MessageText));
// ... uses opponentResponse.MessageText in TurnResult
```

The `DetectedTell` and `DetectedWeakness` properties on `OpponentResponse` are **not consumed** by `GameSession` in this issue. They will be consumed by #49 (WeaknessWindow) and #50 (Tells) respectively. For now, they are simply ignored.

## Input/Output Examples

### Constructing OpponentResponse

```
// Minimal — no tell or weakness detected
var response = new OpponentResponse("Oh interesting, tell me more...");
// response.MessageText == "Oh interesting, tell me more..."
// response.DetectedTell == null
// response.DetectedWeakness == null

// With tell and weakness
var tell = new Tell(StatType.Charm, "She keeps mentioning confidence");
var weakness = new WeaknessWindow(StatType.Wit, 2);
var response = new OpponentResponse("Haha you're funny", tell, weakness);
// response.MessageText == "Haha you're funny"
// response.DetectedTell.Stat == StatType.Charm
// response.DetectedTell.Description == "She keeps mentioning confidence"
// response.DetectedWeakness.DefendingStat == StatType.Wit
// response.DetectedWeakness.DcReduction == 2
```

### Constructing CallbackOpportunity

```
var cb = new CallbackOpportunity("pizza-story", 3);
// cb.TopicKey == "pizza-story"
// cb.TurnIntroduced == 3
```

### DialogueContext with new optional fields

```
// Existing call — still works, new fields get defaults
var ctx = new DialogueContext(playerPrompt, opponentPrompt, history, lastMsg, activeTraps, 10);
// ctx.ShadowThresholds == null
// ctx.HorninessLevel == 0
// ctx.RequiresRizzOption == false
// ctx.CallbackOpportunities == null
// ctx.ActiveTrapInstructions == null

// New call with shadow thresholds
var shadows = new Dictionary<ShadowStatType, int> { { ShadowStatType.Horniness, 12 } };
var ctx = new DialogueContext(playerPrompt, opponentPrompt, history, lastMsg, activeTraps, 10,
    shadowThresholds: shadows, horninessLevel: 12, requiresRizzOption: true);
// ctx.ShadowThresholds[ShadowStatType.Horniness] == 12
// ctx.HorninessLevel == 12
// ctx.RequiresRizzOption == true
```

### NullLlmAdapter returns OpponentResponse

```
var adapter = new NullLlmAdapter();
OpponentResponse result = await adapter.GetOpponentResponseAsync(someContext);
// result.MessageText == "..."
// result.DetectedTell == null
// result.DetectedWeakness == null
```

## Acceptance Criteria

### AC1: `OpponentResponse` class exists

A new `sealed class OpponentResponse` exists in `Pinder.Core.Conversation` with:
- A `string MessageText` read-only property (non-null, enforced by `ArgumentNullException`)
- A `Tell? DetectedTell` read-only nullable property
- A `WeaknessWindow? DetectedWeakness` read-only nullable property
- A constructor taking `(string messageText, Tell? detectedTell = null, WeaknessWindow? detectedWeakness = null)`

### AC2: `Tell`, `WeaknessWindow`, `CallbackOpportunity` stub types exist

Three new `sealed class` types in `Pinder.Core.Conversation`:
- `Tell` with `StatType Stat` and `string Description` (non-null, enforced)
- `WeaknessWindow` with `StatType DefendingStat` and `int DcReduction`
- `CallbackOpportunity` with `string TopicKey` (non-null, enforced) and `int TurnIntroduced`

Each in its own `.cs` file.

### AC3: `ILlmAdapter.GetOpponentResponseAsync` returns `Task<OpponentResponse>`

The interface method signature is changed. This is a **breaking change** for all implementors of `ILlmAdapter`. Currently only `NullLlmAdapter` exists in the repo, so migration is trivial.

### AC4: All context types have new fields (nullable/defaulted — existing callers unaffected)

- `DialogueContext` gains 5 new optional constructor parameters (see table above)
- `DeliveryContext` gains 2 new optional constructor parameters
- `OpponentContext` gains 2 new optional constructor parameters
- All new parameters have defaults (`null`, `0`, or `false`)
- All new parameters are appended **after** existing parameters so positional callers are unaffected
- Each new parameter has a corresponding read-only property

### AC5: `NullLlmAdapter` updated to return `OpponentResponse`

`GetOpponentResponseAsync` returns `Task.FromResult(new OpponentResponse("..."))` instead of `Task.FromResult("...")`.

### AC6: `GameSession` updated to use `.MessageText`

In `GameSession.ResolveTurnAsync`, the line that calls `GetOpponentResponseAsync` and stores the result changes to extract `.MessageText` for the conversation history and `TurnResult`. No other behavioral changes to `GameSession`.

### AC7: All 98 existing tests still pass

The changes are purely additive (new types, optional fields) or mechanical (`.MessageText` extraction). No test should need modification. Run `dotnet test` and verify 98 pass, 0 fail.

### AC8: Build clean

`dotnet build` produces zero warnings and zero errors.

## Edge Cases

| Scenario | Expected Behavior |
|----------|-------------------|
| `new OpponentResponse(null)` | Throws `ArgumentNullException` with parameter name `"messageText"` |
| `new OpponentResponse("")` | Allowed — empty string is valid (LLM might return empty in edge cases) |
| `new Tell(StatType.Charm, null)` | Throws `ArgumentNullException` with parameter name `"description"` |
| `new CallbackOpportunity(null, 1)` | Throws `ArgumentNullException` with parameter name `"topicKey"` |
| `new WeaknessWindow(StatType.Wit, 0)` | Allowed — zero DC reduction is semantically a no-op but not invalid |
| `new WeaknessWindow(StatType.Wit, -1)` | Allowed — no validation on DcReduction range (stub type, validation deferred to consumers) |
| `new CallbackOpportunity("topic", -1)` | Allowed — no validation on TurnIntroduced (stub type) |
| Existing code calling `DialogueContext` constructor with positional args only | Compiles and runs unchanged — new params have defaults |
| Existing code calling `new NullLlmAdapter()` and using `GetOpponentResponseAsync` | Return type changes from `string` to `OpponentResponse` — callers must update to `.MessageText` |
| `OpponentResponse` with both `DetectedTell` and `DetectedWeakness` set | Both stored; no mutual exclusivity constraint |
| `DialogueContext.ShadowThresholds` passed as empty dictionary | Allowed — treated same as `null` by consumers (no thresholds to check) |

## Error Conditions

| Error | Type | When |
|-------|------|------|
| Null `messageText` in `OpponentResponse` constructor | `ArgumentNullException` | Always — `messageText` is required |
| Null `description` in `Tell` constructor | `ArgumentNullException` | Always — `description` is required |
| Null `topicKey` in `CallbackOpportunity` constructor | `ArgumentNullException` | Always — `topicKey` is required |
| Compilation errors in external `ILlmAdapter` implementors | Compile-time error | Any class implementing `ILlmAdapter` must update `GetOpponentResponseAsync` return type |

No runtime exceptions beyond null-argument validation. These are data-carrying types with no complex logic.

## Dependencies

### Internal (this repo)
- `Pinder.Core.Stats.StatType` — used by `Tell.Stat` and `WeaknessWindow.DefendingStat`
- `Pinder.Core.Stats.ShadowStatType` — used in `Dictionary<ShadowStatType, int>` for shadow threshold fields
- `Pinder.Core.Conversation.DialogueContext` — modified (new fields)
- `Pinder.Core.Conversation.DeliveryContext` — modified (new fields)
- `Pinder.Core.Conversation.OpponentContext` — modified (new fields)
- `Pinder.Core.Interfaces.ILlmAdapter` — modified (return type change)
- `Pinder.Core.Conversation.NullLlmAdapter` — modified (implements new signature)
- `Pinder.Core.Conversation.GameSession` — modified (`.MessageText` extraction)

### External
- None. Zero NuGet dependencies. .NET Standard 2.0.

### Downstream consumers (future issues that depend on this)
- **#49** (Weakness Windows) — consumes `OpponentResponse.DetectedWeakness`
- **#50** (Tells) — consumes `OpponentResponse.DetectedTell`
- **#51** (Horniness-forced Rizz) — uses `DialogueContext.HorninessLevel` and `.RequiresRizzOption`
- **#52** (Trap taint injection) — uses `ActiveTrapInstructions` fields on all context types
- **#44/#45** (Shadow growth/thresholds) — uses `ShadowThresholds` fields on all context types
- **#47** (Callback bonus) — uses `DialogueContext.CallbackOpportunities`

### Architect Contract Reference
`contracts/issue-63-illadapter-expansion.md` — this spec aligns with the architect's contract. Property name `DetectedWeakness` (contract) vs `WeaknessWindow` (issue body) — use `DetectedWeakness` as per the contract, which is the authoritative source.
