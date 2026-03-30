# Spec: Issue #206 — AnthropicClient HTTP Client with Retry Logic and cache_control Support

## Overview

`AnthropicClient` is a raw HTTP transport class that sends requests to the Anthropic Messages API (`POST https://api.anthropic.com/v1/messages`) and returns deserialized responses. It handles retry logic for transient failures (429 rate-limit, 529 overloaded, 5xx server errors) and serializes/deserializes request/response DTOs using Newtonsoft.Json. It lives in the `Pinder.LlmAdapters` project — separate from `Pinder.Core` — so that Core remains zero-dependency.

## Function Signatures

All types live in namespace `Pinder.LlmAdapters.Anthropic`.

### AnthropicClient

```csharp
public sealed class AnthropicClient : IDisposable
{
    /// <summary>
    /// Creates a client with an internally-owned HttpClient.
    /// Sets required headers: x-api-key, anthropic-version.
    /// </summary>
    /// <param name="apiKey">Anthropic API key. Must not be null or empty.</param>
    /// <exception cref="ArgumentException">If apiKey is null or empty.</exception>
    public AnthropicClient(string apiKey);

    /// <summary>
    /// Creates a client with a caller-provided HttpClient (for testing with mock handlers).
    /// The client does NOT dispose the provided HttpClient.
    /// Sets required headers on the provided client: x-api-key, anthropic-version.
    /// </summary>
    /// <param name="apiKey">Anthropic API key. Must not be null or empty.</param>
    /// <param name="httpClient">Caller-owned HttpClient instance.</param>
    /// <exception cref="ArgumentException">If apiKey is null or empty.</exception>
    /// <exception cref="ArgumentNullException">If httpClient is null.</exception>
    public AnthropicClient(string apiKey, HttpClient httpClient);

    /// <summary>
    /// Sends a Messages API request with automatic retry for transient failures.
    /// </summary>
    /// <param name="request">The MessagesRequest to send. Must not be null.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Deserialized MessagesResponse from the API.</returns>
    /// <exception cref="ArgumentNullException">If request is null.</exception>
    /// <exception cref="AnthropicApiException">
    /// On non-retryable 4xx errors (immediate), or after all retry attempts are exhausted.
    /// </exception>
    /// <exception cref="OperationCanceledException">If ct is cancelled.</exception>
    public Task<MessagesResponse> SendMessagesAsync(
        MessagesRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Disposes the internally-owned HttpClient if this instance created it.
    /// Does nothing if the HttpClient was provided externally.
    /// </summary>
    public void Dispose();
}
```

### AnthropicApiException

```csharp
public sealed class AnthropicApiException : Exception
{
    /// <summary>HTTP status code returned by the API.</summary>
    public int StatusCode { get; }

    /// <summary>Raw response body from the API (may be null if no body was returned).</summary>
    public string? ResponseBody { get; }

    public AnthropicApiException(int statusCode, string? responseBody);
    public AnthropicApiException(int statusCode, string? responseBody, string message);
}
```

### DTOs (defined in #205 scaffold, consumed here)

These are expected to exist from issue #205. The spec documents expected shape for context:

```csharp
// Pinder.LlmAdapters.Anthropic.Dto namespace
public sealed class MessagesRequest
{
    public string Model { get; set; }
    public int MaxTokens { get; set; }
    public List<SystemBlock> System { get; set; }  // system prompt blocks with optional cache_control
    public List<Message> Messages { get; set; }
}

public sealed class MessagesResponse
{
    public string Id { get; set; }
    public string Type { get; set; }       // "message"
    public string Role { get; set; }       // "assistant"
    public List<ContentBlock> Content { get; set; }
    public string StopReason { get; set; }
    public Usage Usage { get; set; }
}

public sealed class ContentBlock
{
    public string Type { get; set; }       // "text"
    public string Text { get; set; }
}

public sealed class Usage
{
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int? CacheCreationInputTokens { get; set; }
    public int? CacheReadInputTokens { get; set; }
}
```

## Input/Output Examples

### Example 1: Successful Request

**Input:**
```csharp
var client = new AnthropicClient("sk-ant-api03-xxxx");
var request = new MessagesRequest
{
    Model = "claude-sonnet-4-20250514",
    MaxTokens = 1024,
    System = new List<SystemBlock>
    {
        new SystemBlock { Type = "text", Text = "You are a dating game NPC." }
    },
    Messages = new List<Message>
    {
        new Message { Role = "user", Content = "Generate 4 dialogue options." }
    }
};
var response = await client.SendMessagesAsync(request);
```

**HTTP request sent:**
```
POST https://api.anthropic.com/v1/messages
x-api-key: sk-ant-api03-xxxx
anthropic-version: 2023-06-01
Content-Type: application/json

{"model":"claude-sonnet-4-20250514","max_tokens":1024,"system":[{"type":"text","text":"You are a dating game NPC."}],"messages":[{"role":"user","content":"Generate 4 dialogue options."}]}
```

**Output (on HTTP 200):**
A `MessagesResponse` with `Id`, `Content[0].Text` containing the generated text, `StopReason = "end_turn"`, and `Usage` with token counts.

