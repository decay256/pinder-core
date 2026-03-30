# Spec: Issue #208 — AnthropicLlmAdapter

## Overview

`AnthropicLlmAdapter` is the first real `ILlmAdapter` implementation in the Pinder engine. It translates the four `ILlmAdapter` method calls into Anthropic Claude Messages API requests, using `AnthropicClient` for HTTP transport, `SessionDocumentBuilder` for prompt formatting, and `CacheBlockBuilder` for prompt caching. It also owns response parsing — converting structured LLM text output into `DialogueOption[]` and `OpponentResponse` (with Tell/WeaknessWindow signals). The adapter lives in the separate `Pinder.LlmAdapters` project to keep `Pinder.Core` zero-dependency.

---

## Function Signatures

All types below are in namespace `Pinder.LlmAdapters.Anthropic` unless noted. Types from `Pinder.Core` are prefixed with their namespace.

### Class: `AnthropicLlmAdapter`

```csharp
public sealed class AnthropicLlmAdapter : ILlmAdapter, IDisposable
{
    // Constructors
    public AnthropicLlmAdapter(AnthropicOptions options);
    public AnthropicLlmAdapter(AnthropicOptions options, HttpClient httpClient);

    // ILlmAdapter
    public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context);
    public Task<string> DeliverMessageAsync(DeliveryContext context);
    public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context);
    public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context);

    // IDisposable
    public void Dispose();
}
```

**Constructor details:**
- The single-arg constructor creates an internal `AnthropicClient` from `options` (adapter owns its lifecycle and disposes it).
- The two-arg constructor accepts an external `HttpClient` for testing (passed through to `AnthropicClient`). The adapter does **not** dispose an externally-provided `HttpClient`.

### Internal parsing methods (private or internal for testing)

```csharp
// Visible as internal for unit testing via [InternalsVisibleTo]
internal static DialogueOption[] ParseDialogueOptions(string llmResponse);
internal static OpponentResponse ParseOpponentResponse(string llmResponse);
```

---

## Input/Output Examples

### GetDialogueOptionsAsync

**Input:** A `DialogueContext` with:
- `PlayerPrompt`: `"You are Thundercock, a bold confident penis..."`
- `OpponentPrompt`: `"You are Velvet, a mysterious and alluring match..."`
- `ConversationHistory`: `[("Velvet", "Hey there, nice profile pic 😏")]`
- `OpponentLastMessage`: `"Hey there, nice profile pic 😏"`
- `ActiveTraps`: `[]`
- `CurrentInterest`: `10`
- `PlayerName`: `"Thundercock"` (Sprint 9 DTO extension, default `""`)
- `OpponentName`: `"Velvet"` (Sprint 9 DTO extension, default `""`)
- `CurrentTurn`: `1` (Sprint 9 DTO extension, default `0`)

**Internal flow:**
1. `CacheBlockBuilder.BuildCachedSystemBlocks("You are Thundercock...", "You are Velvet...")` → system content blocks with `cache_control: ephemeral`
2. `SessionDocumentBuilder.BuildDialogueOptionsPrompt(history, traps, interest:10, turn:1, playerName:"Thundercock", opponentName:"Velvet")` → user content string
3. `MessagesRequest` built with model from `options.Model` (default `"claude-sonnet-4-20250514"`), temperature `0.9`, max_tokens from `options.MaxTokens` (default `1024`)
4. POST via `_client.SendMessagesAsync(request)`

**LLM response text (example):**
```
OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
"Thanks! You're not so bad yourself. What brings you to Pinder?"

OPTION_2
[STAT: RIZZ] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
"Oh, you haven't even seen my best angle yet 😏"

OPTION_3
[STAT: WIT] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
"Bold opening. I like that in a match. Usually I get 'hey' fourteen times."

OPTION_4
[STAT: HONESTY] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
"I appreciate the compliment. Honestly I spent way too long picking that photo."
```

