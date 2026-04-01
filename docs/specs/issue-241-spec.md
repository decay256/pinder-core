# Specification: Issue #241 — Legendary Fail Delivery Generates Wrong Character Voice

## Overview

When a player triggers a Legendary Fail (Nat 1), the LLM-generated delivery message is written in the **opponent's** voice instead of the **player's** voice. This happens because (1) `DeliverMessageAsync` sends both character system prompts to the LLM, causing voice contamination, (2) `FailureDeliveryInstruction` does not explicitly identify which character the LLM should write as, and (3) `GameSession` does not populate `playerName`/`opponentName`/`currentTurn` on the `DeliveryContext` or `OpponentContext` DTOs, leaving them at their empty-string defaults.

The fix requires changes in five files across `Pinder.Core` and `Pinder.LlmAdapters`: wiring name fields in `GameSession`, adding a player-only cache block builder method, switching the adapter's delivery call to player-only system blocks, adding player identity framing to the failure template, and adding `{player_name}` substitution in the delivery prompt builder.

## Function Signatures

### CacheBlockBuilder (new method)

```csharp
// File: src/Pinder.LlmAdapters/Anthropic/CacheBlockBuilder.cs
// Namespace: Pinder.LlmAdapters.Anthropic

/// <summary>
/// Builds system blocks with only the player prompt cached.
/// Used by delivery calls where only the player speaks.
/// </summary>
/// <param name="playerPrompt">The player's assembled §3.1 system prompt. Must not be null.</param>
/// <returns>Array containing exactly one ContentBlock with cache_control: ephemeral.</returns>
/// <exception cref="ArgumentNullException">Thrown when playerPrompt is null.</exception>
public static ContentBlock[] BuildPlayerOnlySystemBlocks(string playerPrompt)
```

**Return type:** `ContentBlock[]` (length 1). The single element has `Type = "text"`, `Text = playerPrompt`, `CacheControl = new CacheControl { Type = "ephemeral" }`.

This mirrors the existing `BuildOpponentOnlySystemBlocks(string opponentPrompt)` method exactly, but for the player prompt.

### AnthropicLlmAdapter.DeliverMessageAsync (behavioral change)

```csharp
// File: src/Pinder.LlmAdapters/Anthropic/AnthropicLlmAdapter.cs
// Existing method — signature unchanged:
public async Task<string> DeliverMessageAsync(DeliveryContext context)
```

**Behavioral change:** The `systemBlocks` variable must be built using `CacheBlockBuilder.BuildPlayerOnlySystemBlocks(context.PlayerPrompt)` instead of `CacheBlockBuilder.BuildCachedSystemBlocks(context.PlayerPrompt, context.OpponentPrompt)`.

Before (current):
```csharp
var systemBlocks = CacheBlockBuilder.BuildCachedSystemBlocks(
    context.PlayerPrompt, context.OpponentPrompt);
```

After (required):
```csharp
var systemBlocks = CacheBlockBuilder.BuildPlayerOnlySystemBlocks(context.PlayerPrompt);
```

### PromptTemplates.FailureDeliveryInstruction (content change)

```csharp
// File: src/Pinder.LlmAdapters/PromptTemplates.cs
// Existing constant — type unchanged: public const string FailureDeliveryInstruction
```

**Content change:** Prepend an explicit player identity block at the beginning of the template, before any other content:

```
You are writing as {player_name}. This is THEIR message, in THEIR voice.
Do NOT write as the opponent. The failure corrupts what {player_name} says.

```

The `{player_name}` token is substituted by `SessionDocumentBuilder.BuildDeliveryPrompt` at runtime.

### PromptTemplates.SuccessDeliveryInstruction (content change)

```csharp
// File: src/Pinder.LlmAdapters/PromptTemplates.cs
// Existing constant — type unchanged: public const string SuccessDeliveryInstruction
```

**Content change:** For consistency, add `Write as {player_name}.` to the beginning or a natural location in the success instruction. This prevents voice contamination on the success path as well (defense in depth).

### SessionDocumentBuilder.BuildDeliveryPrompt (behavioral change)

```csharp
// File: src/Pinder.LlmAdapters/SessionDocumentBuilder.cs
// Existing method — signature unchanged:
public static string BuildDeliveryPrompt(
    IReadOnlyList<(string Sender, string Text)> conversationHistory,
    DialogueOption chosenOption,
    FailureTier outcome,
    int beatDcBy,
    string[]? activeTrapInstructions,
    string playerName,
    string opponentName)
```

**Behavioral change:** After performing all existing `{placeholder}` replacements on the failure template text, also replace `{player_name}` with the `playerName` parameter value. Similarly, apply `{player_name}` replacement to the success template text.

The `{player_name}` substitution must occur in **both** the success and failure code paths within this method.

### GameSession.ResolveTurnAsync (wiring change)

```csharp
// File: src/Pinder.Core/Conversation/GameSession.cs
// Existing method — signature unchanged:
public async Task<TurnResult> ResolveTurnAsync(int optionIndex)
```