### Example 2: 429 Rate-Limited, Then Succeeds

1. First attempt → HTTP 429, `Retry-After: 3` header
2. Client waits 3 seconds
3. Second attempt → HTTP 200
4. Returns the `MessagesResponse` from the successful attempt

### Example 3: 400 Bad Request (Immediate Failure)

1. First attempt → HTTP 400, body: `{"type":"error","error":{"type":"invalid_request_error","message":"max_tokens: must be positive"}}`
2. Throws `AnthropicApiException` with `StatusCode = 400` and `ResponseBody` containing the raw JSON body

### Example 4: Request with cache_control

**Input (system blocks with cache_control):**
```csharp
var request = new MessagesRequest
{
    Model = "claude-sonnet-4-20250514",
    MaxTokens = 1024,
    System = new List<SystemBlock>
    {
        new SystemBlock
        {
            Type = "text",
            Text = "Long character prompt ~6000 tokens...",
            CacheControl = new CacheControl { Type = "ephemeral" }
        }
    },
    Messages = new List<Message> { /* ... */ }
};
```

The `cache_control` field is serialized into the request body. **No `anthropic-beta` header is sent.** Prompt caching is GA — the `cache_control` field in the request body is sufficient. The response `Usage` will contain `CacheCreationInputTokens` (on first call) or `CacheReadInputTokens` (on subsequent calls within the TTL).

## Acceptance Criteria

### AC1: AnthropicClient exists with both constructors

The class `AnthropicClient` must be defined in `src/Pinder.LlmAdapters/Anthropic/AnthropicClient.cs` within namespace `Pinder.LlmAdapters.Anthropic`. It must implement `IDisposable`.

- **Constructor 1** `AnthropicClient(string apiKey)`: Creates and owns an internal `HttpClient`. Configures `x-api-key` and `anthropic-version: 2023-06-01` as default request headers. Calling `Dispose()` disposes the internal `HttpClient`.
- **Constructor 2** `AnthropicClient(string apiKey, HttpClient httpClient)`: Accepts a caller-provided `HttpClient`. Configures `x-api-key` and `anthropic-version` headers on it. Calling `Dispose()` does NOT dispose the provided `HttpClient`.

Both constructors must reject null/empty `apiKey` with `ArgumentException`. Constructor 2 must reject null `httpClient` with `ArgumentNullException`.

### AC2: No anthropic-beta header

The client must **NOT** send the `anthropic-beta: prompt-caching-2024-07-31` header. Prompt caching is GA per Anthropic docs (verified 2025-03-30) — the `cache_control` field in the request body is sufficient. Per vision concern #213, the beta header is explicitly excluded.

A code comment should be present: `// Prompt caching is GA — no beta header required. cache_control in body is sufficient.`

### AC3: Retry logic for 429 (rate-limited)

When the API returns HTTP 429:
1. Read the `Retry-After` response header (value is in seconds as an integer string, e.g., `"3"`).
2. If the header is present and parseable, wait that many seconds before retrying.
3. If the header is missing or unparseable, wait 5 seconds.
4. Maximum 3 retry attempts for 429.
5. If all 3 retries are exhausted (i.e., 4 total attempts: 1 original + 3 retries), throw `AnthropicApiException` with status code 429 and the response body from the last attempt.

### AC4: Retry logic for 529 (overloaded)

When the API returns HTTP 529:
1. Use exponential backoff: 1 second, 2 seconds, 4 seconds.
2. Maximum 3 retry attempts.
3. If all 3 retries are exhausted, throw `AnthropicApiException` with status code 529 and the response body.

### AC5: Retry logic for 5xx (server error, not 529)

When the API returns a 5xx status code other than 529 (e.g., 500, 502, 503):
1. Wait 1 second.
2. Retry exactly once.
3. If the retry also fails, throw `AnthropicApiException` with the status code and response body.

### AC6: Non-retryable 4xx throws immediately

When the API returns a 4xx status code that is NOT 429 (e.g., 400, 401, 403, 404, 422):
1. Do NOT retry.
2. Immediately throw `AnthropicApiException` with the status code and the response body.

### AC7: Build clean

The project must compile without errors or warnings under `dotnet build` for the `Pinder.LlmAdapters` project targeting `netstandard2.0` with `LangVersion 8.0` and nullable reference types enabled.

## Edge Cases

### Empty API key
Both constructors must throw `ArgumentException` if `apiKey` is `null`, empty (`""`), or whitespace-only.

### Null request
`SendMessagesAsync(null)` must throw `ArgumentNullException` before any HTTP call is made.

### Cancellation during retry wait
If the `CancellationToken` is cancelled while the client is waiting between retries (e.g., during the `Retry-After` delay), the method must throw `OperationCanceledException` / `TaskCanceledException` promptly — it must not wait for the full delay to elapse.

### Retry-After header with non-integer value
If the `Retry-After` header contains a non-integer value (e.g., a date string or garbage), treat it as if the header were missing and use the 5-second default.

### Retry-After header with zero or negative value
If `Retry-After` parses to 0 or a negative number, retry immediately (no delay) — do not apply the 5-second default.