**Output:** `DialogueOption[4]`:
- `[0]`: `DialogueOption(StatType.Charm, "Thanks! You're not so bad yourself. What brings you to Pinder?")`
- `[1]`: `DialogueOption(StatType.Rizz, "Oh, you haven't even seen my best angle yet 😏")`
- `[2]`: `DialogueOption(StatType.Wit, "Bold opening. I like that in a match. Usually I get 'hey' fourteen times.")`
- `[3]`: `DialogueOption(StatType.Honesty, "I appreciate the compliment. Honestly I spent way too long picking that photo.")`

### GetDialogueOptionsAsync — with callback and combo

**LLM response excerpt:**
```
OPTION_2
[STAT: WIT] [CALLBACK: pizza_story] [COMBO: The Setup] [TELL_BONUS: yes]
"Speaking of pizza, remember when you said pineapple was a crime?"
```

**Parsed output for option 2:**
- `Stat`: `StatType.Wit`
- `IntendedText`: `"Speaking of pizza, remember when you said pineapple was a crime?"`
- `CallbackTurnNumber`: derived from callback topic tracking (the int value extracted from the topic key lookup, or `null` if not resolvable from context)
- `ComboName`: `"The Setup"`
- `HasTellBonus`: `true`
- `HasWeaknessWindow`: `false` (weakness windows are set by GameSession from OpponentResponse, not from dialogue option generation)

### DeliverMessageAsync

**Input:** A `DeliveryContext` with the chosen option, `Outcome = FailureTier.None` (success), `BeatDcBy = 7`.

**Output:** A string — the post-delivery message text as returned by Claude:
```
"Thanks! You're not so bad yourself. What brings you to Pinder? I hear the penguin section is wild this time of year."
```

### GetOpponentResponseAsync

**Input:** An `OpponentContext` with `PlayerDeliveredMessage`, `InterestBefore = 10`, `InterestAfter = 12`.

**Internal flow:**
1. System blocks use **only** `context.OpponentPrompt` — NOT `PlayerPrompt`. Uses `CacheBlockBuilder.BuildOpponentOnlySystemBlocks(context.OpponentPrompt)`.
2. Temperature: `0.85`

**LLM response text (example with signals):**
```
[RESPONSE]
"Haha the penguin section! I actually went there once. It was... an experience. So what's YOUR type then? 👀"

[SIGNALS]
TELL: CHARM (opponent seems genuinely flustered by direct compliments)
WEAKNESS: WIT -2 (opponent is clearly overthinking their responses, opening for wit)
```

**Output:** `OpponentResponse`:
- `MessageText`: `"Haha the penguin section! I actually went there once. It was... an experience. So what's YOUR type then? 👀"`
- `DetectedTell`: `Tell(StatType.Charm, "opponent seems genuinely flustered by direct compliments")`
- `WeaknessWindow`: `WeaknessWindow(StatType.Wit, 2)`

### GetOpponentResponseAsync — no signals

**LLM response text:**
```
[RESPONSE]
"Haha yeah, it's pretty wild out there."
```

**Output:** `OpponentResponse`:
- `MessageText`: `"Haha yeah, it's pretty wild out there."`
- `DetectedTell`: `null`
- `WeaknessWindow`: `null`

### GetInterestChangeBeatAsync

**Input:** `InterestChangeContext("Velvet", 15, 17, InterestState.VeryIntoIt)`

**Output:** `"Velvet leans closer to her phone, a smile spreading across her face. This conversation just got interesting."` — or `null` if the response is empty or the state transition is not noteworthy.

---

## Acceptance Criteria

### AC1: All 4 ILlmAdapter methods implemented

`AnthropicLlmAdapter` must implement all four methods of `ILlmAdapter`:
- `GetDialogueOptionsAsync(DialogueContext) → Task<DialogueOption[]>`
- `DeliverMessageAsync(DeliveryContext) → Task<string>`
- `GetOpponentResponseAsync(OpponentContext) → Task<OpponentResponse>`
- `GetInterestChangeBeatAsync(InterestChangeContext) → Task<string?>`

Each method must build the appropriate system blocks and user content via the helper classes, create a `MessagesRequest`, call `AnthropicClient.SendMessagesAsync`, and parse/return the result.

