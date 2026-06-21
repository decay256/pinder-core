# issue-1220: Provider-Neutral LLM Request/Response Contract Design Doc

This document serves as a design spike and migration plan for transitioning the `pinder-core` LLM transport layers to a provider-neutral request/response contract. This is a design-only plan and introduces NO production code or transport rewrites under this issue.

---

## 1. Summary & Scope

### Goal
The current low-level transport contracts `ILlmTransport` and `IStreamingLlmTransport` enforce a single-turn `(systemPrompt, userMessage)` interface. This forces higher-level adapters to flatten rich, multi-turn conversation history into a single monolithic user message embedded with custom tag-identifiers, which are subsequently re-parsed back into structured arrays by specific provider clients. 

This design spike proposes a **provider-neutral request/response contract** that natively supports multi-turn arrays of role-labeled messages, cache headers, model reasoning capture, and structured validation.

### Scope
- **In-Scope**:
  - Detailed inventory of current history-flattening, tag re-parsing, and streaming fallback behaviors.
  - Enum phase inventory and categorization.
  - Designing provider-neutral contract types (`LlmRequest`, `LlmMessage`, `LlmResponse`, `LlmRole`, `CacheControlHint`).
  - Mapping logic specification for Anthropic, OpenAI-compatible, and Groq transports.
  - Isolated model reasoning capture design (diagnostics-only, separated from gameplay text).
  - High-observability trace/log event structures for response anomalies, integrated with existing codebase callback patterns.
  - An ordered, low-risk, multi-step migration plan.
  - Concrete follow-up implementation tickets.
- **Out-of-Scope**:
  - Implementing or rewriting any production transport or adapter code.
  - Modifying actual game prompt/guideline text or prompt copy.
  - Modifying active runtime game session states.

---

## 2. Current-State Inventory

A review of the active codebase reveals significant overhead, token waste, and risks introduced by the single-turn transport boundary:

### 1. Magic-Tag Flattening in `PinderLlmAdapter`
In `src/Pinder.LlmAdapters/PinderLlmAdapter.cs` (lines 777–806), the multi-turn session history is flattened into a single string in the `SendStatefulAsync` helper method:
```csharp
private Task<string> SendStatefulAsync(
    string systemPrompt,
    string currentUserContent,
    IReadOnlyList<ConversationMessage> priorHistory,
    double temperature,
    string phase,
    CancellationToken ct = default)
{
    // Multi-turn: prefix prior exchanges into the user message for context.
    var contextBuilder = new StringBuilder();
    contextBuilder.AppendLine("[PREVIOUS CONVERSATION CONTEXT]");
    for (int i = 0; i < priorHistory.Count; i++)
    {
        var msg = priorHistory[i];
        string displayRole = string.Equals(msg.Role, ConversationMessage.AssistantRole, StringComparison.OrdinalIgnoreCase)
            ? "DATEE" : "PLAYER";
        contextBuilder.AppendLine($"[{displayRole}] {msg.Content}");
    }
    contextBuilder.AppendLine();
    contextBuilder.AppendLine("[CURRENT TURN]");
    contextBuilder.Append(currentUserContent);

    return _transport.SendAsync(
        systemPrompt,
        contextBuilder.ToString(),
        temperature,
        _options.MaxTokens,
        phase: phase,
        ct: ct);
}
```
This forces all conversational turns to be serialized as a single plain text string, losing native API message structure.

### 2. Anthropic Tag Re-Parsing
To restore structure and make prompt caching work, Anthropic must reverse this flattening. In `src/Pinder.LlmAdapters/Anthropic/AnthropicRequestBuilders.cs` (lines 86–218), `BuildMessages` splits the incoming monolithic user message:
```csharp
public static Message[] BuildMessages(string userMessage)
{
    if (userMessage == null) throw new ArgumentNullException(nameof(userMessage));

    if (!userMessage.StartsWith("[PREVIOUS CONVERSATION CONTEXT]"))
    {
        return new[] { new Message { Role = "user", Content = userMessage } };
    }
    // ... loops, parses "[PLAYER] " -> role "user", "[DATEE] " -> role "assistant"
    // ... applies cache_control: { type: "ephemeral" } to histUserIdx and lastUserIdx
}
```
This is a brittle string-parsing pipeline. If the model or input formatting deviates slightly, the alternating role structure is destroyed.

