# AGENTS.md — Pinder.Core

## What has been built

### Sprint: Session Architecture

#### Issue #542 — GameSession: create LLM conversation session at start, use for all turns
- **Status**: Complete
- **Files changed**:
  - `src/Pinder.Core/Interfaces/IStatefulLlmAdapter.cs` — New interface extending `ILlmAdapter` with `StartConversation(string)` and `HasActiveConversation`
  - `src/Pinder.Core/Conversation/GameSession.cs` — Constructor detects `IStatefulLlmAdapter` via `is` pattern match, builds system prompt from both character profiles (separated by `\n\n---\n\n`), calls `StartConversation`
  - `src/Pinder.LlmAdapters/Anthropic/AnthropicLlmAdapter.cs` — Implements `IStatefulLlmAdapter` (was `ILlmAdapter`)
- **Tests**: 36 tests across 3 files
  - `tests/Pinder.Core.Tests/Issue542_StatefulSessionTests.cs` — 10 tests (constructor detection, prompt format, backward compat, turn flow)
  - `tests/Pinder.Core.Tests/Issue542_StatefulSession_SpecTests.cs` — 20 tests (spec-driven AC verification, edge cases, integration)
  - `tests/Pinder.LlmAdapters.Tests/Issue542_IStatefulLlmAdapterTests.cs` — 6 tests (interface hierarchy, cast behavior)
- **Known issues**: None
- **Deviations from contract**: None — `IStatefulLlmAdapter` design from issue note was followed exactly
