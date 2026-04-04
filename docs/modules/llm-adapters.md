# LLM Adapters

## Overview
The LLM Adapters module (`Pinder.LlmAdapters`) provides prompt templates and API clients for integrating large language models into Pinder's conversation game. It defines the structured instruction templates that guide LLM output (dialogue options, opponent responses, interest beats) and handles communication with external LLM providers.

## Key Components

| File | Description |
|------|-------------|
| `PromptTemplates.cs` | Static instruction templates (§3.2–3.8) with `{placeholder}` tokens for dynamic content |
| `SessionDocumentBuilder.cs` | Fills placeholder tokens in prompt templates with session-specific data; also injects opponent profile as informational context in user messages |
| `Anthropic/AnthropicClient.cs` | HTTP client for the Anthropic Messages API |
| `Anthropic/AnthropicLlmAdapter.cs` | Adapter implementing the LLM interface using Anthropic's API |
| `Anthropic/AnthropicOptions.cs` | Configuration options for the Anthropic client |
| `Anthropic/CacheBlockBuilder.cs` | Builds cache-control blocks for Anthropic prompt caching |
| `Anthropic/AnthropicApiException.cs` | Exception type for Anthropic API errors |
| `Anthropic/Dto/MessagesRequest.cs` | Request DTO for the Anthropic Messages API |
| `Anthropic/Dto/MessagesResponse.cs` | Response DTO for the Anthropic Messages API |
| `Anthropic/Dto/ContentBlock.cs` | Content block DTO for Anthropic message payloads |

## API / Public Interface

### `PromptTemplates` (static class)

- **`DialogueOptionsInstruction`** (`const string`) — §3.2: Instructs the LLM to generate exactly 4 dialogue options tagged with stat, callback, combo, and tell bonus metadata.
- **`OpponentResponseInstruction`** (`const string`) — §3.5: Instructs the LLM to generate an opponent response with optional `[SIGNALS]` block containing TELLs and WEAKNESSes. Includes 10 explicit tell category mappings (behavior → stat) to constrain LLM output.
- **`InterestBeatInstruction`** (`const string`) — §3.8: Generates narrative beats when interest crosses a threshold.
- **`InterestBeatAbove15`** (`internal const string`) — Sub-instruction for interest rising above 15.
- **`InterestBeatBelow8`** (`internal const string`) — Sub-instruction for interest dropping below 8.
- **`InterestBeatDateSecured`** (`internal const string`) — Sub-instruction for date-secured outcome.
- **`InterestBeatUnmatched`** (`internal const string`) — Sub-instruction for unmatched outcome.

### `SessionDocumentBuilder.BuildDialogueOptionsPrompt(DialogueContext)`

Builds the user message content for dialogue option generation. When `context.OpponentPrompt` is non-empty, prepends an `OPPONENT PROFILE` section (labelled "NOT who you are") before the conversation history. This provides the LLM with opponent context without placing the opponent's identity in the system prompt.

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
- **Structured output:** Templates enforce strict output formats (`[RESPONSE]`, `[SIGNALS]`, `[STAT: X]` tags) so responses can be parsed deterministically.
- **Tell category constraint:** The `OpponentResponseInstruction` explicitly lists which opponent behaviors map to which stat categories, preventing the LLM from inventing arbitrary tell associations. This was added to close a gap where the LLM was guessing which tells to produce.
- **Character-voiced interest beats:** `GetInterestChangeBeatAsync` injects the opponent's system prompt as a system block (via `CacheBlockBuilder.BuildOpponentOnlySystemBlocks`) when `InterestChangeContext.OpponentPrompt` is non-empty. This ensures §3.8 interest change beats are generated in the opponent's voice rather than generic narration. When no prompt is provided, no system blocks are sent (backward-compatible).
- **Voice bleed prevention (dialogue options):** `GetDialogueOptionsAsync` places only the player's prompt in the system blocks (via `CacheBlockBuilder.BuildPlayerOnlySystemBlocks`). The opponent's prompt is moved to the user message as an `OPPONENT PROFILE` informational section built by `SessionDocumentBuilder`. This prevents the LLM from adopting the opponent's register/voice when generating dialogue options for the player. The opponent profile is explicitly labelled "NOT who you are" to reinforce the boundary.
- **Provider abstraction:** The Anthropic-specific code is isolated in its own subdirectory. The adapter pattern allows swapping LLM providers without changing prompt templates or game logic.

## Change Log
| Date | Issue | Summary |
|------|-------|---------|
| 2026-04-03 | #311 | Initial creation — Added 10 tell category mappings to `OpponentResponseInstruction` with "ONLY" constraint, preventing LLM from guessing tell stats. Mappings cover all 6 stats (CHARM, RIZZ, HONESTY, CHAOS, WIT, SELF_AWARENESS) across 10 opponent behaviors. |
| 2026-04-03 | #352 | `AnthropicLlmAdapter.GetInterestChangeBeatAsync` now includes opponent system prompt as a system block when `InterestChangeContext.OpponentPrompt` is non-empty, so §3.8 interest change beats are generated in the opponent's character voice. Uses `CacheBlockBuilder.BuildOpponentOnlySystemBlocks`. Tests in `InterestChangeBeatVoiceTests.cs`. |
| 2026-04-04 | #487 | Fix voice bleed — moved opponent prompt out of system blocks into user message for dialogue option generation. `AnthropicLlmAdapter.GetDialogueOptionsAsync` now uses `CacheBlockBuilder.BuildPlayerOnlySystemBlocks` (player only). `SessionDocumentBuilder.BuildDialogueOptionsPrompt` prepends `OPPONENT PROFILE` section in user content when opponent prompt is present. |
