# LLM Adapters

## Overview
The LLM Adapters module (`Pinder.LlmAdapters`) provides prompt templates and API clients for integrating large language models into Pinder's conversation game. It defines the structured instruction templates that guide LLM output (dialogue options, opponent responses, interest beats) and handles communication with external LLM providers.

## Key Components

| File | Description |
|------|-------------|
| `PromptTemplates.cs` | Static instruction templates (§3.2–3.8) with `{placeholder}` tokens for dynamic content; includes resistance band descriptors |
| `SessionDocumentBuilder.cs` | Fills placeholder tokens in prompt templates with session-specific data; injects opponent profile, texting style, and resistance stance into user messages |
| `Anthropic/AnthropicClient.cs` | HTTP client for the Anthropic Messages API |
| `Anthropic/AnthropicLlmAdapter.cs` | Adapter implementing the LLM interface using Anthropic's API |
| `Anthropic/AnthropicOptions.cs` | Configuration options for the Anthropic client |
| `Anthropic/CacheBlockBuilder.cs` | Builds cache-control blocks for Anthropic prompt caching |
| `Anthropic/AnthropicApiException.cs` | Exception type for Anthropic API errors |
| `Anthropic/Dto/MessagesRequest.cs` | Request DTO for the Anthropic Messages API |
| `Anthropic/Dto/MessagesResponse.cs` | Response DTO for the Anthropic Messages API |
| `Anthropic/Dto/ContentBlock.cs` | Content block DTO for Anthropic message payloads |
| `src/Pinder.Core/Interfaces/IStatefulLlmAdapter.cs` | Interface extending `ILlmAdapter` with `StartConversation(string)` and `HasActiveConversation` for stateful conversation mode |
| `ConversationSession.cs` | Accumulates user/assistant messages for stateful multi-turn conversations; builds `MessagesRequest` with cached system blocks + full history |
| `GameDefinitionYamlContentTests.cs` (test) | 30 content-validation tests ensuring `game-definition.yaml` has correct structure and Pinder-specific creative content |
| `ConversationSessionTests.cs` (test) | 16 unit tests for `ConversationSession` construction, append, BuildRequest, and edge cases |
| `AnthropicLlmAdapterStatefulTests.cs` (test) | 12 tests for stateful adapter behavior across all 4 `ILlmAdapter` methods |
| `Issue541_StatefulConversationTests.cs` (test) | Integration tests for stateful conversation mode — multi-turn accumulation, error recovery, stateless fallback |
| `Issue541_AdditionalTests.cs` (test) | Additional coverage for snapshot isolation, role correctness, message ordering, system block caching, and API failure resilience |
| `Issue542_StatefulSession_TestEngineerTests.cs` (test) | Spec-driven tests for `IStatefulLlmAdapter` interface shape, `GameSession` constructor stateful detection, system prompt format, and backward compatibility |
| `GameDefinition.cs` | Sealed data carrier for game-level creative direction (7 string properties); includes `LoadFrom(yamlContent)` YAML parser and `PinderDefaults` hardcoded fallback |
| `SessionSystemPromptBuilder.cs` | Static builder that assembles a 5-section session system prompt from character profiles and a `GameDefinition` |
| `GameDefinitionTests.cs` (test) | Unit tests for `GameDefinition` constructor, `LoadFrom` YAML parsing (valid, invalid, missing keys, null values, extra keys), and `PinderDefaults` |
| `SessionSystemPromptBuilderTests.cs` (test) | Unit tests for `SessionSystemPromptBuilder.Build` output structure, section ordering, null handling, and defaults fallback |
| `Issue543_SessionSystemPromptSpecTests.cs` (test) | 45 spec-driven tests covering all acceptance criteria for `GameDefinition` and `SessionSystemPromptBuilder` |

## API / Public Interface

### `PromptTemplates` (static class)

