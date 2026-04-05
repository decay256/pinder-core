# Spec: GameSession creates LLM conversation session at start

**Issue**: #542  
**Module**: docs/modules/conversation-game-session.md, docs/modules/llm-adapters.md

---

## Overview

GameSession should automatically detect when its injected `ILlmAdapter` supports stateful conversation mode (via the new `IStatefulLlmAdapter` sub-interface) and, if so, start a persistent conversation session at construction time. This enables all LLM calls within a game session to share continuous context—messages accumulate across turns rather than being rebuilt from scratch each call. When the adapter does not support stateful mode (e.g., `NullLlmAdapter`), the existing stateless behavior is preserved with zero changes.

This feature bridges three components: a new `IStatefulLlmAdapter` interface in `Pinder.Core.Interfaces`, an implementation of that interface in `AnthropicLlmAdapter` (which delegates to `ConversationSession` from #541), and wiring logic in `GameSession`'s constructor.

---

## Function Signatures

### New Interface: `IStatefulLlmAdapter` (Pinder.Core.Interfaces)

```csharp
namespace Pinder.Core.Interfaces
{
    /// <summary>
    /// Extends ILlmAdapter with stateful conversation support.
    /// When implemented, GameSession creates a persistent conversation
    /// at construction and routes all LLM calls through accumulated
    /// message history.
    /// </summary>
    public interface IStatefulLlmAdapter : ILlmAdapter
    {
        /// <summary>
        /// Start a new conversation session with the given system prompt.
        /// The adapter internally tracks the active session.
        /// Subsequent ILlmAdapter method calls use the accumulated
        /// message history from this session.
        /// Call once per GameSession lifetime.
        /// </summary>
        /// <param name="systemPrompt">
        /// Full system prompt string (both character profiles + game context).
        /// Must not be null or empty.
        /// </param>
        void StartConversation(string systemPrompt);

        /// <summary>
        /// Whether a conversation session is currently active.
        /// Returns true after StartConversation has been called.
        /// </summary>
        bool HasActiveConversation { get; }
    }
}
```

**Key constraints:**
- `IStatefulLlmAdapter` inherits all four methods from `ILlmAdapter` (`GetDialogueOptionsAsync`, `DeliverMessageAsync`, `GetOpponentResponseAsync`, `GetInterestChangeBeatAsync`).
- `StartConversation(string)` returns `void` — the adapter internally owns the `ConversationSession` (#541). GameSession does not hold or pass a session reference.
- Calling `StartConversation` when a session is already active replaces the existing session (no error thrown).
- `HasActiveConversation` returns `false` before `StartConversation` is called, `true` after.

### Modified: `AnthropicLlmAdapter` (Pinder.LlmAdapters.Anthropic)

The class declaration changes from:

```csharp
public sealed class AnthropicLlmAdapter : ILlmAdapter, IDisposable
```

to:

```csharp
public sealed class AnthropicLlmAdapter : IStatefulLlmAdapter, IDisposable
```

New members added:

```csharp
// Internal field
private ConversationSession? _session;

// IStatefulLlmAdapter implementation
public bool HasActiveConversation => _session != null;

public void StartConversation(string systemPrompt)
// Creates a new ConversationSession(systemPrompt), storing it in _session.
// Replaces any existing session.
```

**Behavioral change when `_session` is active:**
- Each `ILlmAdapter` method call appends a user message to `_session` (via `AppendUser`), sends the full accumulated `MessagesRequest` via `AnthropicClient`, appends the assistant response (via `AppendAssistant`), and returns the parsed result.
- When `_session` is null, existing stateless behavior is unchanged (each call builds a fresh `MessagesRequest` from scratch using `SessionDocumentBuilder` and `CacheBlockBuilder`).

### Modified: `GameSession` constructor (Pinder.Core.Conversation)

No new public methods or properties. The change is inside the existing 6-parameter constructor body:

```csharp
public GameSession(
    CharacterProfile player,
    CharacterProfile opponent,
    ILlmAdapter llm,
    IDiceRoller dice,
    ITrapRegistry trapRegistry,
    GameSessionConfig? config)
```

**New logic added at the end of the constructor** (after all existing initialization):

```
if _llm is IStatefulLlmAdapter stateful:
    build system prompt string from player and opponent profiles
    call stateful.StartConversation(systemPrompt)
```

The system prompt assembly for this issue is a simple concatenation:

```
{player.AssembledSystemPrompt}

---

{opponent.AssembledSystemPrompt}
```

> **Note:** Issue #543 (`SessionSystemPromptBuilder`) replaces this simple concatenation with a structured prompt including game vision, world rules, and meta contract. The #542 implementer should use the simple concatenation above and expect #543 to replace it.

### Unchanged: `NullLlmAdapter` (Pinder.Core.Conversation)

`NullLlmAdapter` continues to implement only `ILlmAdapter`. It does **not** implement `IStatefulLlmAdapter`. This is the mechanism that guarantees all existing tests remain on the stateless path.

### Unchanged: `GameSessionConfig` (Pinder.Core.Conversation)

No new properties. The adapter type detection happens via interface check (`is IStatefulLlmAdapter`), not via config.

---

## Input/Output Examples

### Example 1: Stateful adapter (AnthropicLlmAdapter)

```
// Setup
var options = new AnthropicOptions("sk-ant-...", "claude-sonnet-4-20250514");
var adapter = new AnthropicLlmAdapter(options);  // implements IStatefulLlmAdapter
var session = new GameSession(velvet, sable, adapter, dice, trapReg, config);

// At construction time:
//   GameSession detects adapter is IStatefulLlmAdapter
//   Builds system prompt: velvet.AssembledSystemPrompt + "\n\n---\n\n" + sable.AssembledSystemPrompt
//   Calls adapter.StartConversation(systemPrompt)
//   adapter.HasActiveConversation → true

// Per turn:
//   session.StartTurnAsync() → adapter.GetDialogueOptionsAsync(context)
//     → adapter appends user message to internal ConversationSession
//     → sends accumulated messages[] via AnthropicClient
//     → appends assistant response to ConversationSession
//     → returns parsed DialogueOption[]
//
//   session.ResolveTurnAsync(1) → adapter.DeliverMessageAsync(context)
//     → adapter appends user message (delivery context)
//     → sends accumulated messages[] (now includes option generation exchange)
//     → appends assistant response
//     → returns delivered text
//   Then → adapter.GetOpponentResponseAsync(context)
//     → adapter appends user message (opponent context)
//     → sends accumulated messages[] (now includes delivery exchange)
//     → appends assistant response
//     → returns OpponentResponse
```

### Example 2: Stateless adapter (NullLlmAdapter)

```
// Setup
var adapter = new NullLlmAdapter();  // implements ILlmAdapter only
var session = new GameSession(velvet, sable, adapter, dice, trapReg, config);

// At construction time:
//   GameSession checks: adapter is IStatefulLlmAdapter? → false
//   No conversation started
//   Behavior is 100% identical to current code

// Per turn:
//   All ILlmAdapter calls use existing stateless behavior
//   NullLlmAdapter returns hardcoded responses as before
```

### Example 3: System prompt content

Given:
- `player.AssembledSystemPrompt` = `"You are Velvet — lowercase-with-intent, precise, ironic..."`
- `opponent.AssembledSystemPrompt` = `"You are Sable — omg, 😭, fast-talk energy..."`

The system prompt passed to `StartConversation` is:

```
You are Velvet — lowercase-with-intent, precise, ironic...

---

You are Sable — omg, 😭, fast-talk energy...
```

---

## Acceptance Criteria

### AC1: `IStatefulLlmAdapter : ILlmAdapter` interface with `StartConversation(string)`

A new file `src/Pinder.Core/Interfaces/IStatefulLlmAdapter.cs` defines `IStatefulLlmAdapter` extending `ILlmAdapter` with:
- `void StartConversation(string systemPrompt)` — initializes internal conversation session
- `bool HasActiveConversation { get; }` — returns whether a session is active

The interface lives in namespace `Pinder.Core.Interfaces`, in the `Pinder.Core` project (zero external dependencies — this is an interface only).

### AC2: `AnthropicLlmAdapter` implements `IStatefulLlmAdapter`

`AnthropicLlmAdapter` changes its class declaration to implement `IStatefulLlmAdapter` (which extends `ILlmAdapter`, so all existing method implementations satisfy both interfaces).

- `StartConversation(string)` creates a `ConversationSession` (#541 dependency) and stores it internally.
- When a session is active, all four `ILlmAdapter` method calls route through the accumulated `ConversationSession` messages.
- When no session is active, existing stateless behavior is preserved.

### AC3: `GameSession` uses stateful session if adapter supports it

In `GameSession`'s 6-parameter constructor, after all existing initialization:
1. Check if `_llm is IStatefulLlmAdapter stateful`
2. If true: build system prompt from `_player.AssembledSystemPrompt` and `_opponent.AssembledSystemPrompt`, call `stateful.StartConversation(systemPrompt)`
3. If false: do nothing (stateless path)

No changes to any `GameSession` method bodies (`StartTurnAsync`, `ResolveTurnAsync`, `ReadAsync`, `RecoverAsync`, `Wait`). The adapter itself handles routing through the session internally—GameSession continues calling `_llm.GetDialogueOptionsAsync(context)` etc. as before.

### AC4: Session system prompt includes both character profiles

The system prompt passed to `StartConversation` must contain:
- The player character's full assembled system prompt (`_player.AssembledSystemPrompt`)
- The opponent character's full assembled system prompt (`_opponent.AssembledSystemPrompt`)
- Separated by `"\n\n---\n\n"`

This is a temporary format. Issue #543 introduces `SessionSystemPromptBuilder` which produces a structured prompt with game vision, world rules, and meta contract.

### AC5: All existing tests still pass (NullLlmAdapter is not IStatefulLlmAdapter → stateless path)

Because `NullLlmAdapter` implements only `ILlmAdapter` (not `IStatefulLlmAdapter`), the `is IStatefulLlmAdapter` check in GameSession's constructor evaluates to `false` for all existing tests. No conversation session is started, and all existing behavior is preserved exactly as before. All 2979+ existing tests must compile and pass without modification.

### AC6: Build clean

The solution (`Pinder.Core`, `Pinder.LlmAdapters`, `Pinder.Rules`, all test projects, `session-runner`) must build with zero errors and zero warnings related to these changes.

---

## Edge Cases

### Null or empty system prompt
- If `_player.AssembledSystemPrompt` or `_opponent.AssembledSystemPrompt` is empty string (never null per `CharacterProfile` constructor validation), `StartConversation` receives a prompt with one empty section. The `ConversationSession` should accept any non-null string. GameSession does not guard against empty prompts—`CharacterProfile` guarantees non-null via its constructor.

### Adapter is IStatefulLlmAdapter but StartConversation fails
- `StartConversation` creates a `ConversationSession` object (no I/O, no network call). It should not throw under normal circumstances. If `systemPrompt` is null, `ConversationSession` constructor should throw `ArgumentNullException`. Since GameSession builds the prompt from non-null `AssembledSystemPrompt` values, this should never happen in practice.

### Multiple GameSessions sharing one adapter instance
- The architecture assumes one adapter instance per GameSession (1:1 relationship). If two `GameSession` instances share an `AnthropicLlmAdapter`, the second constructor call to `StartConversation` replaces the first session. This is documented as unsupported at prototype maturity. Do not throw; silently replace.

### Adapter implements IStatefulLlmAdapter but StartConversation was already called
- `StartConversation` replaces the existing session. No error. Previous message history is discarded.

### Five-parameter GameSession constructor
- The 5-parameter constructor delegates to the 6-parameter constructor with `config: null`. The `is IStatefulLlmAdapter` check still runs (it checks `_llm`, not config). If the adapter is stateful, the session is started even without config.

### GameSession with IStatefulLlmAdapter but null config
- The stateful detection is independent of `GameSessionConfig`. Config is not involved in the decision. A null config does not prevent stateful mode.

---

## Error Conditions

| Condition | Expected Behavior |
|-----------|-------------------|
| `systemPrompt` argument to `StartConversation` is `null` | `ArgumentNullException` thrown by `ConversationSession` constructor |
| `ILlmAdapter` methods called before `StartConversation` on `AnthropicLlmAdapter` | Existing stateless behavior (no session active) |
| Network failure during a stateful LLM call | `AnthropicApiException` propagates up through `GameSession` (same as stateless mode) — the session's message history is NOT corrupted (failed response is not appended) |
| `AnthropicLlmAdapter` disposed while session is active | Adapter disposal disposes `AnthropicClient`; subsequent calls throw `ObjectDisposedException` (existing behavior) |
| Second `StartConversation` call on same adapter | Previous session is silently replaced — no error |

---

## Dependencies

| Dependency | Type | Notes |
|------------|------|-------|
| #541 `ConversationSession` | Hard prerequisite | Must be merged first. `AnthropicLlmAdapter.StartConversation` creates a `ConversationSession` instance. |
| `Pinder.Core` (netstandard2.0) | Project reference | `IStatefulLlmAdapter` interface lives here. Zero NuGet dependencies. |
| `Pinder.LlmAdapters` (netstandard2.0) | Project reference | `AnthropicLlmAdapter` implementation lives here. References `Pinder.Core` + `Newtonsoft.Json`. |
| `Pinder.Core.Characters.CharacterProfile` | Existing class | `AssembledSystemPrompt` property used to build system prompt. |
| `Pinder.Core.Interfaces.ILlmAdapter` | Existing interface | Base interface extended by `IStatefulLlmAdapter`. |
| `Pinder.LlmAdapters.Anthropic.Dto.Message` | Existing DTO | Used internally by `ConversationSession` for message accumulation. |
| `Pinder.LlmAdapters.Anthropic.Dto.ContentBlock` | Existing DTO | Used by `ConversationSession` for system blocks. |
| `Pinder.LlmAdapters.Anthropic.Dto.MessagesRequest` | Existing DTO | Built by `ConversationSession.BuildRequest()`. |

### Files Changed

| File | Change Type | Description |
|------|-------------|-------------|
| `src/Pinder.Core/Interfaces/IStatefulLlmAdapter.cs` | **New** | Interface definition |
| `src/Pinder.Core/Conversation/GameSession.cs` | **Modified** | Constructor body: `is IStatefulLlmAdapter` check + `StartConversation` call |
| `src/Pinder.LlmAdapters/Anthropic/AnthropicLlmAdapter.cs` | **Modified** | Implements `IStatefulLlmAdapter`; gains `_session` field and dual stateful/stateless code paths in all four `ILlmAdapter` methods |
| `src/Pinder.Core/Conversation/NullLlmAdapter.cs` | **Unchanged** | Remains `ILlmAdapter` only |
| `src/Pinder.Core/Conversation/GameSessionConfig.cs` | **Unchanged** | No new properties |

### Constraints

- **netstandard2.0 + LangVersion 8.0**: No `record` types. Use `sealed class`. No generic `Enum.Parse<T>`.
- **Zero NuGet dependencies in Pinder.Core**: `IStatefulLlmAdapter` is a pure interface.
- **Backward compatibility**: All 2979+ existing tests must pass unchanged.
- **One-way dependency**: `Pinder.LlmAdapters → Pinder.Core`. Core must not reference LlmAdapters.
- **ConversationSession type is internal to LlmAdapters**: GameSession never touches it directly. The adapter owns the session lifecycle.
