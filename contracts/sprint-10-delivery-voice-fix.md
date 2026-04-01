# Contract: Issue #241 — Legendary Fail Delivery Voice Fix

## Component
`Pinder.LlmAdapters` (PromptTemplates, CacheBlockBuilder, AnthropicLlmAdapter) + `Pinder.Core` (GameSession context wiring)

## Problem
Legendary fail delivery generates the opponent's voice instead of the player's. Three contributing factors:
1. `FailureDeliveryInstruction` doesn't explicitly name the player character
2. `DeliverMessageAsync` sends BOTH character system prompts — opponent voice contaminates
3. `GameSession` doesn't pass `playerName`/`opponentName`/`currentTurn` to `DeliveryContext` or `OpponentContext` — they default to empty strings

## Changes Required

### 1. `GameSession.ResolveTurnAsync()` (Pinder.Core/Conversation/GameSession.cs)

**Line ~503**: Pass player/opponent names and turn number to `DeliveryContext` constructor.

```csharp
// BEFORE (line 503)
var deliveryContext = new DeliveryContext(
    playerPrompt: _player.AssembledSystemPrompt,
    opponentPrompt: _opponent.AssembledSystemPrompt,
    conversationHistory: _history.AsReadOnly(),
    opponentLastMessage: GetLastOpponentMessage(),
    chosenOption: chosenOption,
    outcome: rollResult.Tier,
    beatDcBy: beatDcBy,
    activeTraps: deliveryTrapNames,
    activeTrapInstructions: deliveryTrapInstructions);

// AFTER
var deliveryContext = new DeliveryContext(
    playerPrompt: _player.AssembledSystemPrompt,
    opponentPrompt: _opponent.AssembledSystemPrompt,
    conversationHistory: _history.AsReadOnly(),
    opponentLastMessage: GetLastOpponentMessage(),
    chosenOption: chosenOption,
    outcome: rollResult.Tier,
    beatDcBy: beatDcBy,
    activeTraps: deliveryTrapNames,
    activeTrapInstructions: deliveryTrapInstructions,
    playerName: _player.DisplayName,
    opponentName: _opponent.DisplayName,
    currentTurn: _turnNumber);
```

**Line ~538**: Same for `OpponentContext` constructor:

```csharp
// AFTER — add at end of constructor call:
    playerName: _player.DisplayName,
    opponentName: _opponent.DisplayName,
    currentTurn: _turnNumber);
```

### 2. `CacheBlockBuilder` — Add `BuildPlayerOnlySystemBlocks` (Pinder.LlmAdapters/Anthropic/CacheBlockBuilder.cs)

New static method mirroring `BuildOpponentOnlySystemBlocks`:

```csharp
/// <summary>
/// Builds system blocks with only the player prompt cached.
/// Used by delivery calls where only the player speaks.
/// </summary>
public static ContentBlock[] BuildPlayerOnlySystemBlocks(string playerPrompt)
{
    if (playerPrompt == null) throw new ArgumentNullException(nameof(playerPrompt));
    return new[]
    {
        new ContentBlock
        {
            Type = "text",
            Text = playerPrompt,
            CacheControl = new CacheControl { Type = "ephemeral" }
        }
    };
}
```

### 3. `AnthropicLlmAdapter.DeliverMessageAsync` (Pinder.LlmAdapters/Anthropic/AnthropicLlmAdapter.cs)

Change from `BuildCachedSystemBlocks(player, opponent)` to `BuildPlayerOnlySystemBlocks(player)`:

```csharp
// BEFORE
var systemBlocks = CacheBlockBuilder.BuildCachedSystemBlocks(
    context.PlayerPrompt, context.OpponentPrompt);

// AFTER — delivery is always the player's voice
var systemBlocks = CacheBlockBuilder.BuildPlayerOnlySystemBlocks(context.PlayerPrompt);
```

### 4. `PromptTemplates.FailureDeliveryInstruction` (Pinder.LlmAdapters/PromptTemplates.cs)

Add explicit player identity framing. Insert at the beginning:

```
You are writing as {player_name}. This is THEIR message, in THEIR voice.
Do NOT write as the opponent. The failure corrupts what {player_name} says.

```

The `{player_name}` token is substituted by `SessionDocumentBuilder.BuildDeliveryPrompt`.

### 5. `SessionDocumentBuilder.BuildDeliveryPrompt` (Pinder.LlmAdapters/SessionDocumentBuilder.cs)

Currently does NOT substitute `{player_name}` in `FailureDeliveryInstruction`. Add substitution:

```csharp
// After existing .Replace("{active_trap_llm_instructions}", ...) block:
failureText = failureText.Replace("{player_name}", playerName);
```

Also add `{player_name}` substitution for `SuccessDeliveryInstruction` (for consistency — add "Write as {player_name}." to SuccessDeliveryInstruction as well).

## Interface Changes

### `CacheBlockBuilder` — new method
```
BuildPlayerOnlySystemBlocks(string playerPrompt) → ContentBlock[]
```
- Pre: playerPrompt non-null
- Post: returns array with 1 cached ContentBlock
- Mirrors existing `BuildOpponentOnlySystemBlocks`

### `DeliveryContext` — no structural change
Fields `PlayerName`, `OpponentName`, `CurrentTurn` already exist with defaults. GameSession just needs to populate them.

### `AnthropicLlmAdapter.DeliverMessageAsync` — behavioral change
- System blocks: player-only (was: both)
- User content: unchanged (already includes player name via BuildDeliveryPrompt)

## Tests Required
1. Unit test: `CacheBlockBuilder.BuildPlayerOnlySystemBlocks` returns 1 block with player text
2. Unit test: `BuildDeliveryPrompt` for failure path includes player name in output
3. Unit test: `FailureDeliveryInstruction` contains `{player_name}` token
4. GameSession test: verify `DeliveryContext` has non-empty `PlayerName`/`OpponentName` after `ResolveTurnAsync`

## Dependencies
- #240 must land first (options format fix — delivery depends on valid options)

## Files Changed
- `src/Pinder.Core/Conversation/GameSession.cs` (context wiring, ~2 lines per context)
- `src/Pinder.LlmAdapters/PromptTemplates.cs` (FailureDeliveryInstruction, SuccessDeliveryInstruction)
- `src/Pinder.LlmAdapters/SessionDocumentBuilder.cs` (player_name substitution in delivery)
- `src/Pinder.LlmAdapters/Anthropic/CacheBlockBuilder.cs` (new BuildPlayerOnlySystemBlocks)
- `src/Pinder.LlmAdapters/Anthropic/AnthropicLlmAdapter.cs` (DeliverMessageAsync system blocks)
- Tests in both test projects
