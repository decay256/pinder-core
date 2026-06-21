# Prompt-cache immutable-first ordering contract (#1208)

## Contract
The established rule for prompt construction to maximize cacheability is:
(a) Immutable/static instructions must come first.
(b) Cache breakpoints are placed on the last block of the stable prefix.
(c) Volatile history, current-turn content, and state appear as a suffix.

## System prompts (compliant)
`SessionSystemPromptBuilder.BuildPlayerAvatarEx` and `BuildDateeEx` already emit the static GM base first and the per-character spec last (after '== CHARACTER YOU CONTROL =='). Anthropic/OpenRouter transports attach `cache_control` `ephemeral` to that stable system block (`AnthropicTransport.cs`, `OpenAiCacheControl.cs`). This is the GOOD pattern.

## User-message prompts (documented exceptions)
`BuildDialogueOptionsPromptEx` and `BuildDateePromptEx` are NOT fully immutable-first and CANNOT be safely reordered, because:
- Their trailing output-format/engine instruction templates back-reference earlier volatile content positionally ('character profile above', 'messages above', 'system prompt above', 'profile below').
- The `[ENGINE]` blocks interpolate volatile state (`{game_state}`, `{interest_narrative}`).
- Golden/order regression tests (Issue1153 golden fixtures, EngineInjectionBlockTests, SessionDocumentBuilderTests, Issue489 voice tests) pin the current order.

Reordering would change rendered semantics and model behavior. The current INTENDED order (history/state -> engine block -> trailing static instruction) is pinned by `tests/Pinder.LlmAdapters.Tests/Issue1208_PromptOrderingTests.cs` so it cannot silently regress.

## User-message cache-control limitation (follow-up)
Note that because the user message is flattened into one string mixing volatile history + static instructions, the Anthropic ephemeral marker on the current-turn user block does not sit on a pure stable prefix. Splitting the user payload into `[static_prefix, volatile_suffix]` content blocks is a larger follow-up beyond #1208's safe scope.

## Regression tests
Reference `tests/Pinder.LlmAdapters.Tests/Issue1208_PromptOrderingTests.cs`.
These tests pin the current structure of the prompts and document the builders to ensure order does not silently regress and that exceptions to the immutable-first rule remain well documented.
