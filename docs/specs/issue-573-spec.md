**Module**: docs/modules/llm-adapters.md

## Overview

This specification details the complete removal of the NarrativeBeat LLM call from the Anthropic LLM Adapter. Making an LLM call for the narrative beat polluted the conversation history and risked out-of-character behavior. `GetInterestChangeBeatAsync` must immediately return `null` without calling the LLM API. All tests validating the old behavior must be cleaned up, and a new test must be added to explicitly verify that the method returns `null` and avoids making API calls.

## Function Signatures

```csharp
namespace Pinder.LlmAdapters.Anthropic
{
    public sealed class AnthropicLlmAdapter : ILlmAdapter, IDisposable
    {
        // Other methods omitted for brevity

        public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context);
    }
}
```

## Input/Output Examples

**Input:**
```csharp
var context = new InterestChangeContext("Velvet", 10, 11, InterestState.Interested);
var result = await adapter.GetInterestChangeBeatAsync(context);
```

**Output:**
```csharp
null
```

## Acceptance Criteria

### 1. No LLM Call on GetInterestChangeBeatAsync
- **Given** `GetInterestChangeBeatAsync` is called on the `AnthropicLlmAdapter`
- **When** the method is executed
- **Then** it immediately returns `Task.FromResult<string?>(null)`.
- **And** no Anthropic HTTP API request is made.

### 2. Clean Up Dead Code
- **Given** existing tests in `AnthropicLlmAdapterTests.cs` testing `GetInterestChangeBeatAsync`
- **When** updating the test suite
- **Then** any skipped tests (e.g., `GetInterestChangeBeatAsync_no_system_blocks`) must be permanently deleted.
- **And** any old behavior tests (e.g., `GetInterestChangeBeatAsync_empty_response_returns_null` mocking API responses) must be removed.

### 3. Test Null Return Behavior
- **Given** the test suite `AnthropicLlmAdapterTests.cs`
- **When** testing the `AnthropicLlmAdapter`
- **Then** a new test `GetInterestChangeBeatAsync_ReturnsNullWithoutCallingApi` must be added.
- **And** the test must verify that `GetInterestChangeBeatAsync` returns `null` without invoking the `HttpClient` (using a mock handler that asserts it was not called or simply by passing a context).

### 4. Context Validation
- **Given** `GetInterestChangeBeatAsync` is called with a `null` context
- **When** the method is executed
- **Then** it must throw an `ArgumentNullException`.

## Edge Cases
- **Missing or Disconnected HttpClient**: Since the method immediately returns `null` before utilizing the HTTP client, it should safely handle scenarios where the client might be disposed or disconnected without throwing `ObjectDisposedException` or similar networking exceptions.

## Error Conditions
- Passing `null` to `GetInterestChangeBeatAsync` throws `ArgumentNullException(nameof(context))`.

## Dependencies
- `Pinder.Core.Conversation.InterestChangeContext`