- **`DialogueOptionsInstruction`** (`const string`) — §3.2: Instructs the LLM to generate exactly 4 dialogue options tagged with stat, callback, combo, and tell bonus metadata. Includes a voice-check reminder: "Before writing each option, verify: does this sound exactly like the texting style above? If not, rewrite it."
- **`OpponentResponseInstruction`** (`const string`) — §3.5: Instructs the LLM to generate an opponent response with optional `[SIGNALS]` block containing TELLs and WEAKNESSes. Includes 10 explicit tell category mappings (behavior → stat) to constrain LLM output. Now embeds a fundamental resistance rule ("Below Interest 25, you are not won over…") and a `{resistance_block}` placeholder filled at runtime by `SessionDocumentBuilder`.
- **`ResistanceActiveDisengagement`** (`internal const string`) — Interest 0–4: active disengagement descriptor.
- **`ResistanceSkepticalInterest`** (`internal const string`) — Interest 5–9: skeptical interest descriptor.
- **`ResistanceUnstableAgreement`** (`internal const string`) — Interest 10–14: unstable agreement descriptor.
- **`ResistanceDeliberateApproach`** (`internal const string`) — Interest 15–20: deliberate approach descriptor.
- **`ResistanceAlmostConvinced`** (`internal const string`) — Interest 21–24: almost convinced descriptor.
- **`ResistanceDissolved`** (`internal const string`) — Interest 25: resistance dissolved descriptor.
- **`SuccessDeliveryInstruction`** (`const string`) — §3.3: Delivers the player's intended message on a successful roll. Defines multiple margin-based tiers: Clean (1–4), Strong (5–9), Critical (10–14), Exceptional (15+), and Critical/Nat 20. Contains an explicit counterpart rule: every idea in the delivered version must have a counterpart in the intended version. Additions sharpen, not expand. Retains `{player_name}` and `{beat_dc_by}` placeholders for runtime substitution by `SessionDocumentBuilder.BuildDeliveryPrompt()`.
- **`InterestBeatInstruction`** (`const string`) — §3.8: Generates narrative beats when interest crosses a threshold.
- **`InterestBeatAbove15`** (`internal const string`) — Sub-instruction for interest rising above 15.
- **`InterestBeatBelow8`** (`internal const string`) — Sub-instruction for interest dropping below 8.
- **`InterestBeatDateSecured`** (`internal const string`) — Sub-instruction for date-secured outcome.
- **`InterestBeatUnmatched`** (`internal const string`) — Sub-instruction for unmatched outcome.
- **`OpponentReactionFumble`** (`internal const string`) — Opponent reaction guidance for Fumble (miss 1–2): slight coolness, barely noticeable.
- **`OpponentReactionMisfire`** (`internal const string`) — Opponent reaction guidance for Misfire (miss 3–5): half-step more guarded.
- **`OpponentReactionTropeTrap`** (`internal const string`) — Opponent reaction guidance for TropeTrap (miss 6–9): warmth drops noticeably, recognizable bad-texting archetype.
- **`OpponentReactionCatastrophe`** (`internal const string`) — Opponent reaction guidance for Catastrophe (miss 10+): genuine confusion or discomfort.
- **`OpponentReactionLegendary`** (`internal const string`) — Opponent reaction guidance for Legendary (Nat 1): maximum cringe, screenshot-worthy.

### `SessionDocumentBuilder.GetOpponentReactionGuidance(FailureTier tier)` (internal)

Returns per-tier opponent reaction guidance text for failure degradation. Maps each `FailureTier` value to the corresponding `PromptTemplates.OpponentReaction*` constant. Returns `string.Empty` for `FailureTier.None` (success) and for any unrecognized enum value (graceful degradation, no throw).

### `SessionDocumentBuilder.GetResistanceBlock(int interest)` (internal)

Returns a resistance descriptor string for the given interest level. Selects one of six `PromptTemplates.Resistance*` constants based on interest bands (0–4, 5–9, 10–14, 15–20, 21–24, 25) and formats it as `"Current interest: {interest}/25. Resistance level: {descriptor}"`. Values below 0 are treated as 0; values above 25 are treated as 25.