### AC2: `cache_control: ephemeral` on both character prompts in system blocks

For `GetDialogueOptionsAsync` and `DeliverMessageAsync`, the system content must include **both** `PlayerPrompt` and `OpponentPrompt` as separate content blocks, each annotated with `cache_control: { type: "ephemeral" }`. This is achieved via `CacheBlockBuilder.BuildCachedSystemBlocks(playerPrompt, opponentPrompt)`.

For `GetOpponentResponseAsync`, **only** `OpponentPrompt` is included in the system blocks (via `CacheBlockBuilder.BuildOpponentOnlySystemBlocks(opponentPrompt)`), also with `cache_control: ephemeral`.

For `GetInterestChangeBeatAsync`, the system array is **empty** — no cached system blocks.

### AC3: Opponent response call uses ONLY OpponentPrompt in system

The `GetOpponentResponseAsync` method must call `CacheBlockBuilder.BuildOpponentOnlySystemBlocks(context.OpponentPrompt)` — it must NOT include `context.PlayerPrompt` anywhere in the system content. This is per §3.5 design: the opponent "plays themselves" without knowledge of the player's character sheet.

### AC4: `ParseDialogueOptions` falls back gracefully (never throws)

`ParseDialogueOptions(string llmResponse)` must **never** throw an exception regardless of input. On any parse failure — malformed text, missing options, garbage input, null/empty string — it must return exactly 4 `DialogueOption` instances. If fewer than 4 options were successfully parsed, pad remaining slots with defaults:

Default padding order: `StatType.Charm`, `StatType.Honesty`, `StatType.Wit`, `StatType.Chaos` — each with `IntendedText = "..."` and all optional fields as `null`/`false`.

For example, if parsing yields 2 valid options, the returned array is: `[parsed_0, parsed_1, default_Wit, default_Chaos]`. The padding fills from the end of the default stat list, skipping stats already present in parsed options.

### AC5: Uses `(StatType)Enum.Parse(typeof(StatType), value, true)` not generic overload

All stat parsing must use the non-generic `Enum.Parse` form:
```csharp
(StatType)Enum.Parse(typeof(StatType), value, true)
```
The generic overload `Enum.Parse<T>(string, bool)` is not available in .NET Standard 2.0.

### AC6: Integration test with real Anthropic call

An integration test must exist that:
- Creates an `AnthropicLlmAdapter` using `ANTHROPIC_API_KEY` from environment
- Calls at least `GetDialogueOptionsAsync` with a realistic `DialogueContext`
- Verifies the response is a `DialogueOption[]` of length 4
- Is **skippable** when `ANTHROPIC_API_KEY` is not set (use `[Fact(Skip = ...)]` or a runtime skip pattern like checking the env var and returning early)

### AC7: Unit tests with mocked HttpClient verify request shapes

Unit tests must:
- Provide a mock/fake `HttpClient` (or `HttpMessageHandler`) to `AnthropicLlmAdapter`
- Invoke each of the 4 methods
- Capture the HTTP request and assert:
  - Correct URL (`https://api.anthropic.com/v1/messages`)
  - Correct model string in request body
  - Correct temperature per method (0.9 for dialogue options, 0.7 for delivery, 0.85 for opponent response, 0.8 for interest change beat)
  - System blocks contain `cache_control` with `type: "ephemeral"`
  - Opponent response request system blocks contain only one prompt (opponent)

### AC8: Build clean

The solution must build without errors or warnings under .NET Standard 2.0 with `LangVersion 8.0`. All existing Pinder.Core tests (1118+) must continue to pass.

---

## Edge Cases

### ParseDialogueOptions

