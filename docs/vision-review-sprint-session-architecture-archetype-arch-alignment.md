# CPO Strategic Review — Sprint: Session Architecture + Archetype Fix (Architecture Alignment Pass)

## Alignment: ✅

The architect's output is well-aligned with the product vision and appropriate for prototype maturity. The key structural decision — `IStatefulLlmAdapter : ILlmAdapter` with opt-in stateful conversation — is the right abstraction. It introduces the highest-leverage change (persistent conversation context) while preserving zero regression risk via the `NullLlmAdapter` stateless fallback. The contracts are precise, dependency direction is correct (LlmAdapters → Core, never reverse), and the 4-wave ordering respects the actual dependency chain.

## Architecture Fit for Maturity Level

### Appropriate complexity
- **`IStatefulLlmAdapter` as interface inheritance**: Simple, idiomatic, backward-compatible. Not over-engineered — a single `is` check in the GameSession constructor. ✅
- **`ConversationSession` as a plain class with `AppendUser`/`AppendAssistant`/`BuildRequest`**: Minimal API surface, easy to test. No framework abstractions. ✅
- **`GameDefinition` with `PinderDefaults` fallback**: Appropriate for prototype — works without YAML, works better with it. ✅
- **`CharacterAssembler` optional params**: Adding 2 nullable params is acceptable at prototype. The fallback-to-unfiltered-if-all-filtered-out is a good safety net. ✅

### Not over-engineered
- No event sourcing, no message bus, no complex state machines
- No new interfaces beyond `IStatefulLlmAdapter` (which is necessary)
- `ConversationSession` is a simple list — no abstractions over abstractions

## Coupling Analysis

### ✅ Good separation
- `ConversationSession` knows nothing about game rules — just messages
- `IStatefulLlmAdapter` is defined in Core but implemented in LlmAdapters — proper DI
- `EngineInjectionBuilder` is pure formatting — no state, no transport
- `SessionSystemPromptBuilder` is pure string assembly — no I/O
- `GameDefinition` is a data carrier — parse once, read many

### ⚠️ Minor concern: Adapter 1:1 assumption
The contract states "One ConversationSession per adapter instance" and "adapter is 1:1 with GameSession." This is fine at prototype, but if `ConversationRegistry` (multi-session) ever needs multiple GameSessions with different opponents, each needs its own adapter instance. The architect acknowledges this. Not blocking — just a known trajectory.

## Abstraction Choices — Reversibility

- **`IStatefulLlmAdapter`**: Easy to extend at MVP (e.g., add `EndConversation()`, token counting). Easy to drop if stateful mode proves unnecessary. ✅
- **`ConversationSession` as public class**: If it needs to become internal later, the only external consumer is tests. Low blast radius. ✅
- **`GameDefinition.LoadFrom(string yamlContent)`**: Takes string, not file path — proper separation. Caller owns I/O. ✅
- **Adding YamlDotNet to LlmAdapters**: This is the one slightly questionable choice (LlmAdapters grows from 1 NuGet dep to 2), but GameDefinition needs YAML parsing and LlmAdapters is the correct home. Moving it to Pinder.Rules would couple creative direction to the rules engine, which is worse. ✅

## Interface Design Review

### `IStatefulLlmAdapter`
- `StartConversation(string systemPrompt)` returns `void` — adapter owns session lifecycle internally. This is clean. No leaky abstractions.
- `HasActiveConversation` is a useful diagnostic property.
- Not exposing `ConversationSession` type in the interface avoids Core depending on LlmAdapters DTOs. ✅

### `EngineInjectionBuilder`
- Static methods taking existing context DTOs — no new types needed. ✅
- 6 interest narrative bands are hardcoded — could become data-driven later but fine for prototype.

### `CharacterAssembler.Assemble()` extension
- Optional params with null defaults — 100% backward compatible. ✅
- "Unknown archetypes excluded" and "all filtered out → fallback" are good safety valves.

## Data Flow Validation

### Stateful conversation path
```
GameSession ctor → is IStatefulLlmAdapter? → StartConversation(systemPrompt)
  ↓ (each turn)
ILlmAdapter method call → adapter appends user msg from EngineInjectionBuilder → sends full messages[] → parses response → appends assistant msg → returns parsed result
```
✅ Complete path. No missing data. Context accumulates naturally.

### Archetype level-range path
```
session-runner loads stat-to-archetype.json → extracts level_range per archetype → passes as IReadOnlyDictionary<string, (int, int)> to Assemble() → filter before sort → FragmentCollection
```
✅ Complete path. Vision concern #547 is resolved by the contract.

## Requirements Compliance

- **DC (zero deps in Core)**: ✅ `IStatefulLlmAdapter` is interface-only in Core
- **DC (netstandard2.0)**: ✅ No records, sealed classes throughout
- **FR (backward compat)**: ✅ All existing tests pass via stateless path, all new params have defaults
- **NFR (performance)**: ✅ Prompt caching via `cache_control` blocks on system prompt. Full history sent each call is how Anthropic API works — no alternative.

## Gap Reconciliation with First Pass

My first-pass vision review (#547) identified the CharacterAssembler data path gap. The architect resolved it cleanly with optional params + caller-owned data loading. All three acceptance criteria from #547 are addressed:
1. ✅ `characterLevel` reaches filtering via optional param
2. ✅ Level-range data loaded by session-runner, passed as dictionary
3. ✅ Backward-compatible (null defaults)

## Concerns

No architectural concerns found. The design is clean, appropriately scoped for prototype, and doesn't create coupling that would be painful to unwind at MVP. The dual code paths (stateful/stateless) in the adapter are the main complexity cost, but they're necessary for backward compatibility and will be the natural unification point at MVP.

## Verdict: CLEAN

Architecture aligns with product vision. No concerns to file. The 4-wave implementation plan is sound, contracts are precise, and all dependency directions are correct. Proceed with implementation.