### `SessionDocumentBuilder.BuildOpponentPrompt(OpponentContext)`

Builds the user-message content for `GetOpponentResponseAsync` (§3.5). Assembles conversation history, interest state, optional trap/shadow blocks, and the final `OpponentResponseInstruction`. The resistance block is injected into `OpponentResponseInstruction` by replacing the `{resistance_block}` placeholder with the output of `GetResistanceBlock(context.InterestAfter)`. Section order: CONVERSATION HISTORY → PLAYER'S LAST MESSAGE (with optional tier label) → FAILURE CONTEXT (conditional, on non-None `DeliveryTier`) → INTEREST CHANGE → RESPONSE TIMING → CURRENT INTEREST STATE → ACTIVE TRAP INSTRUCTIONS (conditional) → SHADOW STATE (conditional) → OpponentResponseInstruction (with embedded FUNDAMENTAL RULE + resistance block). When `context.DeliveryTier != FailureTier.None`, the "PLAYER'S LAST MESSAGE" heading includes the tier name (e.g. "delivered after a CATASTROPHE") and a "FAILURE CONTEXT" section is injected containing the per-tier reaction guidance from `GetOpponentReactionGuidance()`. On success (`FailureTier.None`), the prompt is identical to pre-#493 behavior.

### `SessionDocumentBuilder.BuildDialogueOptionsPrompt(DialogueContext)`

Builds the user message content for dialogue option generation. When `context.OpponentPrompt` is non-empty, prepends an `OPPONENT PROFILE` section (labelled "NOT who you are") before the conversation history. When `context.PlayerTextingStyle` is non-empty, injects a `YOUR TEXTING STYLE — follow this exactly, no deviations:` block immediately before the `YOUR TASK` heading. If `PlayerTextingStyle` is empty, the block is omitted entirely. This provides the LLM with opponent context without placing the opponent's identity in the system prompt, and reinforces the player character's unique texting voice.

### `IStatefulLlmAdapter` (Pinder.Core.Interfaces)

```csharp
public interface IStatefulLlmAdapter : ILlmAdapter
{
    void StartConversation(string systemPrompt);
    bool HasActiveConversation { get; }
}
```

- Extends `ILlmAdapter` — implementors must also satisfy the four `ILlmAdapter` methods.
- `StartConversation` initializes an internal conversation session. Calling again replaces the previous session (no error).
- `HasActiveConversation` returns `false` before `StartConversation`, `true` after.
- Lives in `Pinder.Core` (zero NuGet dependencies — pure interface). Implemented by `AnthropicLlmAdapter`; not implemented by `NullLlmAdapter`.

### `ConversationSession` (public sealed class)

Accumulates user/assistant messages for stateful multi-turn conversations with the Anthropic Messages API. System blocks are set once at construction; messages grow unbounded as turns are played.

- **`SystemBlocks`** (`ContentBlock[]`) — Single-element array containing the system prompt as a `ContentBlock` with `Type = "text"`, `Text = systemPrompt`, and `CacheControl = { Type = "ephemeral" }`. Set at construction, immutable thereafter.
- **`Messages`** (`IReadOnlyList<Message>`) — Read-only view of all accumulated messages in append order.
- **`ConversationSession(string systemPrompt)`** — Constructor. Wraps `systemPrompt` in a `ContentBlock` with ephemeral cache control. Throws `ArgumentException` if `systemPrompt` is null, empty, or whitespace.
- **`AppendUser(string content)`** — Appends a `Message` with `Role = "user"`. Throws `ArgumentNullException` if `content` is null. Empty string is allowed.
- **`AppendAssistant(string content)`** — Appends a `Message` with `Role = "assistant"`. Throws `ArgumentNullException` if `content` is null. Empty string is allowed.
- **`BuildRequest(string model, int maxTokens, double temperature)`** — Returns a `MessagesRequest` with `System = SystemBlocks`, `Messages` as a snapshot array (copy of internal list), and the provided model/maxTokens/temperature. Subsequent appends do not affect previously returned requests.