**Behavioral change at ~line 503:** The `DeliveryContext` constructor call must pass the three name/turn fields that currently default to empty/zero:

```csharp
var deliveryContext = new DeliveryContext(
    // ... existing params ...
    playerName: _player.DisplayName,
    opponentName: _opponent.DisplayName,
    currentTurn: _turnNumber);
```

**Behavioral change at ~line 538:** The `OpponentContext` constructor call must similarly pass:

```csharp
var opponentContext = new OpponentContext(
    // ... existing params ...
    playerName: _player.DisplayName,
    opponentName: _opponent.DisplayName,
    currentTurn: _turnNumber);
```

**Note:** Both `DeliveryContext` and `OpponentContext` already have `playerName`, `opponentName`, and `currentTurn` as optional constructor parameters with empty-string/zero defaults. No structural change to the DTO classes is needed.

## Input/Output Examples

### Example 1: Legendary Fail Delivery (the bug scenario)

**Setup:**
- Player character: "Sable" (Scorpio, Love Bomber, omg energy)
- Opponent character: "Brick" (analytical, M&A background)
- Player chose option: "omg you actually work in M&A?? that's so hot in a scary way"
- Roll result: Nat 1 (Legendary Fail), missed DC by 20

**Current behavior (broken):**
The system prompt sent to the LLM contains BOTH Sable's and Brick's character prompts. The failure instruction does not state who to write as. The LLM generates:

> "You're right. I was watching. I do that a lot actually. Watching people work through things. It's a professional habit that probably translates poorly to dating apps..."

This is clearly Brick's voice (analytical, professional), not Sable's.

**Expected behavior (fixed):**
The system prompt contains ONLY Sable's character prompt. The failure instruction begins with "You are writing as Sable. This is THEIR message, in THEIR voice. Do NOT write as the opponent." The LLM generates something like:

> "omg wait sorry that was so weird lmao i just sent you my entire astrological compatibility analysis for us and then immediately followed it with a screenshot of my ex's linkedin?? im literally spiraling"

This is Sable's voice (chaotic, lowercase, over-sharing).

### Example 2: CacheBlockBuilder.BuildPlayerOnlySystemBlocks

**Input:** `playerPrompt = "You are Sable, a Scorpio sun..."`

**Output:**
```
ContentBlock[] {
  [0] = { Type: "text", Text: "You are Sable, a Scorpio sun...", CacheControl: { Type: "ephemeral" } }
}
```

Array length is exactly 1 (not 2).

### Example 3: FailureDeliveryInstruction after fix

The `{player_name}` token appears in the template. After `BuildDeliveryPrompt` substitutes it with `"Sable"`, the instruction begins:

```
You are writing as Sable. This is THEIR message, in THEIR voice.
Do NOT write as the opponent. The failure corrupts what Sable says.

The player chose option: "omg you actually work in M&A?? that's so hot in a scary way"
...
```

### Example 4: DeliveryContext with populated names

After the fix, the `DeliveryContext` passed to `DeliverMessageAsync` has:
- `PlayerName = "Sable"` (was: `""`)
- `OpponentName = "Brick"` (was: `""`)
- `CurrentTurn = 3` (was: `0`)

## Acceptance Criteria

### AC1: `FailureDeliveryInstruction` explicitly identifies the player character role

The `PromptTemplates.FailureDeliveryInstruction` constant must contain `{player_name}` tokens and text that explicitly instructs the LLM to write as the player character and NOT as the opponent. The identity framing must appear before any failure-tier instructions.

**Verification:** Read the constant string; confirm it contains `{player_name}` and language like "You are writing as {player_name}" and "Do NOT write as the opponent."

### AC2: Legendary fail delivery sounds like the player character, not the opponent

This is a systemic fix with three parts:
1. `DeliverMessageAsync` sends only the player's system prompt (not both) — verified by checking `BuildPlayerOnlySystemBlocks` is called instead of `BuildCachedSystemBlocks`.
2. The failure instruction template includes player identity framing — verified by AC1.
3. `GameSession` populates `PlayerName` on `DeliveryContext` — verified by AC3.

**Verification:** Unit test mocking `ILlmAdapter` confirms the adapter receives `DeliveryContext` with non-empty `PlayerName`. Integration test (if adapter is available) confirms the generated text is in character.

### AC3: Unit test with mock LLM verifies player name is in the delivery context

A test must:
1. Create a `GameSession` with a mock `ILlmAdapter` and named player/opponent characters.
2. Execute `StartTurnAsync()` followed by `ResolveTurnAsync(0)`.
3. Capture the `DeliveryContext` passed to the mock's `DeliverMessageAsync`.
4. Assert `DeliveryContext.PlayerName` equals the player's `DisplayName`.
5. Assert `DeliveryContext.OpponentName` equals the opponent's `DisplayName`.
6. Assert `DeliveryContext.CurrentTurn` is non-zero.