| Input | Expected Output |
|-------|----------------|
| `null` | 4 default `DialogueOption`s (Charm, Honesty, Wit, Chaos with `"..."` text) |
| `""` (empty string) | 4 default `DialogueOption`s |
| Only 1 valid option parsed | That option + 3 defaults |
| 5 or more options parsed | First 4 only (truncate) |
| `[STAT: INVALID_STAT]` | Skip that option, pad with default |
| Missing quoted text but valid stat | Skip that option (no text = invalid) |
| `[CALLBACK: pizza_story]` (non-numeric) | `CallbackTurnNumber = null` (cannot resolve to int without external context) |
| `[CALLBACK: 3]` (numeric) | `CallbackTurnNumber = 3` |
| `[COMBO: none]` | `ComboName = null` |
| `[COMBO: The One-Two Punch]` | `ComboName = "The One-Two Punch"` |
| `[TELL_BONUS: yes]` | `HasTellBonus = true` |
| `[TELL_BONUS: anything_else]` | `HasTellBonus = false` |
| Text has extra whitespace/newlines | Trimmed appropriately |
| Text has multiple quoted strings per option | First quoted string used |

### ParseOpponentResponse

| Input | Expected Output |
|-------|----------------|
| `null` | `OpponentResponse("", null, null)` — empty message, no signals |
| `""` (empty string) | `OpponentResponse("", null, null)` |
| No `[RESPONSE]` marker, just plain text | Entire text used as `MessageText`, no signals |
| `[RESPONSE]` present, no `[SIGNALS]` | Message text extracted, `DetectedTell = null`, `WeaknessWindow = null` |
| `[SIGNALS]` with only TELL | `DetectedTell` populated, `WeaknessWindow = null` |
| `[SIGNALS]` with only WEAKNESS | `DetectedTell = null`, `WeaknessWindow` populated |
| `[SIGNALS]` with both | Both populated |
| `TELL: INVALID_STAT (desc)` | `DetectedTell = null` (graceful degradation) |
| `WEAKNESS: WIT -0 (desc)` | `WeaknessWindow = null` (dcReduction must be > 0) |
| `WEAKNESS: WIT -3 (desc)` | `WeaknessWindow(StatType.Wit, 3)` |
| Malformed `[SIGNALS]` block (garbage) | Both signals `null`, no exception |

### Temperature overrides

| `AnthropicOptions` field | Default | Method |
|--------------------------|---------|--------|
| `DialogueOptionsTemperature` | `0.9` | `GetDialogueOptionsAsync` |
| `DeliveryTemperature` | `0.7` | `DeliverMessageAsync` |
| `OpponentResponseTemperature` | `0.85` | `GetOpponentResponseAsync` |
| `InterestChangeBeatTemperature` | `0.8` | `GetInterestChangeBeatAsync` |

If an `AnthropicOptions` per-method temperature is set, it overrides the default. If not set (null), use the default listed above.

### Concurrent usage

`AnthropicLlmAdapter` is **not** required to be thread-safe for this prototype. `GameSession` calls methods sequentially within a turn. However, `HttpClient` is thread-safe by design, so no corruption will occur if methods happen to be called concurrently — results are just not guaranteed to be ordered.

### Dispose behavior

- Calling `Dispose()` disposes the internal `AnthropicClient` (which disposes its owned `HttpClient`).
- If constructed with an external `HttpClient`, that client is **not** disposed — the caller owns its lifecycle.
- Calling methods after `Dispose()` should throw `ObjectDisposedException` (inherited from `HttpClient` disposal).

---

## Error Conditions

| Condition | Expected Behavior |
|-----------|-------------------|
| `AnthropicClient` throws `AnthropicApiException` (4xx/5xx after retries) | Exception propagates to caller (`GameSession`). The adapter does **not** catch API exceptions — the caller decides how to handle them. |
| `AnthropicClient` throws `HttpRequestException` (network failure) | Exception propagates to caller. |
| `AnthropicClient` throws `TaskCanceledException` (timeout) | Exception propagates to caller. |
| LLM returns empty response body | `GetDialogueOptionsAsync` returns 4 default options. `DeliverMessageAsync` returns `""`. `GetOpponentResponseAsync` returns `OpponentResponse("", null, null)`. `GetInterestChangeBeatAsync` returns `null`. |
| LLM returns completely unparseable response | Same as empty response — graceful fallback, never throw from parsing. |
| `options.ApiKey` is null or empty | `AnthropicClient` constructor should throw `ArgumentNullException`. |
| `context` parameter is null | Throw `ArgumentNullException` with parameter name. |
| `Enum.Parse` fails on invalid stat string | Caught internally in `ParseDialogueOptions`/`ParseOpponentResponse`, that option/signal is skipped (returns null/default). |