### `AnthropicLlmAdapter` — Stateful Conversation Members

- **`HasActiveConversation`** (`bool`, read-only) — `true` when a `ConversationSession` is active; `false` otherwise. When `true`, all four `ILlmAdapter` methods route through the accumulated session.
- **`StartConversation(string systemPrompt)`** — Creates a new `ConversationSession` and stores it in the internal `_session` field. Replaces any existing session (no error). Throws `ArgumentException` if `systemPrompt` is null or whitespace. Implements `IStatefulLlmAdapter.StartConversation`.

### `AnthropicOptions` (public sealed class)
- `string? DebugDirectory` — (New in #534) When set, the adapter writes raw request/response JSON payloads per LLM call and a rolling `session-summary.json` containing token usage metrics.

### `GameDefinition` (public sealed class)

Data carrier for game-level creative direction. Parsed from YAML or provided via hardcoded defaults. All properties are non-null strings set at construction.

- **`Name`** (`string`) — Game name (e.g. "Pinder").
- **`Vision`** (`string`) — Creative brief: what the game is, tone, goal.
- **`WorldDescription`** (`string`) — World setting description.
- **`PlayerRoleDescription`** (`string`) — Player character role description.
- **`OpponentRoleDescription`** (`string`) — Opponent character role description.
- **`MetaContract`** (`string`) — Immersion rules: never break character, [ENGINE] blocks as stage directions.
- **`WritingRules`** (`string`) — Writing style rules: texting register, brevity, character voice fidelity.
- **`GameDefinition(string name, string vision, string worldDescription, string playerRoleDescription, string opponentRoleDescription, string metaContract, string writingRules)`** — Constructor. Throws `ArgumentNullException` if any argument is null. Empty strings are allowed.
- **`LoadFrom(string yamlContent)`** (`static`) — Parses a YAML string into a `GameDefinition`. Maps keys: `name`, `vision`, `world_description`, `player_role_description`, `opponent_role_description`, `meta_contract`, `writing_rules`. Extra YAML keys are ignored. Throws `ArgumentNullException` if `yamlContent` is null. Throws `FormatException` if YAML is unparseable, a required key is missing, or a key has a null value. Block scalar newlines are preserved as-is from the YAML parser (no trimming). Uses `YamlDotNet 16.3.0` with `UnderscoredNamingConvention`.
- **`PinderDefaults`** (`static GameDefinition`) — Hardcoded fallback with all 7 fields populated with Pinder-specific creative direction: comedy dating RPG, sentient penises, absurdist world, d20 mechanics, shadow stats, texting register, immersion rules. Usable in production playtests.

### `SessionSystemPromptBuilder` (public static class)

Assembles a session-level system prompt from character profiles and game definition data.

```csharp
public static string Build(
    string playerPrompt,
    string opponentPrompt,
    GameDefinition? gameDef = null);
```

- Returns a single string with 5 sections delimited by `== SECTION NAME ==` headers:
  1. **== GAME VISION ==** — from `gameDef.Vision`
  2. **== WORLD RULES ==** — from `gameDef.WorldDescription`
  3. **== PLAYER CHARACTER ==** — `playerPrompt` verbatim
  4. **== OPPONENT CHARACTER ==** — `opponentPrompt` verbatim
  5. **== META CONTRACT ==** — `gameDef.MetaContract` + `gameDef.WritingRules` concatenated
- Each section body is trimmed of trailing whitespace via `TrimEnd()`.
- When `gameDef` is `null`, `GameDefinition.PinderDefaults` is used.
- Throws `ArgumentNullException` if `playerPrompt` or `opponentPrompt` is null. Empty strings are allowed.

### Tell Category Mappings (in `OpponentResponseInstruction`)

The prompt includes an explicit "ONLY" constraint with 10 behavior-to-stat mappings:

| Opponent Behavior | Tell Stat(s) |
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

- **Template-based prompting:** All LLM instructions are static `const string` fields with `{placeholder}` tokens. `SessionDocumentBuilder` fills these at runtime with session-specific data (player name, opponent name, interest levels, etc.).
- **Structured output:** Templates enforce strict output formats (e.g., `[SIGNALS]`, `[STAT: X]` tags) so responses can be parsed deterministically. (Note: The `[RESPONSE]` wrapper for main messages was removed, and the LLM now outputs the message text directly.)
- **Tell category constraint:** The `OpponentResponseInstruction` explicitly lists which opponent behaviors map to which stat categories, preventing the LLM from inventing arbitrary tell associations. This was added to close a gap where the LLM was guessing which tells to produce.
- **Character-voiced interest beats:** `GetInterestChangeBeatAsync` injects the opponent's system prompt as a system block (via `CacheBlockBuilder.BuildOpponentOnlySystemBlocks`) when `InterestChangeContext.OpponentPrompt` is non-empty. This ensures §3.8 interest change beats are generated in the opponent's voice rather than generic narration. When no prompt is provided, no system blocks are sent (backward-compatible).
- **Voice bleed prevention (dialogue options):** `GetDialogueOptionsAsync` places only the player's prompt in the system blocks (via `CacheBlockBuilder.BuildPlayerOnlySystemBlocks`). The opponent's prompt is moved to the user message as an `OPPONENT PROFILE` informational section built by `SessionDocumentBuilder`. This prevents the LLM from adopting the opponent's register/voice when generating dialogue options for the player. The opponent profile is explicitly labelled "NOT who you are" to reinforce the boundary.
- **Voice distinctness (texting style reinforcement):** `SessionDocumentBuilder.BuildDialogueOptionsPrompt` injects a `YOUR TEXTING STYLE` constraint block immediately before `YOUR TASK` when `DialogueContext.PlayerTextingStyle` is non-empty. The texting style fragment originates from `CharacterProfile.TextingStyleFragment`, threaded through `DialogueContext.PlayerTextingStyle` via `GameSession.StartTurnAsync`. `PromptTemplates.DialogueOptionsInstruction` includes a voice-check reminder that tells the LLM to verify each option matches the style. This layers on top of the voice bleed fix (#487) to ensure generated options sound like the player character.
- **Opponent resistance framing:** `OpponentResponseInstruction` now contains a fundamental resistance rule stating the opponent is not won over below Interest 25. A `{resistance_block}` placeholder is filled at runtime by `GetResistanceBlock()`, which selects from six archetype-independent resistance postures (Active disengagement → Skeptical interest → Unstable agreement → Deliberate approach → Almost convinced → Resistance dissolved). The resistance system is purely prompt-engineering — no game mechanics or DTOs were changed. It complements the existing `GetInterestBehaviourBlock()` (which describes engagement behavior like reply speed/length) by framing the opponent's *opposition posture*.
- **Success delivery: sharpen, not expand:** `SuccessDeliveryInstruction` (§3.3) uses margin-based tiers (1–4 clean, 5–9 strong, 10–14 critical, 15+ exceptional, Nat 20) to scale delivery quality infinitely with roll margin. The strong tier allows sharpening word choice and adding at most ONE word or phrase to make the existing sentiment more precise, but explicitly prohibits adding new sentences or ideas not in the intended message. A counterpart rule enforces that every idea in the delivered message maps back to the intended message. This scales smoothly up to exceptional deliveries at +15 margins and legendary deliveries on Nat 20.
- **Failure degradation legibility:** When a player's roll fails, `OpponentContext.DeliveryTier` (set from `rollResult.Tier` in `GameSession.ResolveTurnAsync`) carries the `FailureTier` enum value into `BuildOpponentPrompt`. The method injects a "FAILURE CONTEXT" section with tier-specific guidance from `GetOpponentReactionGuidance()`, so the opponent LLM reacts proportionally to how badly the message was corrupted — from slight coolness (Fumble) to secondhand embarrassment (Legendary). Guidance text avoids fourth-wall-breaking language (no "failed", "rolled", etc.). On success (`FailureTier.None`), no failure section is injected. Note: the spec proposed a `PromptTemplates.GetOpponentFailureGuidance()` method and "DELIVERY NOTE" section name; the implementation places the method on `SessionDocumentBuilder.GetOpponentReactionGuidance()` and uses the section name "FAILURE CONTEXT".
- **Stateful conversation mode:** `AnthropicLlmAdapter` supports an optional stateful mode activated by `StartConversation(systemPrompt)`. When active (`HasActiveConversation == true`), all four `ILlmAdapter` methods follow a shared pattern: (1) build user content via `SessionDocumentBuilder` (same as stateless), (2) append user message to `ConversationSession`, (3) build request via `_session.BuildRequest()` (includes system blocks + ALL accumulated messages), (4) send via `_client.SendMessagesAsync()`, (5) append raw assistant response to session, (6) parse and return. System blocks come from the `ConversationSession` (set at construction), not from `CacheBlockBuilder`. When no session is active, all methods execute the original stateless code path with no conditional logic overhead. The Anthropic Messages API is stateless (full history must be sent each call), so `ConversationSession` accumulates messages client-side. Messages grow unbounded within a session (acceptable for ~20-turn games within Anthropic's 200k token window). `ConversationSession` does not enforce user/assistant alternation — the caller is responsible. The class is not thread-safe; it is designed for sequential use within one `GameSession`.
- **Session system prompt assembly:** `SessionSystemPromptBuilder.Build` produces a structured 5-section system prompt combining game-level creative direction (`GameDefinition`) with per-character profile prompts. This replaces the placeholder concatenation (player + `\n\n---\n\n` + opponent) used in the initial `GameSession` wiring (#542). The output is passed to `IStatefulLlmAdapter.StartConversation()` to initialize a stateful conversation session. `GameDefinition` can be loaded from a YAML file via `LoadFrom()` or use `PinderDefaults` as a hardcoded fallback. The YAML parsing uses `YamlDotNet 16.3.0` (same version as `Pinder.Rules`), added only to `Pinder.LlmAdapters.csproj` — `Pinder.Core` remains dependency-free.
- **Debug payload logging:** `AnthropicOptions` exposes an optional `DebugDirectory`. When set, `AnthropicLlmAdapter` writes exactly what is sent to and received from the Anthropic API to disk (`turn-XX-callType-request.json` and `-response.json`). It also accumulates token and cache performance metrics via thread-safe tracking, outputting a rolling `session-summary.json` file. This allows inspection of raw LLM interaction and prompt caching behavior without modifying game logic.
- **Provider abstraction:** The Anthropic-specific code is isolated in its own subdirectory. The adapter pattern allows swapping LLM providers without changing prompt templates or game logic.

## Change Log
| Date | Issue | Summary |
|------|-------|---------|
| 2026-04-03 | #311 | Initial creation — Added 10 tell category mappings to `OpponentResponseInstruction` with "ONLY" constraint, preventing LLM from guessing tell stats. Mappings cover all 6 stats (CHARM, RIZZ, HONESTY, CHAOS, WIT, SELF_AWARENESS) across 10 opponent behaviors. |
| 2026-04-03 | #352 | `AnthropicLlmAdapter.GetInterestChangeBeatAsync` now includes opponent system prompt as a system block when `InterestChangeContext.OpponentPrompt` is non-empty, so §3.8 interest change beats are generated in the opponent's character voice. Uses `CacheBlockBuilder.BuildOpponentOnlySystemBlocks`. Tests in `InterestChangeBeatVoiceTests.cs`. |
| 2026-04-04 | #487 | Fix voice bleed — moved opponent prompt out of system blocks into user message for dialogue option generation. `AnthropicLlmAdapter.GetDialogueOptionsAsync` now uses `CacheBlockBuilder.BuildPlayerOnlySystemBlocks` (player only). `SessionDocumentBuilder.BuildDialogueOptionsPrompt` prepends `OPPONENT PROFILE` section in user content when opponent prompt is present. |
| 2026-04-04 | #489 | Voice distinctness — `CharacterProfile` gains `TextingStyleFragment` property (optional, default `""`). `DialogueContext` gains `PlayerTextingStyle` property (optional, default `""`). `SessionDocumentBuilder.BuildDialogueOptionsPrompt` injects `YOUR TEXTING STYLE` block before `YOUR TASK` when style is non-empty. `PromptTemplates.DialogueOptionsInstruction` appended with voice-check reminder. `GameSession.StartTurnAsync` wires player's texting style into `DialogueContext`. Session-runner loaders (`CharacterLoader`, `CharacterDefinitionLoader`) extract/join texting style fragments for `CharacterProfile`. |
| 2026-04-04 | #490 | Opponent resistance — `OpponentResponseInstruction` now embeds a fundamental resistance rule and `{resistance_block}` placeholder. Six `internal const` resistance descriptors added to `PromptTemplates` (bands: 0–4, 5–9, 10–14, 15–20, 21–24, 25). `SessionDocumentBuilder.GetResistanceBlock(int)` selects the appropriate descriptor. `BuildOpponentPrompt` fills the placeholder at runtime. Note: spec proposed `GetResistanceDescriptor` on `PromptTemplates` and a separate `OpponentResistanceRule` constant; implementation places logic on `SessionDocumentBuilder.GetResistanceBlock` and inlines the rule into `OpponentResponseInstruction`. Band names also differ from spec (e.g. "Unstable agreement" vs "Warmth with visible holdback"). Tests in `Issue490_ResistanceSpec_Tests.cs` (25 tests). |
| 2026-04-04 | #491 | Success delivery rewrite — `SuccessDeliveryInstruction` revised to use margin tiers aligned with `SuccessScale` (1–4 clean, 5–9 strong, 10+ critical/Nat 20), replacing old misaligned bands (1–5, 6–10). Removed "add a small flourish" language. Strong tier now sharpens existing phrasing (allows ONE added word/phrase for precision) but explicitly prohibits new ideas. Added counterpart rule: every idea in delivered must map to intended. Tests in `Issue491_SuccessDeliveryTests.cs` and additional assertions in `SessionDocumentBuilderSpecTests.cs`. |
| 2026-04-04 | #493 | Failure degradation legibility — `OpponentContext` gains `DeliveryTier` property (`FailureTier`, default `None`). `GameSession.ResolveTurnAsync` passes `rollResult.Tier` to `OpponentContext`. Five `OpponentReaction*` constants added to `PromptTemplates` (Fumble/Misfire/TropeTrap/Catastrophe/Legendary). `SessionDocumentBuilder.GetOpponentReactionGuidance(FailureTier)` maps tiers to guidance. `BuildOpponentPrompt` injects "FAILURE CONTEXT" section for non-None tiers. Spec divergences: method placed on `SessionDocumentBuilder` (not `PromptTemplates`), section named "FAILURE CONTEXT" (not "DELIVERY NOTE"), constants named `OpponentReaction*` (not `OpponentFailureGuidance` / `Opponent*Guidance`). Tests in `Issue493_FailureDegradationTests.cs`, `Issue493_FailureDegradationSpecTests.cs`, `Issue493_FailureDegradationCoreTests.cs`. |
| 2026-04-05 | #545 | Game definition YAML content validation — Added `GameDefinitionYamlContentTests.cs` (30 tests) in `Pinder.Rules.Tests`. Tests validate that `data/game-definition.yaml` exists, is valid YAML (no tabs, no BOM, all scalar strings, exactly 7 keys), and contains Pinder-specific creative content in all sections (vision, world_description, player_role_description, opponent_role_description, meta_contract, writing_rules). Each section is checked for required domain concepts (e.g. shadow growth, d20 rolls, 4 dialogue options, resistance, ENGINE blocks, asterisk prohibition). The YAML file itself lives outside the repo at `/root/.openclaw/agents-extra/pinder/data/game-definition.yaml` and is consumed by `GameDefinition.LoadFrom()` (#543). No C# production code changed. |
| 2026-04-05 | #541 | Stateful conversation mode — Added `ConversationSession` class (`Pinder.LlmAdapters`) that accumulates user/assistant `Message` objects and builds `MessagesRequest` with cached system blocks (ephemeral `CacheControl`) + full message history snapshot. `AnthropicLlmAdapter` gains `StartConversation(string systemPrompt)` and `HasActiveConversation` property. When active, all four `ILlmAdapter` methods (`GetDialogueOptionsAsync`, `DeliverMessageAsync`, `GetOpponentResponseAsync`, `GetInterestChangeBeatAsync`) append to and read from the session instead of building fresh single-message requests. Stateless fallback is preserved when no session is active. `ILlmAdapter` interface unchanged. Tests: `ConversationSessionTests.cs` (16), `AnthropicLlmAdapterStatefulTests.cs` (12), `Issue541_StatefulConversationTests.cs`, `Issue541_AdditionalTests.cs`. |
| 2026-04-05 | #542 | `IStatefulLlmAdapter` interface + GameSession wiring — New `IStatefulLlmAdapter` interface in `Pinder.Core.Interfaces` formalizes `StartConversation(string)` and `HasActiveConversation` as a sub-interface of `ILlmAdapter`. `AnthropicLlmAdapter` class declaration changed from `ILlmAdapter` to `IStatefulLlmAdapter`. `GameSession` 6-parameter constructor now checks `_llm is IStatefulLlmAdapter` and, if true, builds a system prompt from both character profiles (player + `\n\n---\n\n` + opponent) and calls `StartConversation`. `NullLlmAdapter` unchanged — stateless path preserved. Tests: `Issue542_StatefulSession_TestEngineerTests.cs`. |
| 2026-04-05 | #543 | Session system prompt builder — Added `GameDefinition` sealed class (7 read-only string properties, `LoadFrom(yamlContent)` YAML parser, `PinderDefaults` hardcoded fallback) and `SessionSystemPromptBuilder` static class (`Build` method producing 5-section `== SECTION NAME ==` delimited prompt from character profiles + game definition). `YamlDotNet 16.3.0` added to `Pinder.LlmAdapters.csproj`. Tests: `GameDefinitionTests.cs`, `SessionSystemPromptBuilderTests.cs`, `Issue543_SessionSystemPromptSpecTests.cs` (45 spec-driven tests). |
| 2026-04-06 | #572 | Bug fix — Removed the `[RESPONSE]` tag requirement from `PromptTemplates.OpponentResponseInstruction` so the LLM outputs message text directly. Updated `AnthropicLlmAdapter.ParseOpponentResponse` to extract text before `[SIGNALS]` and gracefully strip legacy `[RESPONSE]` tags or quotes if generated. |
| 2026-04-06 | #530 | Scaled delivery quality infinitely with roll margin. Added `Critical (10-14)` and `Exceptional (15+)` tiers to `SuccessDeliveryInstruction`. Injected exact `{beat_dc_by}` margin and Nat 20 status into the prompt via `SessionDocumentBuilder.BuildDeliveryPrompt()` to instruct the LLM on exactly how well the player rolled. |
| 2026-04-06 | #534 | Added `--debug` flag support to `session-runner` via `AnthropicOptions.DebugDirectory`. `AnthropicLlmAdapter` now intercepts and writes `turn-{turn:D2}-{callType}-request.json` and `response.json` for every API call, plus a `session-summary.json` tracking cumulative input/output and cache tokens. Validated thread-safe stat tracking with 100-thread concurrent test. |