### AC4: Integration test — Legendary fail delivery passes a character voice check

A test must verify the full prompt assembly path: given a failure context with a specific player name, the assembled prompt (from `SessionDocumentBuilder.BuildDeliveryPrompt`) for a `FailureTier.Legendary` outcome contains the player name in the identity framing section. This does not require a live LLM call — it verifies the prompt text itself.

## Edge Cases

### Empty or null player name

`DeliveryContext.PlayerName` defaults to `""` for backward compatibility. When empty:
- `AnthropicLlmAdapter` calls `FallbackName(context.PlayerName, "Player")`, which returns `"Player"` for empty strings.
- `SessionDocumentBuilder.BuildDeliveryPrompt` receives `"Player"` and substitutes it into `{player_name}`.
- The template will read "You are writing as Player" — functional but generic.
- Existing tests that don't set `PlayerName` continue to work unchanged.

### Success path (no failure)

The `SuccessDeliveryInstruction` should also include `{player_name}` substitution for consistency. The `BuildDeliveryPrompt` method's success branch must also perform `{player_name}` replacement. Without this, the success path still uses both system prompts in the current code — the switch to `BuildPlayerOnlySystemBlocks` in the adapter fixes the system prompt issue for both paths.

### First turn (turn number = 0 before increment)

`GameSession._turnNumber` starts at 0 and is incremented at the end of `ResolveTurnAsync`. The `DeliveryContext` is constructed before the increment, so `CurrentTurn` will be 0 on the first turn. This is acceptable — the turn number is informational for the LLM, and `CurrentTurn = 0` means "first turn."

### Null playerPrompt

`CacheBlockBuilder.BuildPlayerOnlySystemBlocks` must throw `ArgumentNullException` when `playerPrompt` is null, matching the existing pattern in `BuildOpponentOnlySystemBlocks` and `BuildCachedSystemBlocks`.

### Backward compatibility of existing tests

All changes use optional constructor parameters with defaults. The ~1118+ existing tests do not pass `playerName`/`opponentName`/`currentTurn` to context DTOs and will continue to compile and pass with empty-string/zero defaults. No existing test behavior changes.

## Error Conditions

| Condition | Expected Behavior |
|---|---|
| `BuildPlayerOnlySystemBlocks(null)` | Throws `ArgumentNullException` with parameter name `"playerPrompt"` |
| `BuildDeliveryPrompt` with empty `playerName` | Throws `ArgumentNullException` (existing validation: `string.IsNullOrEmpty(playerName)`) |
| `_player.DisplayName` is null in GameSession | Depends on `CharacterProfile` validation — if null, `DeliveryContext` constructor stores `""` (its null-coalescing default). Adapter's `FallbackName` then returns `"Player"`. |
| `_player` or `_opponent` is null in GameSession | Would throw `NullReferenceException` before reaching context construction — this is an existing invariant, not new. |

## Dependencies

### Internal Dependencies
- **Issue #240** (options format fix) — must land first. The delivery path depends on valid dialogue options being generated. The issue explicitly states: "Depends on: #240."
- **`Pinder.Core.Conversation.DeliveryContext`** — already has `PlayerName`, `OpponentName`, `CurrentTurn` as optional constructor params. No change needed.
- **`Pinder.Core.Conversation.OpponentContext`** — same; already has the optional fields.
- **`Pinder.Core.Conversation.GameSession`** — the orchestrator that must wire names/turn into context DTOs.
- **`Pinder.LlmAdapters.Anthropic.Dto.ContentBlock`** — existing DTO used by `CacheBlockBuilder`.
- **`Pinder.LlmAdapters.Anthropic.Dto.CacheControl`** — existing DTO for `cache_control: ephemeral`.

### External Dependencies
- None. All changes are to static templates, static builders, and constructor call-sites. No new NuGet packages, no new external services.

## Files Changed Summary

| File | Change Type | Description |
|---|---|---|
| `src/Pinder.Core/Conversation/GameSession.cs` | Wiring | Pass `_player.DisplayName`, `_opponent.DisplayName`, `_turnNumber` to `DeliveryContext` and `OpponentContext` constructors |
| `src/Pinder.LlmAdapters/PromptTemplates.cs` | Content | Add player identity framing with `{player_name}` to `FailureDeliveryInstruction`; add `{player_name}` to `SuccessDeliveryInstruction` |
| `src/Pinder.LlmAdapters/SessionDocumentBuilder.cs` | Logic | Add `{player_name}` substitution in both success and failure paths of `BuildDeliveryPrompt` |
| `src/Pinder.LlmAdapters/Anthropic/CacheBlockBuilder.cs` | New method | Add `BuildPlayerOnlySystemBlocks(string playerPrompt)` |
| `src/Pinder.LlmAdapters/Anthropic/AnthropicLlmAdapter.cs` | Behavioral | Switch `DeliverMessageAsync` from `BuildCachedSystemBlocks` to `BuildPlayerOnlySystemBlocks` |
