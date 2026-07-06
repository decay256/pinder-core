# issue-1314: Resilient Structured LLM Wrapper with Stateless Retries Spec

This document details the architectural design and implementation plan for wrapping LLM operations in `PinderLlmAdapter` with a robust, high-level structured wrapper that supports stateless retries on parsing violations, configurable retry counts, backoff, and robust telemetry.

---

## 1. Summary & Scope

### Goal
Under reasoning-heavy tasks, models (such as `gemini-3.5-flash`) can intermittently fail formatting constraints or leak their chain-of-thought inner monologue into output blocks. Currently, when an output fails strict regex parsing or signature validation, a parsing exception (`LlmContractException`) is immediately thrown. 

In the non-fast (slow) gameplay path, this exception aborts the turn resolution, leading to an HTTP 500 error in the API and completely locking the active session in a permanent full-screen loading state. This spec outlines a stateless, self-correcting retry system wrapped directly inside the adapter layer.

### Scope
- **In-Scope**:
  - Enhancing `PinderLlmAdapterOptions` to support configurable retry and backoff parameters.
  - Modifying `PinderLlmAdapter.cs` (`GetDialogueOptionsAsync` and `GetDateeResponseAsync`) to run their transport-and-parse loops inside a stateless retry block.
  - Defining clean telemetry hooks for recording intermediate parsing failures.
  - Adding unit/integration tests with simulated transient parsing errors to verify correct retry and recovery behavior.
- **Out-of-Scope**:
  - Rewriting low-level `ILlmTransport` implementations or changing the wire protocols of the actual model APIs.
  - Modifying actual game prompt/guideline text or game character cards.

---

## 2. Current-State Analysis & Failure Points

1. **Dialogue Options Parsing** (`PinderLlmAdapter.cs` lines 53–122):
   - A single call to `_transport.SendAsync` is executed.
   - `DialogueOptionParsers.ParseDialogueOptionsStrict` parses the result.
   - If `errorCode != null`, it immediately logs a violation and throws `LlmContractException`.
   - Any transient formatting glitch causes the entire web API call to fail with HTTP 500.

2. **Datee Response Parsing** (`PinderLlmAdapter.cs` lines 135–244):
   - A single shot or multi-turn call to `SendAsync`/`SendStatefulDateeAsync` is executed.
   - `GmOutputContract.ValidateSignalsStrict` checks for malformed signals, throwing an exception if invalid.
   - This failure immediately propagates, abandoning the player's turn resolution.

3. **Stateless Nature of Failures**:
   - Because these validation checks run *before* the output is committed to the database history or applied to the in-memory game state, the failures are completely safe to retry without side effects or duplicating history blocks.

---

## 3. Proposed Design

### A. Configuration Extensions (`PinderLlmAdapterOptions.cs`)
We will extend `PinderLlmAdapterOptions` with properties to control retry behavior:
```csharp
public sealed class PinderLlmAdapterOptions
{
    // ... existing options ...

    /// <summary>
    /// Gets or sets the maximum number of times to retry an LLM call if a contract/parsing violation occurs.
    /// Default is 3. Set to 1 to disable retries.
    /// </summary>
    public int MaxContractViolationRetries { get; set; } = 3;

    /// <summary>
    /// Gets or sets the base millisecond delay to wait before retrying on a contract violation.
    /// Uses exponential backoff. Default is 100ms.
    /// </summary>
    public int ContractViolationBackoffMs { get; set; } = 100;
}
```

### B. Stateless Retry Wrapper Loop (`PinderLlmAdapter.cs`)
We will wrap `GetDialogueOptionsAsync` and `GetDateeResponseAsync` operations in a stateless retry block.

#### High-Level Pattern:
1. Initialize `attempt = 0`.
2. Inside a `while (attempt < MaxRetries)` block:
   - Run `_transport.SendAsync`.
   - Parse and validate response.
   - If parsed successfully: return results.
   - If contract validation fails:
     - Invoke `_options.OnLlmContractViolation?.Invoke(violation)` to capture the telemetry/warning.
     - Increment `attempt`.
     - If `attempt >= MaxRetries`, throw the final `LlmContractException`.
     - Else, compute exponential backoff: `delay = BackoffMs * Math.Pow(2, attempt - 1)` and `await Task.Delay(delay, ct)`.

---

## 4. Implementation Details

### Affected Files
1. `src/Pinder.Core/Interfaces/ILlmAdapter.cs` (Interface stays identical—zero signature drift).
2. `src/Pinder.LlmAdapters/PinderLlmAdapterOptions.cs` (Add configurable properties).
3. `src/Pinder.LlmAdapters/PinderLlmAdapter.cs` (Introduce the retry loops around both options generation and datee responses).
4. `tests/Pinder.LlmAdapters.Tests/Issue1314_ResilientStructuredLlmWrapperTests.cs` (New test suite).

---

## 5. Acceptance Criteria

- [ ] `PinderLlmAdapterOptions` includes `MaxContractViolationRetries` (int, default: 3) and `ContractViolationBackoffMs` (int, default: 100).
- [ ] `PinderLlmAdapter.GetDialogueOptionsAsync` successfully retries on any transient `StrictDialogueOptionsParser` validation failure, only throwing an exception after exhausting all attempts.
- [ ] `PinderLlmAdapter.GetDateeResponseAsync` successfully retries on empty output or `MalformedSignals` contract failures, preserving pristine context between attempts.
- [ ] Intermediate failures call the telemetry callback `OnLlmContractViolation` on every failed attempt with appropriate attempt-tracking logs.
- [ ] Ensure the retry loop is strictly stateless and does not pollute the live `GameSession` active history with intermediate failed outputs.
- [ ] Robust test coverage (mocking a transient failure transport) verifies correct retry-and-recovery behavior under various attempts.
