# LLM Adapters

## Overview
The LLM Adapters module (`Pinder.LlmAdapters`) provides prompt templates and API clients for integrating large language models into Pinder's conversation game. It defines the structured instruction templates that guide LLM output (dialogue options, datee responses, interest beats) and handles communication with external LLM providers.

> Supersession note (#1332): older entries in this module that describe
> provider-persistent "stateful conversation mode", `StartConversation`, or
> `HasActiveConversation` are historical. The current active
> `IStatefulLlmAdapter` shape is stateless adapter calls with engine-owned
> semantic history passed as an argument. See
> [`../specs/issue-1332-datee-prerequisite-architecture.md`](../specs/issue-1332-datee-prerequisite-architecture.md).

## Key Components

| File | Description |
|------|-------------|
| `PromptTemplates.cs` | Static instruction templates (§3.2–3.8) with `{placeholder}` tokens for dynamic content; includes resistance band descriptors |
| `SessionDocumentBuilder.cs` | Fills placeholder tokens in prompt templates with session-specific data; injects datee profile, texting style, and resistance stance into user messages |
| `Anthropic/AnthropicClient.cs` | HTTP client for the Anthropic Messages API |
| `Anthropic/AnthropicLlmAdapter.cs` | Adapter implementing the LLM interface using Anthropic's API |
| `Anthropic/AnthropicOptions.cs` | Configuration options for the Anthropic client |
| `Anthropic/CacheBlockBuilder.cs` | Builds cache-control blocks for Anthropic prompt caching |
| `Anthropic/AnthropicApiException.cs` | Exception type for Anthropic API errors |
| `Anthropic/Dto/MessagesRequest.cs` | Request DTO for the Anthropic Messages API |
| `Anthropic/Dto/MessagesResponse.cs` | Response DTO for the Anthropic Messages API |
| `Anthropic/Dto/ContentBlock.cs` | Content block DTO for Anthropic message payloads |
| `src/Pinder.Core/Interfaces/IStatefulLlmAdapter.cs` | Interface extending `ILlmAdapter` with engine-owned DATEE history passed into adapter calls |
| `ConversationSession.cs` | Legacy Anthropic-native conversation helper; not the current `PinderLlmAdapter` session contract |
| `GameDefinitionYamlContentTests.cs` (test) | 30 content-validation tests ensuring `game-definition.yaml` has correct structure and Pinder-specific creative content |
| `ConversationSessionTests.cs` (test) | 16 unit tests for `ConversationSession` construction, append, BuildRequest, and edge cases |
| `AnthropicLlmAdapterStatefulTests.cs` (test) | 12 tests for stateful adapter behavior across all 4 `ILlmAdapter` methods |
| `Issue541_StatefulConversationTests.cs` (test) | Integration tests for stateful conversation mode — multi-turn accumulation, error recovery, stateless fallback |
| `Issue541_AdditionalTests.cs` (test) | Additional coverage for snapshot isolation, role correctness, message ordering, system block caching, and API failure resilience |
| `Issue542_StatefulSession_TestEngineerTests.cs` (test) | Spec-driven tests for `IStatefulLlmAdapter` interface shape, `GameSession` constructor stateful detection, system prompt format, and backward compatibility |
| `GameDefinition.cs` | Sealed data carrier for game-level creative direction and rule constants; includes `LoadFrom(yamlContent)` YAML parsing. `PinderDefaults` is a test/tool convenience, not production fallback wiring |
| `SessionSystemPromptBuilder.cs` | Static builder that assembles a 5-section session system prompt from character profiles and a `GameDefinition` |
| `GameDefinitionTests.cs` (test) | Unit tests for `GameDefinition` constructor, `LoadFrom` YAML parsing (valid, invalid, missing keys, null values, extra keys), and `PinderDefaults` |
| `SessionSystemPromptBuilderTests.cs` (test) | Unit tests for `SessionSystemPromptBuilder.Build` output structure, section ordering, null handling, and defaults fallback |
| `Issue543_SessionSystemPromptSpecTests.cs` (test) | 45 spec-driven tests covering all acceptance criteria for `GameDefinition` and `SessionSystemPromptBuilder` |

## API / Public Interface

### `PromptTemplates` (static class)

- **`DialogueOptionsInstruction`** (`const string`) — §3.2: Instructs the LLM to generate exactly 4 dialogue options tagged with stat, callback, combo, and tell bonus metadata. Includes a voice-check reminder: "Before writing each option, verify: does this sound exactly like the texting style above? If not, rewrite it."
- **`DateeResponseInstruction`** (`const string`) — §3.5: Instructs the LLM to generate an datee response with optional `[SIGNALS]` block containing TELLs and WEAKNESSes. Includes 10 explicit tell category mappings (behavior → stat) to constrain LLM output. Now embeds a fundamental resistance rule ("Below Interest 25, you are not won over…") and a `{resistance_block}` placeholder filled at runtime by `SessionDocumentBuilder`.
- **Semantic relationship narrative/resistance prompts** (`data/prompts/templates.yaml`) — All seven `InterestState` values have configured narrative and resistance keys. `Interested` covers Interest 10–15 and uses unstable-agreement resistance; `VeryIntoIt` covers Interest 16–20 and uses deliberate-approach resistance. DATEE prompt trace spans point at the selected semantic YAML key.
- **`InterestBeatInstruction`** (`const string`) — §3.8: Generates narrative beats when interest crosses a threshold.
- **`InterestBeatAbove15`** (`internal const string`) — Sub-instruction for interest rising above 15.
- **`InterestBeatBelow8`** (`internal const string`) — Sub-instruction for interest dropping below 8.
- **`InterestBeatDateSecured`** (`internal const string`) — Sub-instruction for date-secured outcome.
- **`InterestBeatUnmatched`** (`internal const string`) — Sub-instruction for unmatched outcome.
- **`DateeReactionFumble`** (`internal const string`) — Datee reaction guidance for Fumble (miss 1–2): slight coolness, barely noticeable.
- **`DateeReactionMisfire`** (`internal const string`) — Datee reaction guidance for Misfire (miss 3–5): half-step more guarded.
- **`DateeReactionTropeTrap`** (`internal const string`) — Datee reaction guidance for TropeTrap (miss 6–9): warmth drops noticeably, recognizable bad-texting archetype.
- **`DateeReactionCatastrophe`** (`internal const string`) — Datee reaction guidance for Catastrophe (miss 10+): genuine confusion or discomfort.
- **`DateeReactionLegendary`** (`internal const string`) — Datee reaction guidance for Legendary (Nat 1): maximum cringe, screenshot-worthy.

### `SessionDocumentBuilder.GetDateeReactionGuidance(FailureTier tier)` (internal)

Returns per-tier datee reaction guidance text for failure degradation. Maps each `FailureTier` value to the corresponding `PromptTemplates.DateeReaction*` constant. Returns `string.Empty` for `FailureTier.None` (success) and for any unrecognized enum value (graceful degradation, no throw).

### `SessionDocumentBuilder.GetResistanceBlock(int interest, InterestState interestState)` (internal)

Returns a resistance descriptor string for the typed relationship state supplied by the engine and formats it as `"Current interest: {interest}/25. Resistance level: {descriptor}"`. The compatibility `GetResistanceBlock(int)` wrapper resolves the canonical `InterestState` and delegates to the typed overload.

### `SessionDocumentBuilder.BuildDateePrompt(DateeContext)`

Builds the user-message content for `GetDateeResponseAsync` (§3.5). Assembles prior completed visible exchanges from `DateeContext.ConversationHistory`, the current delivered event from `DateeContext.PlayerDeliveredMessage`, typed final interest state from `DateeContext.InterestAfterState`, optional trap/shadow blocks, and the final `DateeResponseInstruction`. The relationship narrative and resistance block are selected by the typed state and annotated with their semantic YAML keys. Section order remains the active DATEE order documented in tests. When `context.DeliveryTier != FailureTier.None`, the "PLAYER'S LAST MESSAGE" heading includes the tier name and a "FAILURE CONTEXT" section is injected containing the per-tier reaction guidance from `GetDateeReactionGuidance()`. On success (`FailureTier.None`), no failure section is injected.

### Character emotional direction subsystem

Avatar and DATEE emotional planning share one role-neutral domain model,
`CharacterEmotionalDirection`, one structured-output contract,
`CharacterEmotionalDirectionContract`, one configurable primary-emotion
vocabulary, and one execution lifecycle for transport selection, retries,
journal recording, validation, and diagnostics. Role-specific compilers only
construct the character and situation context supplied to that lifecycle.
This keeps emotional semantics identical for every character without mixing
DATEE event interpretation with avatar option-generation concerns.

The shared contract validates exactly `primary_emotion`,
`intensity`, `underlying_feeling`, `interpretation`, `impulse`, `restraint`,
and `response_posture`. The configured vocabulary is loaded from
`character-emotional-primary-emotions` in
`data/prompts/emotional-reactions.yaml`; the JSON schema and parser both enforce
it. A response posture must explicitly name the selected primary emotion.
The JSON schema property definitions provide informative field descriptions
and constrain `schema_version` to `'emotional_director.v1'`. Targeted repair
templates in `data/prompts/emotional-reactions.yaml` guide retry recovery for
specific contract rejections such as `response_posture_omits_primary_emotion`,
`unsupported_primary_emotion`, and `drafted_chat_reply`.

The DATEE path compiles `DateeContext.EmotionalTurnEvent`, then runs emotional
direction after the delivered player message and before DATEE performance. The
avatar path runs before dialogue-option generation and uses the complete avatar
system prompt plus visible conversation history. Both preserve annotation source files and
keys in private request metadata and emit a sanitized terminal diagnostic when
contract retries are exhausted.

The production `PinderLlmAdapter.GetDateeResponseAsync(DateeContext, history,
ct)` boundary now requires `DateeContext.EmotionalTurnEvent`, runs this private
director exactly once before DATEE performance, and fails closed before any
visible DATEE call if the event is missing or the director fails/cancels. The
validated direction is operation-local and reused by the existing DATEE
performance retry loop. The visible history result still contains only the
delivered player message and the accepted DATEE raw response. Runtime transcript
values in the private director packet are JSON string literal encoded in code,
with trace spans covering the encoded values. The performance attempt rejects
the sprint-owned private direction header or any of its seven line-leading field
labels as `private_direction_leak` before response parsing/history construction;
the existing semantic retry may recover, and diagnostics never include leaked
content. Matching is ordinal case-insensitive and strips only common leading
Unicode punctuation, symbol, and whitespace decoration while preserving explicit
numbered-list handling, exact field prefixes, and the header boundary.
`EmotionalReactionPromptCatalog` also validates
the exact protected header and seven label-placeholder lines against the editable
performance template at startup, preventing config edits from weakening this
guard while leaving reusable prose in YAML.

Operational diagnostics for the private DATEE path use the existing
`OperationalDiagnosticEvent` sink. Core emits sanitized start, terminal, and
contract-rejection events with distinct `datee_private_phase` hints (`director`
or `performance`), attempt counts, elapsed milliseconds, token deltas when the
transport implements `ITokenUsageProvider`, and prompt source/key identifiers
such as `emotional-reaction-director`,
`emotional-reaction-performance-direction`, and YAML source paths. These events
must not include compiled private packets, director field prose, performance
prompts, provider response bodies, visible transcript prose, or raw exception
objects for the private DATEE phases. Core does not persist these diagnostics or
define log retention/access policy; hosts that attach an `OnDiagnostic` sink own
storage, retention, operator access, and any export controls for the received
sanitized metadata.

Token accounting remains on the existing `ITokenUsageProvider` contract.
Text-transform transport decorators forward usage snapshots from their inner
transport. Anthropic's optional improvement pass reports its successful response
through a narrow callback to the owning `AnthropicTransport`, so the same session
total includes both draft and improvement calls without a second telemetry store.

### `SessionDocumentBuilder.BuildDateePerformancePromptEx(DateeContext, CharacterEmotionalDirection)` (internal)

Internal DATEE performance prompt builder used only by the production adapter
path. It preserves the ordinary public `BuildDateePrompt/BuildDateePromptEx`
shape for prompt callers, inserts the YAML-backed
`emotional-reaction-performance-direction` block immediately before the final
`datee-response-instruction`, and traces wrapper prose to
`data/prompts/emotional-reactions.yaml` while tracing the seven direction field
values to stable runtime director keys.

### `SessionDocumentBuilder.BuildDialogueOptionsPrompt(DialogueContext)`

Builds the user message content for dialogue option generation. When `context.DateePrompt` is non-empty, prepends an `DATEE PROFILE` section (labelled "NOT who you are") before the conversation history. When `context.PlayerTextingStyle` is non-empty, injects a `YOUR TEXTING STYLE — follow this exactly, no deviations:` block immediately before the `YOUR TASK` heading. If `PlayerTextingStyle` is empty, the block is omitted entirely. When `context.ActiveTell` is non-null, injects a `TELL DETECTED` directive immediately after the conversation history, instructing the LLM that one option using the tell's stat should explicitly capitalize on the vulnerability. This provides the LLM with datee context without placing the datee's identity in the system prompt, reinforces the player character's unique texting voice, and creates mechanical follow-through for tells.

### `IStatefulLlmAdapter` (Pinder.Core.Interfaces)

```csharp
public interface IStatefulLlmAdapter : ILlmAdapter
{
    Task<StatefulDateeResult> GetDateeResponseAsync(
        DateeContext context,
        IReadOnlyList<ConversationMessage> history,
        CancellationToken cancellationToken = default);

    Task<string> GetSteeringQuestionAsync(SteeringContext context, CancellationToken ct = default);
    Task<string> GetHorninessQuestionAsync(HorninessQuestionContext context, CancellationToken ct = default);
    Task<string> GetSuccessImprovementAsync(SuccessImprovementContext context, CancellationToken ct = default);
}
```

- Extends `ILlmAdapter` - implementors must also satisfy the base adapter methods.
- The DATEE history is owned by `GameSessionState` and supplied on each call.
- Implementations must not retain DATEE-session state across calls.
- Lives in `Pinder.Core` (zero NuGet dependencies - pure interface). Implemented by `PinderLlmAdapter`; `NullLlmAdapter` implements the test/fallback shape.

### `ILlmAdapter.ApplyFailureCorruptionAsync` (Pinder.Core.Interfaces)

```csharp
Task<string> ApplyFailureCorruptionAsync(
    string message,
    string instruction,
    StatType stat,
    FailureTier tier,
    string? archetypeDirective = null,
    CancellationToken ct = default);
```

Applies a config-driven failure corruption instruction prompt to a message when a player rolls a failure (Fumble, Misfire, TropeTrap, Catastrophe, Legendary).

- **Arguments:**
  - `message`: The original intended message text.
  - `instruction`: The stat-specific failure instruction prompt retrieved from config (via `HorninessEngine.GetStatFailureInstruction(...)`).
  - `stat`: The stat type on which the check failed (e.g. `RIZZ`, `WIT`, etc.).
  - `tier`: The severity level of the failure (from `FailureTier.Fumble` to `FailureTier.Legendary`).
  - `archetypeDirective`: Optional archetype directive to preserve character voice traits during the rewrite.
  - `ct`: A cancellation token that must be propagated cleanly.
- **Implementations:**
  - **`PinderLlmAdapter`**: Constructs a system prompt establishing the comedy RPG setting and character voice, formats the user message with the failure instruction and archetype directive (if present), and calls the underlying transport layer.
  - **`NullLlmAdapter`**: A no-op fallback implementation that returns the original message text unmodified, which in turn causes the delivery stage to fall back immediately to static deterministic degradation.
- **Robust Fallback Mechanics:**
  - If the adapter invocation throws an exception (excluding `OperationCanceledException` under cancellation), returns an empty or unmodified string, or detects LLM refusal (e.g., matching phrases like `"I can't"`, `"I cannot"`, `"inappropriate"`, or `"I'd be happy to help"`), it fires an `OnOverlayDegraded` event to capture the degradation state.
  - In `DeliveryStage.ExecuteAsync`, if the asynchronous operation does not produce a valid mutated message, it falls back gracefully to the deterministic static overlay rendering via `DeliveryOverlay.Apply(...)`.

### `ConversationSession` (Legacy Anthropic Helper)

Accumulates user/assistant messages for the legacy Anthropic Messages API adapter path. It is not the current DATEE session contract. Current production session continuity is engine-owned: `GameSessionState.DateeHistory` remains the semantic snapshot/resimulation ledger, and `GameSessionState.History` remains the canonical visible transcript. `DateeContext.ConversationHistory` contains prior completed visible exchanges; the current delivered player line is supplied separately as `PlayerDeliveredMessage`.

- **`SystemBlocks`** (`ContentBlock[]`) — Single-element array containing the system prompt as a `ContentBlock` with `Type = "text"`, `Text = systemPrompt`, and `CacheControl = { Type = "ephemeral" }`. Set at construction, immutable thereafter.
- **`Messages`** (`IReadOnlyList<Message>`) — Read-only view of all accumulated messages in append order.
- **`ConversationSession(string systemPrompt)`** — Constructor. Wraps `systemPrompt` in a `ContentBlock` with ephemeral cache control. Throws `ArgumentException` if `systemPrompt` is null, empty, or whitespace.
- **`AppendUser(string content)`** — Appends a `Message` with `Role = "user"`. Throws `ArgumentNullException` if `content` is null. Empty string is allowed.
- **`AppendAssistant(string content)`** — Appends a `Message` with `Role = "assistant"`. Throws `ArgumentNullException` if `content` is null. Empty string is allowed.
- **`BuildRequest(string model, int maxTokens, double temperature)`** — Returns a `MessagesRequest` with `System = SystemBlocks`, `Messages` as a snapshot array (copy of internal list), and the provided model/maxTokens/temperature. Subsequent appends do not affect previously returned requests.

### `AnthropicLlmAdapter` — Stateful Conversation Members

- **`HasActiveConversation`** (`bool`, read-only) — `true` when a `ConversationSession` is active; `false` otherwise. When `true`, all four `ILlmAdapter` methods route through the accumulated session.
- **`StartConversation(string systemPrompt)`** — Creates a new `ConversationSession` and stores it in the internal `_session` field. Replaces any existing session (no error). Throws `ArgumentException` if `systemPrompt` is null or whitespace. Implements `IStatefulLlmAdapter.StartConversation`.

Historical note (#1332): the preceding Anthropic-specific member list describes the older adapter-retained conversation path. It is not the active `IStatefulLlmAdapter` contract. New work must use engine-owned history passed into `PinderLlmAdapter` instead of resurrecting `StartConversation` / `HasActiveConversation`.

### `AnthropicOptions` (public sealed class)
- `string? DebugDirectory` — (New in #534) When set, the adapter writes raw request/response JSON payloads per LLM call and a rolling `session-summary.json` containing token usage metrics.

### `GameDefinition` (public sealed class)

Data carrier for game-level creative direction and rule constants. Production composition parses it from YAML and passes/registers it explicitly. All properties are non-null strings set at construction.

- **`Name`** (`string`) — Game name (e.g. "Pinder").
- **`GameMasterPrompt`** (`string`) — The complete, pre-assembled Game Master base system prompt.
- **`PlayerAvatarRoleDescription`** (`string`) — Player character role description.
- **`DateeRoleDescription`** (`string`) — Datee character role description.
- **`GlobalDcBias`** (`int`) — Global DC bias applied to all rolls.
- **`GameDefinition(...)`** — Constructor. Throws `ArgumentNullException` if required arguments are null.
- **`LoadFrom(string yamlContent)`** (`static`) — Parses a YAML string into a `GameDefinition`. Maps keys: `name`, `game_master_prompt`, `player_avatar_role_description`, `datee_role_description`, `global_dc_bias`. Extra YAML keys are ignored. Throws `ArgumentNullException` if `yamlContent` is null. Throws `FormatException` if YAML is unparseable, a required key is missing, or a key has a null value.
- **`PinderDefaults`** (`static GameDefinition`) — Test/tool convenience with Pinder-specific creative direction. Production playtests should use the YAML-loaded definition.

### `SessionSystemPromptBuilder` (public static class)

Assembles a session-level system prompt from character profiles and game definition data.

```csharp
public static string Build(
    string playerPrompt,
    string dateePrompt,
    GameDefinition? gameDef = null);
```

- Returns a single string with 5 sections delimited by `== SECTION NAME ==` headers:
  1. **== GAME MASTER PROMPT ==** — from `gameDef.GameMasterPrompt`
  3. **== PLAYER CHARACTER ==** — `playerPrompt` verbatim
  4. **== DATEE CHARACTER ==** — `dateePrompt` verbatim
- Each section body is trimmed of trailing whitespace via `TrimEnd()`.
- When `gameDef` is `null`, `GameDefinition.PinderDefaults` is used.
- Throws `ArgumentNullException` if `playerPrompt` or `dateePrompt` is null. Empty strings are allowed.

### Tell Category Mappings (in `DateeResponseInstruction`)

The prompt includes an explicit "ONLY" constraint with 10 behavior-to-stat mappings:

| Datee Behavior | Tell Stat(s) |
|---|---|
| Compliments player | HONESTY |
| Asks personal question | HONESTY or SELF_AWARENESS |
| Makes joke | WIT or CHAOS |
| Shares vulnerability | HONESTY |
| Pulls back/guards | SELF_AWARENESS |
| Tests/challenges | WIT or CHAOS |
| Sends short reply | CHARM or CHAOS |
| Flirts | RIZZ or CHARM |
| Changes subject | CHAOS |
| Goes quiet/silent | SELF_AWARENESS |

## Architecture Notes

- **Template-based prompting:** Prompt prose is loaded from `data/prompts/*.yaml` through `PromptCatalog` and uses `{placeholder}` tokens. `SessionDocumentBuilder` fills these at runtime with session-specific data (player name, datee name, interest levels, etc.).
- **Structured output:** Templates enforce strict output formats (e.g., `[SIGNALS]`, `[STAT: X]` tags) so responses can be parsed deterministically. (Note: The `[RESPONSE]` wrapper for main messages was removed, and the LLM now outputs the message text directly.)
- **Tell category constraint:** The `DateeResponseInstruction` explicitly lists which datee behaviors map to which stat categories, preventing the LLM from inventing arbitrary tell associations. This was added to close a gap where the LLM was guessing which tells to produce.
- **Character-voiced interest beats:** `GetInterestChangeBeatAsync` injects the datee's system prompt as a system block (via `CacheBlockBuilder.BuildDateeOnlySystemBlocks`) when `InterestChangeContext.DateePrompt` is non-empty. This ensures §3.8 interest change beats are generated in the datee's voice rather than generic narration. When no prompt is provided, no system blocks are sent (backward-compatible).
- **Voice bleed prevention (dialogue options):** `GetDialogueOptionsAsync` places only the player's prompt in the system blocks (via `CacheBlockBuilder.BuildPlayerOnlySystemBlocks`). The datee's prompt is moved to the user message as an `DATEE PROFILE` informational section built by `SessionDocumentBuilder`. This prevents the LLM from adopting the datee's register/voice when generating dialogue options for the player. The datee profile is explicitly labelled "NOT who you are" to reinforce the boundary.
- **Voice distinctness (texting style reinforcement):** `SessionDocumentBuilder.BuildDialogueOptionsPrompt` injects a `YOUR TEXTING STYLE` constraint block immediately before `YOUR TASK` when `DialogueContext.PlayerTextingStyle` is non-empty. The texting style fragment originates from `CharacterProfile.TextingStyleFragment`, threaded through `DialogueContext.PlayerTextingStyle` via `GameSession.StartTurnAsync`. `PromptTemplates.DialogueOptionsInstruction` includes a voice-check reminder that tells the LLM to verify each option matches the style. This layers on top of the voice bleed fix (#487) to ensure generated options sound like the player character.
- **Active tell exploitation:** When an datee reveals a vulnerability (via a Tell), the tell is retained in `GameSession` and passed into `DialogueContext.ActiveTell`. `SessionDocumentBuilder.BuildDialogueOptionsPrompt` uses this to inject a `TELL DETECTED` directive demanding that one of the generated options explicitly capitalize on the vulnerability, creating mechanical follow-through on the "read."
- **Datee resistance framing:** `DateeResponseInstruction` now contains a fundamental resistance rule stating the datee is not won over below Interest 25. A `{resistance_block}` placeholder is filled at runtime by `GetResistanceBlock()`, which selects from six archetype-independent resistance postures (Active disengagement → Skeptical interest → Unstable agreement → Deliberate approach → Almost convinced → Resistance dissolved). The resistance system is purely prompt-engineering — no game mechanics or DTOs were changed. It complements the existing `GetInterestBehaviourBlock()` (which describes engagement behavior like reply speed/length) by framing the datee's *opposition posture*.
- **Failure degradation legibility:** When a player's roll fails, `DateeContext.DeliveryTier` (set from `rollResult.Tier` in `GameSession.ResolveTurnAsync`) carries the `FailureTier` enum value into `BuildDateePrompt`. The method injects a "FAILURE CONTEXT" section with tier-specific guidance from `GetDateeReactionGuidance()`, so the datee LLM reacts proportionally to how badly the message was corrupted — from slight coolness (Fumble) to secondhand embarrassment (Legendary). Guidance text avoids fourth-wall-breaking language (no "failed", "rolled", etc.). On success (`FailureTier.None`), no failure section is injected. Note: the spec proposed a `PromptTemplates.GetDateeFailureGuidance()` method and "DELIVERY NOTE" section name; the implementation places the method on `SessionDocumentBuilder.GetDateeReactionGuidance()` and uses the section name "FAILURE CONTEXT".
- **Historical stateful conversation mode:** The retired `AnthropicLlmAdapter` path used `StartConversation(systemPrompt)`, `HasActiveConversation`, and an adapter-retained `ConversationSession` to resend accumulated messages to the stateless Anthropic Messages API. This describes historical behavior only. The active `PinderLlmAdapter` contract retains no provider conversation: `GameSessionState` owns history and supplies the relevant context on every adapter call.
- **Session system prompt assembly:** Historical #542 wiring passed a combined `SessionSystemPromptBuilder.Build` result to `IStatefulLlmAdapter.StartConversation()`. The active builder instead produces role-specific prompts with `BuildPlayerAvatar` and `BuildDatee`; these are compiled per operation by `PinderLlmAdapter`, with engine-owned context/history supplied as call inputs. Production composition still loads `GameDefinition` from YAML via `LoadFrom()` and passes/registers that resolver explicitly; `PinderDefaults` is reserved for tests and tooling. `Pinder.Core` remains dependency-free.
- **Current session ownership (#1332/#1348):** `GameSessionState.DateeHistory` carries semantic DATEE messages for snapshot/resimulation, while `GameSessionState.History` remains the canonical gameplay transcript. `PinderLlmAdapter.GetDateeResponseAsync(DateeContext, history, ct)` receives the semantic history but does not prepend it when `DateeContext` already contains the rendered transcript. For DATEE prompts, `DateeContext.ConversationHistory` is prior completed visible exchanges only, and `PlayerDeliveredMessage` is the current event. The player/DATEE visible-history pair is appended together only after DATEE generation succeeds. `SessionSystemPromptBuilder` produces role-specific system prompts: `BuildPlayerAvatar` for avatar operations and `BuildDatee` for DATEE operations.
- **Debug payload logging:** `AnthropicOptions` exposes an optional `DebugDirectory`. When set, `AnthropicLlmAdapter` writes exactly what is sent to and received from the Anthropic API to disk (`turn-XX-callType-request.json` and `-response.json`). It also accumulates token and cache performance metrics via thread-safe tracking, outputting a rolling `session-summary.json` file. This allows inspection of raw LLM interaction and prompt caching behavior without modifying game logic.
- **Config-driven failure corruption overlays:** When standard option rolls fail, instead of immediately applying the deterministic static `DeliveryOverlay.Apply` rules, the system queries the config for stat-specific failure instructions. If found, it routes them to `ILlmAdapter.ApplyFailureCorruptionAsync` for a highly creative and context-aware failure rewrite, preserving the player's archetype voice. It guarantees robustness by falling back to the static deterministic overlay whenever the LLM fails, cancels, gets empty output, or issues standard refusals.
- **Provider abstraction:** The Anthropic-specific code is isolated in its own subdirectory. The adapter pattern allows swapping LLM providers without changing prompt templates or game logic.

## Change Log
| Date | Issue | Summary |
|------|-------|---------|
| 2026-04-03 | #311 | Initial creation — Added 10 tell category mappings to `DateeResponseInstruction` with "ONLY" constraint, preventing LLM from guessing tell stats. Mappings cover all 6 stats (CHARM, RIZZ, HONESTY, CHAOS, WIT, SELF_AWARENESS) across 10 datee behaviors. |
| 2026-04-03 | #352 | `AnthropicLlmAdapter.GetInterestChangeBeatAsync` now includes datee system prompt as a system block when `InterestChangeContext.DateePrompt` is non-empty, so §3.8 interest change beats are generated in the datee's character voice. Uses `CacheBlockBuilder.BuildDateeOnlySystemBlocks`. Tests in `InterestChangeBeatVoiceTests.cs`. |
| 2026-04-04 | #487 | Fix voice bleed — moved datee prompt out of system blocks into user message for dialogue option generation. `AnthropicLlmAdapter.GetDialogueOptionsAsync` now uses `CacheBlockBuilder.BuildPlayerOnlySystemBlocks` (player only). `SessionDocumentBuilder.BuildDialogueOptionsPrompt` prepends `DATEE PROFILE` section in user content when datee prompt is present. |
| 2026-04-04 | #489 | Voice distinctness — `CharacterProfile` gains `TextingStyleFragment` property (optional, default `""`). `DialogueContext` gains `PlayerTextingStyle` property (optional, default `""`). `SessionDocumentBuilder.BuildDialogueOptionsPrompt` injects `YOUR TEXTING STYLE` block before `YOUR TASK` when style is non-empty. `PromptTemplates.DialogueOptionsInstruction` appended with voice-check reminder. `GameSession.StartTurnAsync` wires player's texting style into `DialogueContext`. Session-runner loaders (`CharacterLoader`, `CharacterDefinitionLoader`) extract/join texting style fragments for `CharacterProfile`. |
| 2026-04-04 | #490 | Datee resistance — `DateeResponseInstruction` now embeds a fundamental resistance rule and `{resistance_block}` placeholder. Six `internal const` resistance descriptors added to `PromptTemplates` (bands: 0–4, 5–9, 10–14, 15–20, 21–24, 25). `SessionDocumentBuilder.GetResistanceBlock(int)` selects the appropriate descriptor. `BuildDateePrompt` fills the placeholder at runtime. Note: spec proposed `GetResistanceDescriptor` on `PromptTemplates` and a separate `DateeResistanceRule` constant; implementation places logic on `SessionDocumentBuilder.GetResistanceBlock` and inlines the rule into `DateeResponseInstruction`. Band names also differ from spec (e.g. "Unstable agreement" vs "Warmth with visible holdback"). Tests in `Issue490_ResistanceSpec_Tests.cs` (25 tests). |
| 2026-04-04 | #491 | Success delivery rewrite — `SuccessDeliveryInstruction` revised to use margin tiers aligned with `SuccessScale` (1–4 clean, 5–9 strong, 10+ critical/Nat 20), replacing old misaligned bands (1–5, 6–10). Removed "add a small flourish" language. Strong tier now sharpens existing phrasing (allows ONE added word/phrase for precision) but explicitly prohibits new ideas. Added counterpart rule: every idea in delivered must map to intended. Tests in `Issue491_SuccessDeliveryTests.cs` and additional assertions in `SessionDocumentBuilderSpecTests.cs`. |
| 2026-04-04 | #493 | Failure degradation legibility — `DateeContext` gains `DeliveryTier` property (`FailureTier`, default `None`). `GameSession.ResolveTurnAsync` passes `rollResult.Tier` to `DateeContext`. Five `DateeReaction*` constants added to `PromptTemplates` (Fumble/Misfire/TropeTrap/Catastrophe/Legendary). `SessionDocumentBuilder.GetDateeReactionGuidance(FailureTier)` maps tiers to guidance. `BuildDateePrompt` injects "FAILURE CONTEXT" section for non-None tiers. Spec divergences: method placed on `SessionDocumentBuilder` (not `PromptTemplates`), section named "FAILURE CONTEXT" (not "DELIVERY NOTE"), constants named `DateeReaction*` (not `DateeFailureGuidance` / `Datee*Guidance`). Tests in `Issue493_FailureDegradationTests.cs`, `Issue493_FailureDegradationSpecTests.cs`, `Issue493_FailureDegradationCoreTests.cs`. |
| 2026-04-05 | #545 | Game definition YAML content validation — Added `GameDefinitionYamlContentTests.cs` (30 tests) in `Pinder.Rules.Tests`. Tests validate that `data/game-definition.yaml` exists, is valid YAML (no tabs, no BOM, all scalar strings, exactly 7 keys), and contains Pinder-specific creative content in all sections (vision, world_description, player_role_description, datee_role_description, meta_contract, writing_rules). Each section is checked for required domain concepts (e.g. shadow growth, d20 rolls, 4 dialogue options, resistance, ENGINE blocks, asterisk prohibition). The YAML file itself lives outside the repo at `/root/.openclaw/agents-extra/pinder/data/game-definition.yaml` and is consumed by `GameDefinition.LoadFrom()` (#543). No C# production code changed. |
| 2026-04-05 | #541 | Stateful conversation mode — Added `ConversationSession` class (`Pinder.LlmAdapters`) that accumulates user/assistant `Message` objects and builds `MessagesRequest` with cached system blocks (ephemeral `CacheControl`) + full message history snapshot. `AnthropicLlmAdapter` gains `StartConversation(string systemPrompt)` and `HasActiveConversation` property. When active, all active `ILlmAdapter` methods append to and read from the session instead of building fresh single-message requests. Stateless fallback is preserved when no session is active. `ILlmAdapter` interface unchanged. Tests: `ConversationSessionTests.cs` (16), `AnthropicLlmAdapterStatefulTests.cs` (12), `Issue541_StatefulConversationTests.cs`, `Issue541_AdditionalTests.cs`. |
| 2026-04-05 | #542 | `IStatefulLlmAdapter` interface + GameSession wiring — New `IStatefulLlmAdapter` interface in `Pinder.Core.Interfaces` formalizes `StartConversation(string)` and `HasActiveConversation` as a sub-interface of `ILlmAdapter`. `AnthropicLlmAdapter` class declaration changed from `ILlmAdapter` to `IStatefulLlmAdapter`. `GameSession` 6-parameter constructor now checks `_llm is IStatefulLlmAdapter` and, if true, builds a system prompt from both character profiles (player + `\n\n---\n\n` + datee) and calls `StartConversation`. `NullLlmAdapter` unchanged — stateless path preserved. Tests: `Issue542_StatefulSession_TestEngineerTests.cs`. |
| 2026-04-05 | #543 | Session system prompt builder — Added `GameDefinition` sealed class (7 read-only string properties, `LoadFrom(yamlContent)` YAML parser, `PinderDefaults` test/tool default) and `SessionSystemPromptBuilder` static class (`Build` method producing 5-section `== SECTION NAME ==` delimited prompt from character profiles + game definition). `YamlDotNet 16.3.0` added to `Pinder.LlmAdapters.csproj`. Tests: `GameDefinitionTests.cs`, `SessionSystemPromptBuilderTests.cs`, `Issue543_SessionSystemPromptSpecTests.cs` (45 spec-driven tests). |
| 2026-04-06 | #572 | Bug fix — Removed the `[RESPONSE]` tag requirement from `PromptTemplates.DateeResponseInstruction` so the LLM outputs message text directly. Updated `AnthropicLlmAdapter.ParseDateeResponse` to extract text before `[SIGNALS]` and gracefully strip legacy `[RESPONSE]` tags or quotes if generated. |
| 2026-04-06 | #530 | Scaled delivery quality infinitely with roll margin. Added `Critical (10-14)` and `Exceptional (15+)` tiers to `SuccessDeliveryInstruction`. Injected exact `{beat_dc_by}` margin and Nat 20 status into the prompt via `SessionDocumentBuilder.BuildDeliveryPrompt()` to instruct the LLM on exactly how well the player rolled. |
| 2026-04-06 | #534 | Added `--debug` flag support to `session-runner` via `AnthropicOptions.DebugDirectory`. `AnthropicLlmAdapter` now intercepts and writes `turn-{turn:D2}-{callType}-request.json` and `response.json` for every API call, plus a `session-summary.json` tracking cumulative input/output and cache tokens. Validated thread-safe stat tracking with 100-thread concurrent test. |
| 2026-04-07 | #647 | Active tell options — `DialogueContext` gains `ActiveTell` property. `GameSession.StartTurnAsync` passes `_activeTell` into context. `SessionDocumentBuilder.BuildDialogueOptionsPrompt` injects a `TELL DETECTED` directive if an active tell exists, forcing the LLM to craft one option that exploits the revealed vulnerability. |
| 2026-07-05 | #1311 | Restored config-driven LLM failure corruption prompts by implementing `ApplyFailureCorruptionAsync` on `ILlmAdapter`/`PinderLlmAdapter` and wiring it into `DeliveryStage.ExecuteAsync` with robust fallback to `DeliveryOverlay.Apply` and exception handling properties. |
| 2026-07-25 | #1335 | Supersedes the #490/#544 raw numeric relationship prompt split for active DATEE prompt selection. `DateeContext.InterestAfterState` selects semantic narrative/resistance YAML keys for all seven `InterestState` values. Interest 15 remains `Interested` and uses lower narrative plus unstable-agreement resistance; Interest 16 begins `VeryIntoIt` and uses upper narrative plus deliberate-approach resistance. |
