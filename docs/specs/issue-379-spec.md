**Module**: `docs/modules/llm-adapters.md` (Test Cleanup)

## Overview
The `AnthropicClientSpecTests.cs` test file contains approximately 40 tests, duplicating about 70% of the tests already present in `AnthropicClientTests.cs`. This specification outlines the merging of 8 specific edge-case and HTTP status code tests from `AnthropicClientSpecTests.cs` into `AnthropicClientTests.cs`, followed by the complete deletion of the duplicate `AnthropicClientSpecTests.cs` file. This refactoring will reduce technical debt and net test count while preserving essential code coverage.

## Function Signatures
The following test methods (from `AnthropicClientSpecTests.cs`) must be migrated to `AnthropicClientTests.cs`. Implementers should rename them to match the `SendMessagesAsync_...` naming convention established in `AnthropicClientTests.cs`:

1. `AC6_403_NoRetry()`
2. `AC6_404_NoRetry()`
3. `AC6_422_NoRetry()`
4. `AC5_502_RetriesOnce_Succeeds()`
5. `AC5_503_RetriesOnce_ThenThrows()`
6. `Edge_RequestSentToCorrectEndpoint()`
7. `Edge_UsesPostMethod()`
8. `Edge_CacheControl_SerializedInRequestBody()`

*Note: No changes to production `Pinder.LlmAdapters` code are required; this is strictly a test refactoring.*

## Input/Output Examples
- **403/404/422 Responses**: Setting the mock HTTP handler to return HTTP status 403, 404, or 422. Output: Immediately throws `AnthropicApiException` with the corresponding status code without triggering the retry loop.
- **502/503 Responses**: Setting the mock HTTP handler to return HTTP 502/503. Output: Triggers exactly one retry. If the second attempt returns 200 OK, the test passes. If the second attempt returns 503 again, it throws `AnthropicApiException`.
- **Cache Control**: Inspecting the intercepted `HttpRequestMessage` content payload. Output: Must assert that `"cache_control"` is successfully serialized within the JSON payload of the request body.

## Acceptance Criteria
- [ ] The 8 unique test cases listed above are migrated into `AnthropicClientTests.cs` and adapted to use the file's native `MockHttpMessageHandler`.
- [ ] `AnthropicClientSpecTests.cs` is completely deleted from the file system and test project.
- [ ] All remaining tests in the test suite pass cleanly.
- [ ] Net test count across the solution is reduced by approximately 34 tests.

## Edge Cases
- **HTTP Method validation**: Ensure that the ported `Edge_UsesPostMethod` accurately captures the outgoing request and asserts `HttpMethod.Post`.
- **Endpoint validation**: Ensure that the ported `Edge_RequestSentToCorrectEndpoint` accurately asserts that the request URI strictly targets the `/v1/messages` endpoint.
- **Serialization validation**: The `Edge_CacheControl_SerializedInRequestBody` test must verify that `cache_control` properties are present in the JSON body, effectively verifying that the request builder includes caching directives.

## Error Conditions
- **Missing Migrations**: If the test file is deleted without successfully migrating the 8 unique cases, coverage for specific status codes (403, 404, 422, 502, 503) and caching mechanism validation will be lost.
- **Mock Handler Mismatch**: `AnthropicClientSpecTests.cs` uses `SequenceHandler`, whereas `AnthropicClientTests.cs` uses `MockHttpMessageHandler`. Failing to adapt the tests to use `MockHttpMessageHandler` will cause compilation or runtime failures.

## Dependencies
- **Component**: `Pinder.LlmAdapters.Tests`
- **Frameworks**: `xUnit`, `System.Net.Http` Mocking
