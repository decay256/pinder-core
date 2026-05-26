# Sprint 2026-05-24-a7b213 — Lessons Learned

This file documents the core learnings and engineering insights captured during the global monolith decomposition sprint.

## Project-Specific Lessons

### DECOMPOSITION-VIA-PARTIAL-CLASSES

**Symptom:** Core services and adapters like `OpenAiLlmAdapter.cs`, `TextingStyleAggregator.cs`, `GameDefinition.cs`, and large test monoliths like `EngineInjectionBlockTests.cs` grow beyond 500 lines, leading to token-exhaustion, slower reasoning speeds, and high maintenance costs.

**Root cause:** Broad responsibilities (e.g., parsing, default setup, UI helpers, state tracking) are lumped into a single C# source file.

**Fix:** Use C# `partial` classes to separate concerns across physically distinct files:
- Keep the primary class file as a clean entry point containing main properties and high-level methods.
- Move distinct sub-responsibilities to modular partial files (e.g., `Class.Parser.cs`, `Class.Defaults.cs`, `Class.Helpers.cs`).
- Enforce strict size limits of ≤500 lines per file (ideally under 400 lines).

**Rule:** Any new class or test monolith approaching 400 lines MUST be pre-emptively designed or refactored into focused modular partial classes to ensure high readability and maintainability.