### Mixed error codes during retries
Each response is evaluated independently. For example: first attempt → 429, retry → 500. The 500 response gets its own retry policy (1 retry with 1s delay). The total retry budget is tracked per-status-category, not globally. However, the simplest correct implementation tracks retries globally (max 3 total retry attempts across all status codes) — either approach is acceptable as long as the per-category maximums are respected.

### Concurrent calls
Multiple concurrent calls to `SendMessagesAsync` on the same `AnthropicClient` instance must be safe. `HttpClient` is designed for concurrent use. No shared mutable state (retry counters, etc.) should exist at the instance level — all retry state must be local to the method call.

### Response body is null or empty on error
If the API returns an error status code with no body, `AnthropicApiException.ResponseBody` should be `null` or empty string — not throw during construction.

### Dispose called multiple times
`Dispose()` must be safe to call multiple times without throwing.

### Large response bodies
No special handling required — Newtonsoft.Json handles stream deserialization. The response body string is read fully before deserialization (standard `HttpContent.ReadAsStringAsync()` pattern).

## Error Conditions

| Condition | Exception Type | Details |
|-----------|---------------|---------|
| `apiKey` is null/empty/whitespace | `ArgumentException` | Thrown in constructor |
| `httpClient` is null (constructor 2) | `ArgumentNullException` | Thrown in constructor |
| `request` is null | `ArgumentNullException` | Thrown in `SendMessagesAsync` before HTTP call |
| HTTP 400, 401, 403, 404, 422 (non-429 4xx) | `AnthropicApiException` | Immediate, no retry. `StatusCode` = HTTP code, `ResponseBody` = raw body |
| HTTP 429 after 3 retries | `AnthropicApiException` | `StatusCode = 429`, `ResponseBody` = last response body |
| HTTP 529 after 3 retries | `AnthropicApiException` | `StatusCode = 529`, `ResponseBody` = last response body |
| HTTP 5xx (not 529) after 1 retry | `AnthropicApiException` | `StatusCode` = HTTP code, `ResponseBody` = last response body |
| Cancellation token fired | `OperationCanceledException` | Standard .NET cancellation pattern |
| Network failure (DNS, timeout, connection refused) | Unhandled — `HttpRequestException` propagates | Client does NOT retry on network failures (only HTTP status codes) |
| JSON deserialization failure | `JsonException` (Newtonsoft) propagates | If API returns 200 but with malformed JSON |

## Dependencies

### Build-time dependencies
- **Issue #205** (Pinder.LlmAdapters project scaffold + DTOs): Must be merged first. Provides the `.csproj`, namespace structure, and DTO classes (`MessagesRequest`, `MessagesResponse`, `ContentBlock`, `Usage`, `SystemBlock`, `Message`, `CacheControl`).
- **Newtonsoft.Json 13.0.3**: Referenced by the `Pinder.LlmAdapters.csproj` (from #205).
- **System.Net.Http**: Available via .NET Standard 2.0.

### Runtime dependencies
- **Anthropic API** (`https://api.anthropic.com/v1/messages`): The actual HTTP endpoint. Requires a valid API key.

### Consumers
- **Issue #208** (`AnthropicLlmAdapter`): Calls `SendMessagesAsync` to communicate with Claude. This is the sole expected consumer within the codebase.

### Test infrastructure
- Tests live in `tests/Pinder.LlmAdapters.Tests/AnthropicClientTests.cs` (test project created by #205).
- Tests use a mock `HttpMessageHandler` injected via the `AnthropicClient(string, HttpClient)` constructor — no real HTTP calls.
- Required test scenarios:
  1. Successful 200 response → returns deserialized `MessagesResponse`
  2. HTTP 429 with `Retry-After` header → retries, succeeds on retry → returns response
  3. HTTP 5xx → retries once with 1s delay, succeeds on retry → returns response
  4. HTTP 400 → throws `AnthropicApiException` immediately (no retry)
  5. All retries exhausted (e.g., persistent 429) → throws `AnthropicApiException`

## Implementation Notes

### HTTP Headers (exact set)
```
x-api-key: {apiKey}
anthropic-version: 2023-06-01
Content-Type: application/json
```

No other custom headers. Specifically, **no `anthropic-beta` header**.

### Serialization settings
Use `JsonConvert.SerializeObject(request)` with default Newtonsoft settings (or configure `CamelCasePropertyNamesContractResolver` + `NullValueHandling.Ignore` if DTOs use PascalCase properties — coordinate with #205 DTO definitions). The request body must match the Anthropic API's expected JSON schema (snake_case keys like `max_tokens`, `stop_reason`, `cache_control`).

### API endpoint
Hardcoded constant: `https://api.anthropic.com/v1/messages`

### API version
Hardcoded constant: `2023-06-01`

### Delay implementation
Use `Task.Delay(TimeSpan, CancellationToken)` to ensure cancellation is respected during retry waits.

### Target framework constraints
- .NET Standard 2.0, LangVersion 8.0
- No `record` types — use `sealed class`
- Nullable reference types enabled
- No additional NuGet packages beyond what #205 already references