### 3. OpenAI Monolithic Wrappers
In contrast, the OpenAI transport (`src/Pinder.LlmAdapters/OpenAi/OpenAiTransport.cs` lines 59–70) does not attempt to parse these tags. Instead, it sends the raw, flattened string as a single massive `"user"` message:
```csharp
var request = new
{
    model = _model,
    max_tokens = maxTokens,
    temperature = temperature,
    messages = new object[]
    {
        new { role = "system", content = systemContent },
        new { role = "user", content = userMessage } // Flattened monolithic block
    }
};
```
This is suboptimal:
1. It prevents OpenAI-compatible models from properly resolving alternating user/assistant message arrays.
2. It completely invalidates or severely degrades native prompt-caching capabilities on compatible gateways.

### 4. OpenAI Reasoning-as-Text Risk & Fallback Ambiguity
In `src/Pinder.LlmAdapters/OpenAi/OpenAiClient.cs` (lines 150–194), the non-streaming helper `ExtractAssistantText` handles empty response content with fallback reasoning channels:
```csharp
// 1) message.content
var content = message["content"]?.Value<string?>();
if (!string.IsNullOrWhiteSpace(content))
    return content!;

// 2) message.reasoning (OpenRouter / OpenAI)
var reasoning = message["reasoning"]?.Value<string?>();
if (!string.IsNullOrWhiteSpace(reasoning))
    return reasoning!;

// 3) message.reasoning_details[i].summary — concatenated
```
In streaming (`src/Pinder.LlmAdapters/OpenAi/OpenAiStreamingTransport.cs` lines 359–390), `ExtractContentFragmentsOrThrow` performs a similar extraction and yields tokens sequentially from `content`, then `reasoning`, and finally `reasoning_details` summaries.

**The Risk:** Under-the-hood model thoughts, meta-commentary, or chain-of-thought logic are mixed directly into the yielded gameplay text. A player could see the character's internal generation plans instead of pure dialogue.
**The Ambiguity:** If no content or reasoning exists, `ExtractAssistantText` silently returns `""`. This masks critical API, validation, or selection failures as a successful blank response.

---

## 3. Phase Inventory & Classification

To establish structured contract requirements, we categorize the current `LlmPhase` enum constants (from `src/Pinder.Core/Interfaces/LlmPhase.cs`) into three functional classes:

| Constant | Phase String | Output Class | Description |
|---|---|---|---|
| `DialogueOptions` | `dialogue_options` | **Required Structured Output** | Set of multiple candidate dialogue options for player selection. Requires JSON schema or structured line-parsing. |
| `PsychologicalStake` | `psychological_stake` | **Required Structured Output** | Setup-phase narrative elements / stake fragments. Must map to structured JSON arrays. |
| `DateeResponse` | `datee_response` | **Required Final Text** | Main character reply text. Must be raw, clean, user-visible narrative text. |
| `Steering` | `steering` | **Required Final Text** | Direct prompt questions generated for players. Must be user-visible text. |
| `InterestChangeBeat` | `interest_change_beat` | **Required Final Text** | Occasional narrative commentary on interest changes. Must be user-visible text. |
| `HorninessOverlay` | `horniness_overlay` | **Required Final Text** | Message modifier layer. Must be user-visible text. |
| `ShadowCorruption` | `shadow_corruption` | **Required Final Text** | Message modifier layer. Must be user-visible text. |
| `TrapOverlay` | `trap_overlay` | **Required Final Text** | Message modifier layer. Must be user-visible text. |
| `OutfitDescription` | `outfit_description` | **Required Final Text** | One-time character clothes setup. Must be user-visible text. |
| `DramaticArc` | `dramatic_arc` | **Required Final Text** | Session soft narrative outline. Must be clean readable text. |
| `BackgroundStory` | `background_story` | **Required Final Text** | Character profile sheet generator. Must be clean text blocks. |
| `Delivery` | `delivery` | *RETIRED / Historical* | Historical costing reference ONLY. No active LLM calls are mapped here. |
| `CallbackStrip` | `callback_strip` | *Non-LLM Pass* | Reserved for potential future LLM variants, currently runs purely as inline regex. |
| `Unknown` | `unknown` | *Fallback* | Default or untyped phase indicator. |