---

## Dependencies

### Required components (must be implemented first — issues #205, #206, #207)

| Component | Issue | What it provides |
|-----------|-------|-----------------|
| `Pinder.LlmAdapters` project scaffold | #205 | `.csproj`, namespace, `AnthropicOptions`, DTO types (`MessagesRequest`, `MessagesResponse`, `ContentBlock`) |
| `AnthropicClient` | #206 | `SendMessagesAsync(MessagesRequest) → Task<MessagesResponse>`, HTTP transport, retry logic (429/529/5xx) |
| `SessionDocumentBuilder` | #207 | `BuildDialogueOptionsPrompt(...)`, `BuildDeliveryPrompt(...)`, `BuildOpponentPrompt(...)`, `BuildInterestChangeBeatPrompt(...)` — static string formatting methods |
| `CacheBlockBuilder` | #207 | `BuildCachedSystemBlocks(playerPrompt, opponentPrompt)`, `BuildOpponentOnlySystemBlocks(opponentPrompt)` — returns `ContentBlock[]` with `cache_control` |
| `PromptTemplates` | #207 | Static §3.2–3.8 instruction strings (consumed by `SessionDocumentBuilder`) |

### Pinder.Core types consumed (no changes needed)

- `ILlmAdapter` — interface being implemented
- `DialogueContext`, `DeliveryContext`, `OpponentContext`, `InterestChangeContext` — method parameter types
- `DialogueOption`, `OpponentResponse`, `Tell`, `WeaknessWindow` — return types
- `StatType` — for `Enum.Parse` during response parsing
- `FailureTier` — for delivery outcome context
- `InterestState` — for interest change beat context
- `CallbackOpportunity` — referenced in `DialogueContext`

### Context DTO extensions (Vision #211 — implemented as part of #208 or as prerequisite)

`DialogueContext`, `DeliveryContext`, and `OpponentContext` gain optional fields for `PlayerName` (`string`, default `""`), `OpponentName` (`string`, default `""`), and `CurrentTurn` (`int`, default `0`). These are backward-compatible additions (new optional constructor parameters with defaults). These fields are needed by `SessionDocumentBuilder` for turn markers like `[T{n}|PLAYER|name]`.

### External services

- **Anthropic Messages API** (`https://api.anthropic.com/v1/messages`) — called via `AnthropicClient`. Requires a valid API key.

### NuGet dependencies

- `Newtonsoft.Json` — allowed in `Pinder.LlmAdapters` only. Used by `AnthropicClient` for request/response serialization.

---

## Appendix: AnthropicOptions Reference

```csharp
public sealed class AnthropicOptions
{
    public string ApiKey { get; set; }                          // Required
    public string Model { get; set; }                           // Default: "claude-sonnet-4-20250514"
    public int MaxTokens { get; set; }                          // Default: 1024
    public double Temperature { get; set; }                     // Global default: 0.9
    public double? DialogueOptionsTemperature { get; set; }     // Override for GetDialogueOptionsAsync
    public double? DeliveryTemperature { get; set; }            // Override for DeliverMessageAsync
    public double? OpponentResponseTemperature { get; set; }    // Override for GetOpponentResponseAsync
    public double? InterestChangeBeatTemperature { get; set; }  // Override for GetInterestChangeBeatAsync
}
```

## Appendix: Default Padding Stats Order

When `ParseDialogueOptions` must pad to reach 4 options, defaults are drawn from this ordered list, skipping any stat already present in the successfully parsed options:

1. `StatType.Charm`
2. `StatType.Honesty`
3. `StatType.Wit`
4. `StatType.Chaos`

Each default: `new DialogueOption(stat, "...", callbackTurnNumber: null, comboName: null, hasTellBonus: false, hasWeaknessWindow: false)`
