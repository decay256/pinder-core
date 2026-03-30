# Contract: Vision Concern #211 — Context DTO Extensions

## Component
Changes to existing Pinder.Core context DTOs to carry `playerName`, `opponentName`, `currentTurn`.

## Maturity: Prototype

---

## Problem
`SessionDocumentBuilder.BuildDialogueOptionsPrompt()` requires `currentTurn`, `playerName`, `opponentName` to format `[T{n}|PLAYER|name]` markers. These fields are not on the existing context DTOs.

## Changes Required

### DialogueContext — add 3 fields
```csharp
// New properties
public int CurrentTurn { get; }
public string PlayerName { get; }
public string OpponentName { get; }
```

New constructor parameters (appended, with defaults for backward compat):
```csharp
public DialogueContext(
    // ... existing params ...
    int currentTurn = 0,
    string? playerName = null,
    string? opponentName = null)
```
- `playerName` defaults to `"PLAYER"` if null
- `opponentName` defaults to `"OPPONENT"` if null
- `currentTurn` defaults to `0` (turn 0 = "unknown")

### DeliveryContext — add 2 fields
```csharp
public string PlayerName { get; }
public string OpponentName { get; }
```
New constructor parameters (appended, with defaults).

### OpponentContext — add 2 fields
```csharp
public string PlayerName { get; }
public string OpponentName { get; }
```
New constructor parameters (appended, with defaults).

### GameSession wiring
In `GameSession.StartTurnAsync()` where `DialogueContext` is constructed (~line 262):
- Pass `currentTurn: _turnNumber`
- Pass `playerName: _player.DisplayName`
- Pass `opponentName: _opponent.DisplayName`

Same for `DeliveryContext` (~line 502) and `OpponentContext` (~line 537).

## Backward Compatibility
All new parameters have defaults — existing callers (including all 1118+ tests) continue to compile and pass without modification.

## Scope
~30 lines of DTO property additions, ~10 lines in GameSession. Trivial change.

## Who Implements This
This should be done as part of **#208** (AnthropicLlmAdapter) since that's the issue that needs these fields. Alternatively, it can be folded into **#205** as project setup.

**Recommendation:** Do it in #208 since it's the consumer, but the contract is published here so #207 (SessionDocumentBuilder) can design its API knowing these fields will be available.

## Dependencies
None (modifying existing Pinder.Core code)

## Consumers
- #207 (SessionDocumentBuilder reads these fields)
- #208 (AnthropicLlmAdapter passes context to SessionDocumentBuilder)
