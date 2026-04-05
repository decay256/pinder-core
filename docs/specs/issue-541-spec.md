# Issue #541 — AnthropicLlmAdapter: Add Stateful Conversation Mode

**Module**: docs/modules/llm-adapters.md

## Overview

Add a `ConversationSession` class to `Pinder.LlmAdapters` that accumulates user/assistant messages across multiple LLM calls within a single game session. Extend `AnthropicLlmAdapter` with a `StartConversation(string systemPrompt)` method that activates stateful mode, causing all subsequent `ILlmAdapter` method calls to append to and read from the accumulated message history rather than building fresh single-message requests each time.

This enables natural multi-turn conversation context: instead of each Anthropic API call receiving only a single user message (rebuilt from scratch), the adapter sends the full message history. The Anthropic Messages API is stateless (full history must be sent each call), so the adapter accumulates messages client-side.

## Function Signatures

### ConversationSession (new class in `Pinder.LlmAdapters`)

```csharp
namespace Pinder.LlmAdapters
{
    public sealed class ConversationSession
    {
        /// <summary>
        /// The system prompt blocks (with cache_control: ephemeral) persisted for the session.
        /// Set once at construction, immutable thereafter.
        /// </summary>
        public ContentBlock[] SystemBlocks { get; }

        /// <summary>
        /// All accumulated messages in conversation order (user/assistant alternating).
        /// Grows unbounded within a session. Read-only view of internal list.
        /// </summary>
        public IReadOnlyList<Message> Messages { get; }

        /// <summary>
        /// Creates a new conversation session with the given system prompt text.
        /// The system prompt is wrapped in a single ContentBlock with
        /// cache_control: { type: "ephemeral" } for Anthropic prompt caching.
        /// </summary>
        /// <param name="systemPrompt">
        /// Full system prompt text (character bibles, game vision, etc.).
        /// Must not be null or empty.
        /// </param>
        /// <exception cref="ArgumentException">If systemPrompt is null or whitespace.</exception>
        public ConversationSession(string systemPrompt);

        /// <summary>
        /// Append a user-role message to the conversation history.
        /// </summary>
        /// <param name="content">Message text. Must not be null.</param>
        /// <exception cref="ArgumentNullException">If content is null.</exception>
        public void AppendUser(string content);

        /// <summary>
        /// Append an assistant-role message to the conversation history.
        /// </summary>
        /// <param name="content">Message text. Must not be null.</param>
        /// <exception cref="ArgumentNullException">If content is null.</exception>
        public void AppendAssistant(string content);

        /// <summary>
        /// Build a MessagesRequest using accumulated state:
        /// system blocks + all messages + specified parameters.
        /// </summary>
        /// <param name="model">Anthropic model identifier (e.g. "claude-sonnet-4-20250514").</param>
        /// <param name="maxTokens">Maximum tokens in the response.</param>
        /// <param name="temperature">Sampling temperature (0.0–1.0).</param>
        /// <returns>
        /// A MessagesRequest with SystemBlocks as system, all Messages as messages array,
        /// and the given model/maxTokens/temperature.
        /// </returns>
        public MessagesRequest BuildRequest(string model, int maxTokens, double temperature);
    }
}
```

### AnthropicLlmAdapter (extended)

