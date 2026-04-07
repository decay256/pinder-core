# Contract: Issue #647 — Active Tell in LLM Options Generation

## Component
`Pinder.Core/Conversation/DialogueContext` and `Pinder.LlmAdapters/SessionDocumentBuilder`

## Description
The active tell (`_activeTell`) is correctly detected and scored by `GameSession`, but the LLM generating the dialogue options is unaware of it. We must inject it into the prompt so the LLM writes an option that capitalizes on the vulnerability.

## Interface Changes

### 1. `DialogueContext` (Pinder.Core)
**File**: `src/Pinder.Core/Conversation/DialogueContext.cs`
**Change**: Add an optional parameter `Tell? activeTell = null` to the constructor, and expose it as a property `public Tell? ActiveTell { get; }`.
**Backward Compatibility**: Because it's an optional parameter at the end of the constructor (or initialized to null if not provided), existing callers (like tests) will not break.

### 2. `GameSession` (Pinder.Core)
**File**: `src/Pinder.Core/Conversation/GameSession.cs`
**Change**: In `StartTurnAsync`, when constructing the `DialogueContext`, pass `activeTell: _activeTell`.

### 3. `SessionDocumentBuilder` (Pinder.LlmAdapters)
**File**: `src/Pinder.LlmAdapters/SessionDocumentBuilder.cs`
**Change**: In `BuildDialogueOptionsPrompt`, check if `context.ActiveTell` is not null. If it exists, append the following text to the `[ENGINE]` game state block:
```
📡 TELL DETECTED: The opponent revealed a vulnerability around {stat}. 
One option using {stat} should explicitly capitalize on this moment — 
it landed differently than intended. The player read the room.
```
Where `{stat}` is `context.ActiveTell.Stat.ToString()`.

## Dependencies
- Must strictly use the `Tell` object defined in `Pinder.Core.Conversation`.
