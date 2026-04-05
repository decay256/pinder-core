# CPO Strategic Review — Sprint: Session Architecture + Archetype Fix

## Alignment: ✅

This sprint is strategically sound and high-leverage. It addresses the single biggest quality gap in the product: **LLM conversations are stateless, rebuilding context from scratch each turn.** Moving to a persistent conversation session (#541→#542→#543→#544) is foundational — it will directly improve character voice consistency, narrative coherence, and conversation quality, which are the core product differentiators. The archetype bug fix (#540) and game definition YAML (#545) are both necessary supporting work. This is building, not polishing.

## Insufficient Requirements

None. All 6 issues have well-specified acceptance criteria and adequate context for prototype maturity.

## Data Flow Traces

### #540: Archetype Level-Range Filtering
- Item/Anatomy sources → `ArchetypeTendencies` strings → `CharacterAssembler.Assemble()` → count & rank → `FragmentCollection.RankedArchetypes`
- Required fields: archetype name, count, **level range (min, max)**, **character level**
- ⚠️ **Missing**: `characterLevel` is not a parameter of `Assemble()`. Archetype level-range metadata has no data source — no `IArchetypeRepository` exists. Filed as **#547**.

### #541→#542: Stateful Conversation Session
- `GameSession` constructor → detect `IStatefulLlmAdapter` → `StartConversation(systemPrompt)` → `ConversationSession` created
- Per turn: `GetDialogueOptionsAsync` / `DeliverMessageAsync` / `GetOpponentResponseAsync` → messages appended to `ConversationSession.Messages` → sent as accumulated `messages[]` to Anthropic API
- Required fields: system prompt string, accumulated messages list, role per message
- ✅ Data path is clean. `AnthropicClient.SendMessagesAsync` already takes `MessagesRequest` — stateful mode just changes how the request is built.

### #543: Session System Prompt
- `CharacterProfile.AssembledSystemPrompt` (player + opponent) + `GameDefinition` fields → `SessionSystemPromptBuilder.Build()` → system prompt string → `IStatefulLlmAdapter.StartConversation()`
- Required fields: player prompt, opponent prompt, game vision, world rules, meta contract, writing rules, character roles
- ✅ All source data available. `GameDefinition` provides game context; `CharacterProfile` provides character bibles.

### #544: [ENGINE] Injection Blocks
- `GameSession` turn data (roll result, interest, failure tier) → `SessionDocumentBuilder` → `[ENGINE]` formatted user message → appended to `ConversationSession`
- Required fields: turn number, player name, stat, success %, risk tier, roll context narrative, interest level, interest band narrative
- ✅ All fields available from existing `DialogueContext`, `DeliveryContext`, `OpponentContext` DTOs.

## Unstated Requirements

- **Stateful session needs a token budget strategy**: Messages accumulate unbounded. At ~20 turns × ~4 messages/turn, that's 80+ messages. Anthropic's context window is large but not infinite. At prototype this is fine, but the design should note where truncation will go.
- **[ENGINE] blocks must not leak into character dialogue**: The LLM must understand [ENGINE] blocks are meta-instructions, not things characters say. The system prompt must establish this contract.
- **GameDefinition fallback must produce identical quality to YAML version**: `PinderDefaults` hardcoded fallback must contain the same content as the YAML file, or sessions without YAML will have noticeably worse output.

## Domain Invariants

- **Pinder.Core must remain zero-dependency**: `IStatefulLlmAdapter` in `Pinder.Core.Interfaces` is an interface only — no YAML, no JSON, no Newtonsoft.
- **`ILlmAdapter` interface is unchanged**: Stateful mode is opt-in via `IStatefulLlmAdapter` extension. All 2,979 existing tests pass via `NullLlmAdapter` (stateless path).
- **`GameDefinition` cannot live in Pinder.Core**: It needs YAML parsing. Must live in LlmAdapters (add YamlDotNet) or Pinder.Rules (already has YamlDotNet) or a new shared project.
- **Character voice identity is per-character**: Stateful session must not blend player and opponent voices. System prompt must establish clear role separation.

## Gaps

- **Missing (filed as #547)**: #540's `CharacterAssembler.Assemble()` lacks `characterLevel` parameter and archetype level-range data source. Implementer needs architectural guidance on where filtering happens.
- **Assumption to validate**: #543 `GameDefinition.LoadFrom` uses YAML, but LlmAdapters currently only has Newtonsoft.Json. Either LlmAdapters gains YamlDotNet dependency, or `GameDefinition` lives in Pinder.Rules. The architect must decide.
- **Assumption to validate**: #544 roll context narratives from enriched YAML `flavor` fields — need to confirm these fields exist in current `rules-v3-enriched.yaml`.

## Requirements Compliance Check

- **DC (zero deps in Core)**: ✅ `IStatefulLlmAdapter` is an interface in Core — no dependency added.
- **FR (backward compat)**: ✅ All changes are additive. `ILlmAdapter` unchanged. `NullLlmAdapter` unchanged. Existing test paths remain stateless.
- **NFR (performance)**: ✅ Stateful session avoids rebuilding context per turn — net positive for token usage. Prompt caching via `cache_control` blocks continues to work.

## Role Assignments

| Issue | Assigned Role | Correct? |
|-------|--------------|----------|
| #540 | backend-engineer | ✅ |
| #541 | backend-engineer | ✅ |
| #542 | backend-engineer | ✅ |
| #543 | backend-engineer | ✅ |
| #544 | backend-engineer | ✅ |
| #545 | backend-engineer | ✅ |

All roles correct. #545 is a data/content file but backend-engineer is appropriate since it must be parseable by code.

## Wave Plan

```
Wave 1: #540, #541, #545
Wave 2: #542 (depends on #541)
Wave 3: #543 (depends on #541, #542)
Wave 4: #544 (depends on #541, #542, #543)
```

## Recommendations

1. **Architect must resolve #540 data path** (#547): Decide whether `Assemble()` gains new params or filtering is post-assembly. This blocks implementation.
2. **Architect must decide where `GameDefinition` lives**: LlmAdapters (add YamlDotNet) vs Pinder.Rules (already has it). Affects project references and dependency graph.
3. **Add a comment to #544** noting that [ENGINE] block format must be established in the session system prompt (#543) so the LLM knows to treat them as meta-instructions.

## Verdict: ADVISORY

One vision-concern filed (#547) for the #540 archetype level-gating data flow gap. The sprint is strategically aligned and should proceed — the concern is actionable and can be resolved during architect review. No blocking issues.