The adapter gains a `StartConversation` method. It does NOT implement a new interface in this issue (that is #542's responsibility with `IStatefulLlmAdapter`). This issue adds the internal capability.

```csharp
// Added to existing AnthropicLlmAdapter class:

/// <summary>
/// Start a stateful conversation session with the given system prompt.
/// After calling this, all ILlmAdapter method calls will append to and
/// read from the accumulated message history instead of building
/// fresh single-message requests.
///
/// Calling this when a session is already active replaces it (no error).
/// </summary>
/// <param name="systemPrompt">
/// Full system prompt (both character profiles, game vision, etc.).
/// Must not be null or empty.
/// </param>
/// <exception cref="ArgumentException">If systemPrompt is null or whitespace.</exception>
public void StartConversation(string systemPrompt);

/// <summary>
/// Whether a stateful conversation session is currently active.
/// When true, all ILlmAdapter calls route through the accumulated session.
/// When false, behavior is identical to current stateless mode.
/// </summary>
public bool HasActiveConversation { get; }
```

### Existing types referenced (no changes)

- `ContentBlock` (`Pinder.LlmAdapters.Anthropic.Dto`) — system block with optional `CacheControl`.
- `Message` (`Pinder.LlmAdapters.Anthropic.Dto`) — `{ Role: string, Content: string }`.
- `MessagesRequest` (`Pinder.LlmAdapters.Anthropic.Dto`) — `{ Model, MaxTokens, Temperature, System: ContentBlock[], Messages: Message[] }`.
- `ILlmAdapter` (`Pinder.Core.Interfaces`) — `GetDialogueOptionsAsync`, `DeliverMessageAsync`, `GetOpponentResponseAsync`, `GetInterestChangeBeatAsync`.

## Input/Output Examples

### Example 1: Creating a ConversationSession

```
Input:
  systemPrompt = "You are Velvet, a sardonic music critic..."

Result:
  session.SystemBlocks = [
    { Type: "text", Text: "You are Velvet, a sardonic music critic...", CacheControl: { Type: "ephemeral" } }
  ]
  session.Messages = [] (empty)
```

### Example 2: Accumulating messages across turns

```
session.AppendUser("[ENGINE — Turn 1: Option Generation]\nInterest: 10/25...")
// Messages.Count == 1, Messages[0] = { Role: "user", Content: "[ENGINE — Turn 1...]" }

session.AppendAssistant("OPTION_1\n[STAT: Charm]\n\"hey there gorgeous\"...")
// Messages.Count == 2, Messages[1] = { Role: "assistant", Content: "OPTION_1..." }

session.AppendUser("[ENGINE — Turn 1: Delivery]\nRoll: d20(14)...")
// Messages.Count == 3, Messages[2] = { Role: "user", Content: "[ENGINE — Turn 1: Delivery]..." }

session.AppendAssistant("hey there gorgeous 😏")
// Messages.Count == 4, Messages[3] = { Role: "assistant", Content: "hey there gorgeous 😏" }
```

### Example 3: BuildRequest with accumulated state

```
Input:
  model = "claude-sonnet-4-20250514"
  maxTokens = 1024
  temperature = 0.9
  (session has 4 accumulated messages from Example 2)

Output: MessagesRequest {
  Model: "claude-sonnet-4-20250514",
  MaxTokens: 1024,
  Temperature: 0.9,
  System: [ { Type: "text", Text: "You are Velvet...", CacheControl: { Type: "ephemeral" } } ],
  Messages: [
    { Role: "user",      Content: "[ENGINE — Turn 1: Option Generation]..." },
    { Role: "assistant", Content: "OPTION_1\n[STAT: Charm]..." },
    { Role: "user",      Content: "[ENGINE — Turn 1: Delivery]..." },
    { Role: "assistant", Content: "hey there gorgeous 😏" }
  ]
}
```

### Example 4: Stateful adapter — GetDialogueOptionsAsync in stateful mode

```
Precondition: adapter.StartConversation("system prompt text") has been called.

When: GetDialogueOptionsAsync(dialogueContext) is called

Then:
  1. Adapter builds user content from SessionDocumentBuilder (same as current)
  2. Appends user message to ConversationSession
  3. Calls _client.SendMessagesAsync() using session.BuildRequest(model, maxTokens, temperature)
     — request includes ALL prior messages + the new user message
  4. Parses response text into DialogueOption[]
  5. Appends raw assistant response text to ConversationSession
  6. Returns parsed DialogueOption[]
```

### Example 5: Stateless fallback (no session active)

```
Precondition: StartConversation() has NOT been called.

When: GetDialogueOptionsAsync(dialogueContext) is called

Then: Behavior is identical to current implementation:
  1. Build system blocks from CacheBlockBuilder
  2. Build user content from SessionDocumentBuilder
  3. Build single-message MessagesRequest
  4. Send, parse, return
  (No ConversationSession involvement)
```

## Acceptance Criteria

### AC1: ConversationSession class with AppendUser, AppendAssistant, Messages, SystemPrompt

`ConversationSession` must be a `public sealed class` in the `Pinder.LlmAdapters` namespace. It must expose:

- `SystemBlocks` (type: `ContentBlock[]`) — set at construction, read-only thereafter. Contains one `ContentBlock` with `Type = "text"`, the full system prompt as `Text`, and `CacheControl = { Type = "ephemeral" }`.
- `Messages` (type: `IReadOnlyList<Message>`) — the ordered list of all appended messages.
- `AppendUser(string content)` — adds a `Message` with `Role = "user"` and `Content = content`.
- `AppendAssistant(string content)` — adds a `Message` with `Role = "assistant"` and `Content = content`.
- `BuildRequest(string model, int maxTokens, double temperature)` — returns a `MessagesRequest` with `System = SystemBlocks`, `Messages` = snapshot of all accumulated messages as an array, and the provided model/maxTokens/temperature.

Messages must be stored in append order. The `Messages` property must reflect all messages appended so far.

### AC2: AnthropicLlmAdapter.StartConversation(systemPrompt) creates a session

When `StartConversation(string systemPrompt)` is called on `AnthropicLlmAdapter`:

- A new `ConversationSession` is constructed with the provided `systemPrompt`.
- The internal `_session` field (type `ConversationSession?`) is set to the new instance.
- `HasActiveConversation` returns `true` after the call.
- If a session was already active, it is replaced (no error, no exception).

### AC3: When a session is active, calls use accumulated messages[] not fresh context

When `HasActiveConversation` is `true`, the four `ILlmAdapter` methods must:

1. Build user content string the same way they currently do (via `SessionDocumentBuilder`).
2. Append the user content to the active `ConversationSession` via `AppendUser()`.
3. Build the `MessagesRequest` via `_session.BuildRequest()` (which includes system blocks + ALL accumulated messages).
4. Send the request via `_client.SendMessagesAsync()`.
5. Extract the response text.
6. Append the raw response text to the session via `AppendAssistant()`.
7. Parse the response text and return the result (same parsing logic as current).

The system blocks come from the `ConversationSession` (set at construction), NOT from `CacheBlockBuilder` per-call methods.

When `HasActiveConversation` is `false`, all four methods must behave identically to the current implementation (building fresh per-call `MessagesRequest` with `CacheBlockBuilder` system blocks and a single user message).

### AC4: ILlmAdapter interface unchanged

The `ILlmAdapter` interface in `Pinder.Core.Interfaces` must not be modified. No new methods, no signature changes. `StartConversation` and `HasActiveConversation` are concrete members on `AnthropicLlmAdapter` only (the interface extension `IStatefulLlmAdapter` is issue #542's responsibility).

### AC5: All existing tests still pass

All existing tests (currently 2979+) must continue to pass without modification. Since `NullLlmAdapter` does not gain `StartConversation`, and `AnthropicLlmAdapter` defaults to stateless mode (no session active), all existing test paths are unchanged.

### AC6: Build clean

The solution must compile without errors or warnings under `netstandard2.0` with `LangVersion 8.0` and nullable reference types enabled.

## Edge Cases

### Empty system prompt

`ConversationSession(string systemPrompt)` must throw `ArgumentException` if `systemPrompt` is `null`, empty, or whitespace. A system prompt is required for every conversation session.

### Null content in AppendUser/AppendAssistant

Both methods must throw `ArgumentNullException` if `content` is `null`. Empty string (`""`) is allowed — the Anthropic API accepts empty content strings.

### Calling StartConversation multiple times

Each call to `StartConversation` replaces the previous session entirely. The old `ConversationSession` is discarded (garbage collected). No error is thrown. The new session starts with zero messages and the new system prompt.

### Messages ordering: consecutive same-role messages

The Anthropic Messages API requires user/assistant messages to alternate (user first). `ConversationSession` does NOT enforce alternation — it stores messages in whatever order they are appended. The caller (adapter) is responsible for correct alternation. If the caller appends two user messages in a row, the Anthropic API will return a 400 error at send time.

### Unbounded message growth

At prototype maturity, `ConversationSession` accumulates messages without limit. A 20-turn game session may produce ~40-60 messages. With Anthropic's 200k token context window, this is acceptable. No truncation or summarization is implemented.

### BuildRequest returns a snapshot

`BuildRequest()` must return a `MessagesRequest` containing a copy (array) of the current messages, not a reference to the live list. Subsequent `AppendUser`/`AppendAssistant` calls must not modify previously returned `MessagesRequest` instances.

### ConversationSession is not thread-safe

`ConversationSession` is designed for sequential use within one `GameSession`. No locking or synchronization is required. Concurrent access from multiple threads is undefined behavior.

### Stateless path preserved with zero changes

When `HasActiveConversation` is `false`, each `ILlmAdapter` method must execute the exact same code path as the current implementation. No conditional logic should be encountered in the stateless path — the if/else branch for stateful mode must not alter the stateless path in any way.

## Error Conditions

| Condition | Expected Behavior |
|---|---|
| `new ConversationSession(null)` | `ArgumentException` thrown |
| `new ConversationSession("")` | `ArgumentException` thrown |
| `new ConversationSession("  ")` | `ArgumentException` thrown |
| `session.AppendUser(null)` | `ArgumentNullException` thrown |
| `session.AppendAssistant(null)` | `ArgumentNullException` thrown |
| `adapter.StartConversation(null)` | `ArgumentException` thrown |
| `adapter.StartConversation("")` | `ArgumentException` thrown |
| Two consecutive user messages sent to Anthropic API | `AnthropicApiException` (400) from `_client.SendMessagesAsync()` — not adapter's responsibility to prevent |
| `_client.SendMessagesAsync()` throws during stateful call | Exception propagates to caller (same as stateless mode). The appended user message remains in the session. The assistant message is NOT appended (since no response was received). |
| Response parsing fails in stateful mode | Raw response text is still appended to session via `AppendAssistant()` before parsing occurs. This ensures the session stays in sync even if parsing (e.g., `ParseDialogueOptions`) returns padded defaults. |

## Dependencies

### Internal (Pinder.LlmAdapters)

- `ContentBlock` — system block DTO with `CacheControl` annotation.
- `Message` — user/assistant message DTO with `Role` and `Content` string fields.
- `MessagesRequest` — API request DTO: `System: ContentBlock[]`, `Messages: Message[]`, `Model`, `MaxTokens`, `Temperature`.
- `MessagesResponse` — API response DTO with `GetText()` method.
- `AnthropicClient` — HTTP transport. `SendMessagesAsync(MessagesRequest)` sends the request and returns `MessagesResponse`.
- `CacheBlockBuilder` — used in stateless path only (builds per-call system blocks).
- `SessionDocumentBuilder` — used in both paths to build user content strings.
- `AnthropicOptions` — configuration (model, maxTokens, per-method temperatures).

### Internal (Pinder.Core)

- `ILlmAdapter` — interface that `AnthropicLlmAdapter` implements. NOT modified.
- `DialogueContext`, `DeliveryContext`, `OpponentContext`, `InterestChangeContext` — context DTOs passed to each `ILlmAdapter` method.
- `DialogueOption`, `OpponentResponse` — return types from adapter methods.

### External

- `Newtonsoft.Json` (13.0.3) — already a dependency of `Pinder.LlmAdapters`. Used for `MessagesRequest` serialization by `AnthropicClient`.
- Anthropic Messages API (v1) — stateless HTTP API at `https://api.anthropic.com/v1/messages`. Full message history must be sent with each call. Prompt caching via `cache_control: ephemeral` on system blocks is GA.

### Downstream (consumed by)

- Issue #542 (`IStatefulLlmAdapter` interface + `GameSession` wiring) depends on this issue. `GameSession` will call `StartConversation()` at construction if the adapter supports it.
- Issue #543 (`SessionSystemPromptBuilder`) provides the system prompt text that will be passed to `StartConversation()`.
- Issue #544 (`EngineInjectionBuilder`) provides the `[ENGINE]` block content that will replace `SessionDocumentBuilder` content in stateful mode in a future enhancement.

### Constraints

- `Pinder.Core` must NOT reference `Pinder.LlmAdapters` — the dependency is strictly one-way.
- `Pinder.Core` must remain zero NuGet dependencies.
- Target framework: `netstandard2.0`, `LangVersion 8.0` — no `record` types, no pattern matching with `{}`, no generic `Enum.Parse<T>`.
- `NullLlmAdapter` (in `Pinder.Core.Conversation`) is NOT modified and does NOT gain stateful capabilities.

## Files to Create/Modify

| File | Action | Notes |
|---|---|---|
| `src/Pinder.LlmAdapters/ConversationSession.cs` | Create | New class per spec |
| `src/Pinder.LlmAdapters/Anthropic/AnthropicLlmAdapter.cs` | Modify | Add `_session` field, `StartConversation()`, `HasActiveConversation`, dual code paths in all 4 ILlmAdapter methods |
| `tests/Pinder.LlmAdapters.Tests/ConversationSessionTests.cs` | Create | Unit tests for ConversationSession |
| `tests/Pinder.LlmAdapters.Tests/AnthropicLlmAdapterStatefulTests.cs` | Create | Tests for stateful adapter behavior |
