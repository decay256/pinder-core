# Finding 2 Resolution: Silent Swallowing of LLM Failure Corruption & Success Improvement Faults in DeliveryStage

## Audit Finding Details
During turn execution in `DeliveryStage.ExecuteAsync`, if the player rolls a failure, the engine attempts to apply failure corruption using the LLM (`await _llm.ApplyFailureCorruptionAsync(...)`). If the adapter fails (due to service outages, throttling, or timeouts), `DeliveryStage` caught the exception and silently set `deliveredMessage = null`, forcing a deterministic `DeliveryOverlay.Apply(...)` fallback. Similarly, if `GetSuccessImprovementAsync` failed on a successful roll, the general `catch` block silently intercepted the fault and continued. Doing so completely silently left operators blind to critical adapter failures.

## Solution Implemented
To resolve this issue while adhering to the Fail-Fast and transient recovery guidelines, the following modifications were applied to `/root/projects/pinder-core/src/Pinder.Core/Conversation/DeliveryStage.cs`:
1. Introduced a robust static helper method `IsRetryableException(Exception ex)` that distinguishes retryable/transient exceptions (e.g. rate limit 429, service outage 503, network issues, or timeouts) from non-retryable exceptions (e.g. authorization, model not found, validation error).
2. Refactored the `try-catch` blocks around `ApplyFailureCorruptionAsync` and `GetSuccessImprovementAsync`:
   - For **non-retryable** exceptions: The exception is propagated immediately (Fail-Fast discipline).
   - For **retryable** exceptions: The exception is caught, a clear diagnostic warning trace is written to `Console.Error` detailing the exception, and the logical fallback degradation path is executed (falling back to deterministic overlay or skipping success improvement).
3. Developed two comprehensive unit tests in `Issue1311_RestoreLlmDeliveryTests.cs` to explicitly verify and safeguard these behaviors:
   - `ApplyFailureCorruption_ThrowsException_WhenLlmThrowsNonRetryableException`: Asserts that a non-transient validation exception correctly propagates up and is not swallowed.
   - `ApplyFailureCorruption_FallsBackToDeliveryOverlay_WhenLlmThrowsRetryableException`: Asserts that a transient `LlmTransportException` (specifically, `RateLimited`) is caught, falls back gracefully to the deterministic overlay, and logs details.

## Verification
- Built the `Pinder.Core` library using `dotnet build` (succeeded with zero errors).
- Executed the unit test suite in `Pinder.Core.Tests` targeting `Issue1311_RestoreLlmDeliveryTests.cs` (all 5 tests passed successfully, confirming both expected fallbacks and expected fail-fast propagation).