### Global Classification Rule:
- **Optional Diagnostic Output** applies universally across all phases: internal chain-of-thought, reasoning tokens, and token metadata must be isolated into diagnostic properties and **never** returned as part of primary gameplay content.

---

## 4. Proposed Provider-Neutral Contract

The proposed neutral contract isolates the structure, metadata, validation, and diagnostics of the exchange:

```csharp
namespace Pinder.Core.Interfaces
{
    public enum LlmRole
    {
        System,
        User,
        Assistant
    }

    public enum CacheControlHint
    {
        None,
        Ephemeral // Represents an entry point that should trigger provider-side caching
    }

    public sealed class LlmMessage
    {
        public LlmRole Role { get; set; } = LlmRole.User;
        public string Content { get; set; } = string.Empty;
        public CacheControlHint CacheHint { get; set; } = CacheControlHint.None;
    }

    public sealed class LlmRequest
    {
        /// <summary>Natively structured multi-turn message history.</summary>
        public IReadOnlyList<LlmMessage> Messages { get; set; } = Array.Empty<LlmMessage>();
        
        public double Temperature { get; set; } = 0.9;
        
        public int MaxTokens { get; set; } = 1024;
        
        /// <summary>Metadata label for tracking and billing.</summary>
        public string Phase { get; set; } = LlmPhase.Unknown;

        /// <summary>For structured output phases, specifies JSON expectations.</summary>
        public string? ResponseFormatJsonSchema { get; set; }
        
        public bool RequireJson { get; set; }
    }

    public sealed class LlmResponse
    {
        /// <summary>The primary, user-visible content generated by the model.</summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>Isolated reasoning/thinking text (diagnostics-only).</summary>
        public string? ReasoningContent { get; set; }

        public string ModelUsed { get; set; } = string.Empty;
        
        public string Provider { get; set; } = string.Empty;

        public SessionTokenUsage Usage { get; set; } = SessionTokenUsage.Zero;
    }
}
```

---

## 5. Transport Mapping

Under the new contract, transports map the structures directly to provider API contracts without performing brittle string searches or parsing magic tags:

### 1. Anthropic Transport
- **System Prompt**: System-level `LlmRole.System` messages are filtered out and assigned to the top-level Anthropic `system` request field.
- **Messages Array**: `LlmRole.User` and `LlmRole.Assistant` map directly to Alternating Messages inside the `MessagesRequest` body.
- **Prompt Caching**: If `LlmMessage.CacheHint == CacheControlHint.Ephemeral`, the content blocks generated for that message automatically attach:
  ```json
  "cache_control": { "type": "ephemeral" }
  ```
  This eliminates legacy line-parsing based index determination.

### 2. OpenAI / OpenAI-Compatible Transports
- **Messages Array**: Maps natively to standard OpenAI arrays of `{ role, content }`:
  - `LlmRole.System` -> `role: "system"`
  - `LlmRole.User` -> `role: "user"`
  - `LlmRole.Assistant` -> `role: "assistant"`
- **Prompt Caching**: For compatible platforms (e.g. OpenRouter, DeepSeek), cache headers are attached natively when `CacheHint == Ephemeral` is supplied, using standard OpenAI/OpenRouter compliant metadata blocks instead of system-only hacks.

### 3. Groq / Low-Latency Overlay Transports
- Same mapping as OpenAI-Compatible. Alternating message arrays allow Groq models to see clean context without getting confused by tag-decorated monolithic paragraphs.

---

## 6. Reasoning Content Handling

Strict gameplay contracts require that we treat reasoning content exclusively as diagnostic telemetry:

