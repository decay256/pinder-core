# Contract: Issue #206 — AnthropicClient HTTP Client

## Component
`src/Pinder.LlmAdapters/Anthropic/AnthropicClient.cs`

## Maturity: Prototype
## NFR: latency target — p99 < 30s (network-bound to Anthropic API)

---

## Public Interface

```csharp
namespace Pinder.LlmAdapters.Anthropic
{
    /// <summary>
    /// Raw HTTP client for Anthropic Messages API.
    /// Owns: serialization, HTTP transport, retry logic.
    /// Does NOT own: prompt construction, response parsing beyond DTO deserialization.
    /// </summary>
    public sealed class AnthropicClient : IDisposable
    {
        /// <summary>Creates client with internally-owned HttpClient.</summary>
        /// <param name="apiKey">Anthropic API key. Must not be null/empty.</param>
        public AnthropicClient(string apiKey);

        /// <summary>Creates client with externally-provided HttpClient (for testing).</summary>
        /// <param name="apiKey">Anthropic API key. Must not be null/empty.</param>
        /// <param name="httpClient">Caller-owned HttpClient. Client does NOT dispose it.</param>
        public AnthropicClient(string apiKey, HttpClient httpClient);

        /// <summary>
        /// Sends a Messages API request with retry logic.
        /// </summary>
        /// <returns>Deserialized MessagesResponse.</returns>
        /// <exception cref="AnthropicApiException">
        /// On non-retryable 4xx, or after all retries exhausted.
        /// </exception>
        /// <exception cref="ArgumentNullException">If request is null.</exception>
        public Task<MessagesResponse> SendMessagesAsync(
            MessagesRequest request,
            CancellationToken ct = default);

        public void Dispose();
    }
}
```

## HTTP Headers (per Anthropic docs, verified 2025-03-30)

```
x-api-key: {apiKey}
anthropic-version: 2023-06-01
Content-Type: application/json
```

**NO `anthropic-beta` header.** Prompt caching is GA — `cache_control` in the request body is sufficient. Per vision concern #213, the beta header `prompt-caching-2024-07-31` must NOT be sent.

Add comment in code: `// Prompt caching is GA — no beta header required. cache_control in body is sufficient.`

## Retry Policy

| HTTP Status | Strategy | Max Retries |
|------------|----------|-------------|
| 429 | Read `Retry-After` header (seconds). If missing, use 5s. | 3 |
| 529 | Exponential backoff: 1s → 2s → 4s | 3 |
| 5xx (not 529) | Fixed 1s delay | 1 |
| 4xx (not 429) | Throw immediately | 0 |

On final failure after all retries: throw `AnthropicApiException(statusCode, responseBody)`.

## Serialization
- Request: `JsonConvert.SerializeObject(request)` → `StringContent` with `application/json`
- Response: `JsonConvert.DeserializeObject<MessagesResponse>(responseBody)`

## Endpoint
`POST https://api.anthropic.com/v1/messages`

## Test Requirements
Unit tests using mock `HttpMessageHandler`:
1. Successful request → deserializes response
2. 429 → retries with Retry-After, succeeds on retry
3. 5xx → retries once, succeeds on retry
4. 400 → throws immediately (no retry)
5. All retries exhausted → throws AnthropicApiException

**Test location:** `tests/Pinder.LlmAdapters.Tests/AnthropicClientTests.cs`

## Dependencies
- #205 (project scaffold, DTOs)

## Consumers
- #208 (AnthropicLlmAdapter)

## What this component does NOT own
- Prompt construction (that's SessionDocumentBuilder)
- Response parsing beyond JSON deserialization (that's AnthropicLlmAdapter)
- API key management (caller provides)