1. **Isolation in Response**: Both non-streaming clients and streaming transports parse `reasoning` and `reasoning_details` exclusively into `LlmResponse.ReasoningContent`.
2. **Fallback Elimination**: Under the new contract, `LlmResponse.Content` will **never** fall back to reasoning fields. If the primary content is empty, validation immediately fails.
3. **Decorator Integration**:
   - The existing `ThinkingStrippingLlmTransport` decorator (which strips inline `<think>...</think>` tags for models that output thinking inline in the content block) continues to act as a fallback filter.
   - However, for native reasoning models, the decorator is bypassed because the underlying transport populates `LlmResponse.ReasoningContent` directly, leaving `LlmResponse.Content` completely clean.
4. **Failure Policy (#1215)**: If primary content is missing but reasoning content is present, the transport raises an `LlmContractException` with structured violation details. The thinking is preserved in logs, but never exposed to the client.

---

## 7. Response-Shape Anomaly Observability

To catch provider regressions or silent format shifts without risking privacy (no raw prompts, conversation history, or API keys in logs), we specify a structured monitoring event.

### Structured Telemetry Event: `LlmResponseShapeAnomalyEvent`
- **Fields**:
  - `Provider` (e.g., `"openai"`, `"openrouter"`, `"groq"`, `"anthropic"`)
  - `Model` (e.g., `"deepseek-r1"`, `"claude-3-5-sonnet"`)
  - `Phase` (e.g., `"datee_response"`, `"dialogue_options"`)
  - `MissingField` (e.g., `"choices[0].message.content"`, `"delta.content"`)
  - `ContentSourceSelected` (e.g., `"PrimaryContent"`, `"ReasoningFallback"`, `"None"`)
  - `AnomalyDescription` (e.g., `"Primary content was empty; rejected turn via strict contract."`)
  - `SessionId` (Guid/String - unique game identifier)
  - `TurnId` (Int - current conversation index)

### Integration with Existing Callbacks
The anomaly event structures tie directly into the core logging and mitigation infrastructure:
1. **`OnLlmContractViolation` (from `PinderLlmAdapterOptions`)**:
   When a response contains shape anomalies (e.g., missing main content, or unexpected json schema format), the adapter instantiates an `LlmContractViolation` payload detailing the anomaly and fires the callback. This triggers game engine fallback systems (such as falling back to a secondary model).
2. **`OnOverlayDegraded` (for Overlay transforms)**:
   If a modifier phase (such as `TrapOverlay` or `HorninessOverlay`) suffers from a provider shape anomaly, the adapter catches the exception, fires the `OnOverlayDegraded` callback with an `OverlayDegradedEvent`, and applies the safe-fail default (such as skipping the rewrite so the game does not freeze).

---

## 8. Migration Slices

To minimize runtime risk and ensure backward-compatibility, we outline an ordered, independently-shippable migration plan:

### Slice 1: Introduce Contract Types & Interfaces (Additive)
- **Scope**: Define the new `LlmRequest`, `LlmResponse`, `LlmMessage` and metadata enums. Add default-implemented overloads to `ILlmTransport` and `IStreamingLlmTransport`. Maintain legacy string-based interfaces to prevent breaking existing code.
- **Risk**: Extremely low. Code remains 100% backward-compatible.
- **Regression Tests**: Verify Pinder assemblies build with 0 errors. Run existing unit tests verifying legacy implementations behave identically.

### Slice 2: Role-Aware Message Mapping for Anthropic (Flagged)
- **Scope**: Update the Anthropic client to implement the new `LlmRequest`/`LlmResponse` contract. Add a feature flag (`PinderLlmAdapterOptions.UseProviderNeutralLlmContracts`). When active, bypass tag flattening in `PinderLlmAdapter` and pass structured messages to the new Anthropic contract implementation.
- **Risk**: Low. Flag-guarded path allows quick rollbacks.
- **Regression Tests**: "Golden Parity Tests" comparing legacy tag-parsed message outputs against the structured `LlmMessage` inputs to prove byte-identical payload generation on the Anthropic API request level.

### Slice 3: Role-Aware Message Mapping for OpenAI-Compatible (Flagged)
- **Scope**: Update `OpenAiTransport` and `OpenAiStreamingTransport` to implement the structured contract, mapping `LlmMessage` arrays natively into standard OpenAI message arrays when the feature flag is active.
- **Risk**: Low. Flag-guarded path.
- **Regression Tests**: Verify multi-turn conversations operate smoothly under OpenAI-compatible configurations. Assert OpenAI/OpenRouter request structures are correctly formatted as alternating lists.

### Slice 4: Reasoning-Isolate & Strict Validation Enforcement
- **Scope**: Refactor `OpenAiClient` and `OpenAiStreamingTransport` to separate reasoning from primary content completely. Treat missing content as a violation. Integrate strict shape anomaly trace events.
- **Risk**: Medium. Changing the fallback mechanism might trigger contract failures on misconfigured endpoints.
- **Regression Tests**: Mock response payloads with empty `content` and populated `reasoning`. Assert that an `LlmContractException` is raised and `OnLlmContractViolation` fires correctly.

### Slice 5: Decommissioning and Cleanup
- **Scope**: Remove the feature flag, make the new provider-neutral contract the only path, remove legacy string-based `SendAsync` / `SendStreamAsync` transport overloads, and delete `AnthropicRequestBuilders.BuildMessages` alongside old flattening helpers in `PinderLlmAdapter.cs`.
- **Risk**: Low. All systems will have already been tested and vetted under the flag.
- **Regression Tests**: Full integration suite must pass. Code quality coverage reports must show complete removal of legacy tags.

---

## 9. Proposed Follow-Up Tickets

These structured tickets outline the concrete implementation steps required to execute the migration plan:

### Ticket 1: [T-1] Define Provider-Neutral LLM Contract Types & Interfaces
- **Body**: Implement the new `LlmRequest`, `LlmResponse`, `LlmMessage`, `LlmRole`, and `CacheControlHint` types under `Pinder.Core.Interfaces`. Add default-implemented contract overloads to the `ILlmTransport` and `IStreamingLlmTransport` interfaces to ensure full backward compatibility.

### Ticket 2: [T-2] Refactor Anthropic Transport to Neutral Contract (Flagged)
- **Body**: Implement the structured contract inside the Anthropic client. Put the alternating message array construction behind the `UseProviderNeutralLlmContracts` feature flag. Implement Golden Parity Tests matching the legacy tag-parsed payloads against the structured input formats.

### Ticket 3: [T-3] Refactor OpenAI & Other Transports to Neutral Contract (Flagged)
- **Body**: Refactor `OpenAiTransport` and `OpenAiStreamingTransport` to support the structured contract. Map `LlmMessage[]` natively to OpenAI message JSON blocks under the neutral-contract feature flag, enabling native multi-turn support and cache control blocks on compatible gateways.

### Ticket 4: [T-4] Integrate Multi-Turn Conversation History directly into PinderLlmAdapter
- **Body**: Refactor `PinderLlmAdapter.SendStatefulAsync` to build a structured list of `LlmMessage` instead of a flattened magic-tagged string when `UseProviderNeutralLlmContracts` is enabled. Route requests through the new transport interface overloads.

### Ticket 5: [T-5] Clean Up Legacy Tag Flattening & Re-Parsing (Decommissioning)
- **Body**: Remove the `UseProviderNeutralLlmContracts` feature flag. Retain only the structured neutral contract path. Decommission the string-based transport methods. Delete `AnthropicRequestBuilders.BuildMessages` and the legacy tag flattening builders.

### Ticket 6: [T-6] Isolate Reasoning Content to Diagnostics-Only
- **Body**: Modify OpenAI client text extractors and streaming transport chunk parsers to isolate `reasoning` and `reasoning_details` into `LlmResponse.ReasoningContent`. Ensure that missing primary `content` never falls back to reasoning content. Populate telemetry appropriately.

### Ticket 7: [T-7] Strict Empty-Content Validation and Observability
- **Body**: Raise `LlmContractException` and fire `OnLlmContractViolation` when primary content is empty. Implement the `LlmResponseShapeAnomalyEvent` telemetry event to track provider abnormalities in production without leaking secrets.
