# Contracts: Sprint — Session Architecture + Archetype Fix

## Issue #540 — Archetype level-range filtering

### Component: `CharacterAssembler` (Pinder.Core.Characters)

**Change type:** Backward-compatible signature extension

**Current signature:**
```csharp
public FragmentCollection Assemble(
    IEnumerable<string> equippedItemIds,
    IReadOnlyDictionary<string, string> anatomySelections,
    IReadOnlyDictionary<StatType, int> playerBaseStats,
    IReadOnlyDictionary<ShadowStatType, int> shadowStats)
```

**New signature (additive):**
```csharp
public FragmentCollection Assemble(
    IEnumerable<string> equippedItemIds,
    IReadOnlyDictionary<string, string> anatomySelections,
    IReadOnlyDictionary<StatType, int> playerBaseStats,
    IReadOnlyDictionary<ShadowStatType, int> shadowStats,
    int? characterLevel = null,
    IReadOnlyDictionary<string, (int Min, int Max)>? archetypeLevelRanges = null)
```

**Behavioral contract:**
- When `characterLevel` AND `archetypeLevelRanges` are both non-null:
  - Before sorting archetypes by count (step 6), filter `archetypeCount` dictionary to only include archetypes where `archetypeLevelRanges[archetype].Min <= characterLevel <= archetypeLevelRanges[archetype].Max`
  - If an archetype name from items/anatomy is NOT found in `archetypeLevelRanges`, it is excluded (conservative — unknown archetypes are filtered out)
  - If ALL archetypes are filtered out, return the unfiltered list (fallback to current behavior)
- When either param is null: no filtering (100% backward-compatible)
- Return type unchanged: `FragmentCollection`
- All existing callers compile unchanged (default params)

**Archetype level-range data source:**
- `stat-to-archetype.json` (at `/root/.openclaw/agents-extra/pinder/data/archetypes/stat-to-archetype.json`) has `"level_range": [min, max]` per archetype
- `archetypes-enriched.yaml` also has `level_range: [min, max]` in condition blocks
- Session-runner `CharacterDefinitionLoader` is responsible for loading this data and passing it to `Assemble()`

**Dependencies:** None (self-contained change in CharacterAssembler)

**Consumers:** `CharacterDefinitionLoader` (session-runner), tests

**Files changed:**
- `src/Pinder.Core/Characters/CharacterAssembler.cs` — add params + filter logic
- `session-runner/CharacterDefinitionLoader.cs` — load archetype data, pass to Assemble()
- `tests/Pinder.Core.Tests/` — new tests for level-gating

---

## Issue #541 — ConversationSession (stateful message accumulator)

### Component: `ConversationSession` (Pinder.LlmAdapters)

**New class:**
```csharp
namespace Pinder.LlmAdapters
{
    /// <summary>
    /// Accumulates messages across LLM calls within a single game session.
    /// Thread-unsafe — designed for sequential use within one GameSession.
    /// </summary>
    public sealed class ConversationSession
    {
        /// <summary>System prompt blocks (cached).</summary>
        public ContentBlock[] SystemBlocks { get; }

        /// <summary>
        /// All accumulated messages in conversation order.
        /// </summary>
        public IReadOnlyList<Message> Messages { get; }

        /// <summary>
        /// Creates session with the given system prompt text.
        /// System prompt is wrapped in a single cached ContentBlock.
        /// </summary>
        public ConversationSession(string systemPrompt);

        /// <summary>Append a user message to the conversation.</summary>
        public void AppendUser(string content);

        /// <summary>Append an assistant message to the conversation.</summary>
        public void AppendAssistant(string content);

        /// <summary>
        /// Build a MessagesRequest using accumulated state.
        /// System blocks + all messages + specified params.
        /// </summary>
        public MessagesRequest BuildRequest(
            string model, int maxTokens, double temperature);
    }
}
```

**Behavioral contract:**
- Messages are stored in order of `AppendUser`/`AppendAssistant` calls
- `BuildRequest()` includes ALL accumulated messages (full history sent each API call)
- System blocks use `cache_control: ephemeral` for prompt caching
- No truncation or summarization (prototype maturity)
- Messages grow unbounded within a session

**Dependencies:** `Pinder.LlmAdapters.Anthropic.Dto` (ContentBlock, Message, MessagesRequest)

**Files changed:**
- `src/Pinder.LlmAdapters/ConversationSession.cs` (new)
- `tests/Pinder.LlmAdapters.Tests/ConversationSessionTests.cs` (new)

---

## Issue #542 — IStatefulLlmAdapter + GameSession wiring

### Component: `IStatefulLlmAdapter` (Pinder.Core.Interfaces)

**New interface:**
```csharp
namespace Pinder.Core.Interfaces
{
    /// <summary>
    /// Extends ILlmAdapter with stateful conversation support.
    /// When implemented, GameSession creates a persistent conversation
    /// at construction and routes all LLM calls through it.
    /// </summary>
    public interface IStatefulLlmAdapter : ILlmAdapter
    {
        /// <summary>
        /// Start a new conversation session with the given system prompt.
        /// The adapter internally tracks the active session.
        /// Subsequent ILlmAdapter method calls will use
        /// the accumulated message history.
        /// Call once per GameSession lifetime.
        /// </summary>
        void StartConversation(string systemPrompt);

        /// <summary>
        /// Whether a conversation session is currently active.
        /// </summary>
        bool HasActiveConversation { get; }
    }
}
```

**Behavioral contract:**
- `StartConversation(string)` initializes internal `ConversationSession`
- After `StartConversation`, all `ILlmAdapter` method calls append to and read from the session
- Calling `StartConversation` when a session is already active replaces it (no error)
- `HasActiveConversation` returns true after `StartConversation` called
- `NullLlmAdapter` does NOT implement this — remains `ILlmAdapter` only

### Component: `GameSession` wiring (Pinder.Core.Conversation)

**Change to GameSession constructor:**
```csharp
// In the constructor body (after existing initialization):
if (_llm is IStatefulLlmAdapter stateful)
{
    // Build system prompt from both character profiles
    string systemPrompt = _player.AssembledSystemPrompt
        + "\n\n---\n\n"
        + _opponent.AssembledSystemPrompt;
    stateful.StartConversation(systemPrompt);
}
```

**Note:** The system prompt assembly above is a placeholder. #543 defines `SessionSystemPromptBuilder` which produces the proper prompt. #542 implementer should use the simple concatenation above and note that #543 will replace it.

**Behavioral contract:**
- GameSession checks `_llm is IStatefulLlmAdapter` once at construction
- If true: starts conversation (system prompt from both profiles)
- If false: all existing behavior is unchanged
- ILlmAdapter method calls in GameSession are unchanged — the adapter internally routes through the session
- All existing tests pass because NullLlmAdapter is not IStatefulLlmAdapter

**Dependencies:**
- #541 (ConversationSession must exist)

**Files changed:**
- `src/Pinder.Core/Interfaces/IStatefulLlmAdapter.cs` (new)
- `src/Pinder.Core/Conversation/GameSession.cs` (constructor addition)
- `src/Pinder.LlmAdapters/Anthropic/AnthropicLlmAdapter.cs` (implement IStatefulLlmAdapter)
- `tests/Pinder.Core.Tests/GameSessionStatefulTests.cs` (new)

---

## Issue #543 — SessionSystemPromptBuilder + GameDefinition

### Component: `GameDefinition` (Pinder.LlmAdapters)

**New class:**
```csharp
namespace Pinder.LlmAdapters
{
    /// <summary>
    /// Data carrier for game-level creative direction.
    /// Loaded from YAML or used with hardcoded defaults.
    /// </summary>
    public sealed class GameDefinition
    {
        public string Name { get; }
        public string Vision { get; }
        public string WorldDescription { get; }
        public string PlayerRoleDescription { get; }
        public string OpponentRoleDescription { get; }
        public string MetaContract { get; }
        public string WritingRules { get; }

        public GameDefinition(
            string name, string vision,
            string worldDescription,
            string playerRoleDescription,
            string opponentRoleDescription,
            string metaContract,
            string writingRules);

        /// <summary>
        /// Parse from YAML string content.
        /// Throws FormatException on invalid YAML.
        /// </summary>
        public static GameDefinition LoadFrom(string yamlContent);

        /// <summary>
        /// Hardcoded Pinder defaults. Used when YAML unavailable.
        /// </summary>
        public static GameDefinition PinderDefaults { get; }
    }
}
```

### Component: `SessionSystemPromptBuilder` (Pinder.LlmAdapters)

**New static class:**
```csharp
namespace Pinder.LlmAdapters
{
    /// <summary>
    /// Builds the session-level system prompt that persists across all turns.
    /// Contains: game vision, world rules, character bibles, meta contract.
    /// </summary>
    public static class SessionSystemPromptBuilder
    {
        /// <summary>
        /// Build the full session system prompt.
        /// </summary>
        /// <param name="playerPrompt">Player's assembled character prompt.</param>
        /// <param name="opponentPrompt">Opponent's assembled character prompt.</param>
        /// <param name="gameDef">Game definition (vision, world, meta). Null → PinderDefaults.</param>
        /// <returns>Assembled system prompt string.</returns>
        public static string Build(
            string playerPrompt,
            string opponentPrompt,
            GameDefinition? gameDef = null);
    }
}
```

**Behavioral contract:**
- Output has 5 sections in order:
  1. `GAME VISION` — from `gameDef.Vision`
  2. `WORLD RULES` — from `gameDef.WorldDescription`
  3. `PLAYER CHARACTER` — verbatim `playerPrompt`
  4. `OPPONENT CHARACTER` — verbatim `opponentPrompt`
  5. `META CONTRACT` — from `gameDef.MetaContract` + `gameDef.WritingRules`
- When `gameDef` is null, uses `GameDefinition.PinderDefaults`
- Pure function — no I/O, no state

**YAML parsing note:** `GameDefinition.LoadFrom` needs to parse YAML. Options:
- LlmAdapters already depends on Newtonsoft.Json. YAML is not JSON.
- Option A: Add YamlDotNet to LlmAdapters (it's already in Pinder.Rules)
- Option B: Use a simple line-based parser (YAML with only top-level string keys)
- **Decision: Use YamlDotNet** — the YAML has nested content; simple parsing is fragile. Add `YamlDotNet 16.3.0` to `Pinder.LlmAdapters.csproj`.

**Dependencies:**
- #541 (ConversationSession infrastructure)
- #542 (GameSession wiring calls this builder)

**Files changed:**
- `src/Pinder.LlmAdapters/GameDefinition.cs` (new)
- `src/Pinder.LlmAdapters/SessionSystemPromptBuilder.cs` (new)
- `src/Pinder.LlmAdapters/Pinder.LlmAdapters.csproj` (add YamlDotNet if chosen)
- `tests/Pinder.LlmAdapters.Tests/SessionSystemPromptBuilderTests.cs` (new)
- `tests/Pinder.LlmAdapters.Tests/GameDefinitionTests.cs` (new)

---

## Issue #544 — [ENGINE] injection blocks

### Component: `EngineInjectionBuilder` (Pinder.LlmAdapters)

**New static class:**
```csharp
namespace Pinder.LlmAdapters
{
    /// <summary>
    /// Builds [ENGINE] injection blocks for stateful conversation mode.
    /// Each block wraps game mechanics as narrative context for the LLM.
    /// </summary>
    public static class EngineInjectionBuilder
    {
        /// <summary>
        /// Build [ENGINE] block for dialogue option generation.
        /// Contains: turn number, interest state, active traps,
        /// horniness level, callback opportunities.
        /// </summary>
        public static string BuildOptionsInjection(DialogueContext context);

        /// <summary>
        /// Build [ENGINE] block for message delivery.
        /// Contains: roll result, failure/success tier, beat-DC-by,
        /// roll context narrative from YAML flavor fields.
        /// </summary>
        public static string BuildDeliveryInjection(
            DeliveryContext context,
            string? rollFlavorText = null);

        /// <summary>
        /// Build [ENGINE] block for opponent response.
        /// Contains: interest change narrative (6 bands),
        /// interest before/after, delivery tier.
        /// </summary>
        public static string BuildOpponentInjection(
            OpponentContext context,
            string? interestNarrative = null);

        /// <summary>
        /// Build [ENGINE] block for interest change beat.
        /// Contains: interest threshold crossing details.
        /// </summary>
        public static string BuildInterestBeatInjection(
            InterestChangeContext context);
    }
}
```

**[ENGINE] block format:**
```
[ENGINE — Turn 3: Option Generation]
Interest: 14/25 — Interested 😊
Active traps: none
Horniness: 6 (1 Rizz option required)
Callbacks available: Turn 1 (opener reference)

Generate 4 dialogue options...
```

```
[ENGINE — Turn 3: Delivery]
Roll: d20(14) + Charm(4) + LevelBonus(2) = 20 vs DC 15
Result: SUCCESS — beat DC by 5 (Strong hit)
Deliver the chosen message with sharpened phrasing...
```

```
[ENGINE — Turn 3: Opponent Response]
Interest: 14 → 16 (crossed into Very Into It)
Player's delivered message was a strong success.
Respond in character. You are warming to this person but not won over.
```

**Behavioral contract:**
- Each method returns a plain string — the adapter wraps it in a user message
- Interest narrative bands (6 bands per AC):
  - 0: Unmatched — ghosting territory
  - 1-4: Bored — actively disengaged
  - 5-9: Lukewarm — not impressed
  - 10-15: Interested — open but guarded
  - 16-24: Very Into It / Almost There — genuine interest
  - 25: Date Secured — resistance dissolved
- Roll context narratives sourced from enriched YAML `flavor` fields (optional param — null means generic)
- Pure functions — no I/O, no state

**Integration with AnthropicLlmAdapter:**
- In stateful mode: adapter calls `EngineInjectionBuilder.BuildXxxInjection()` instead of `SessionDocumentBuilder.BuildXxxPrompt()`
- Appends result as user message to `ConversationSession`
- In stateless mode: existing `SessionDocumentBuilder` path unchanged

**Dependencies:**
- #541 (ConversationSession)
- #542 (stateful adapter wiring)
- #543 (session system prompt)

**Files changed:**
- `src/Pinder.LlmAdapters/EngineInjectionBuilder.cs` (new)
- `src/Pinder.LlmAdapters/Anthropic/AnthropicLlmAdapter.cs` (add stateful code paths)
- `tests/Pinder.LlmAdapters.Tests/EngineInjectionBuilderTests.cs` (new)

---

## Issue #545 — game-definition.yaml

### Component: Data file (external repo)

**File path:** `/root/.openclaw/agents-extra/pinder/data/game-definition.yaml`

**Schema:**
```yaml
name: "Pinder"
vision: |
  <multi-line game vision text>
world_description: |
  <multi-line world rules text>
player_role_description: |
  <multi-line player role text>
opponent_role_description: |
  <multi-line opponent role text>
meta_contract: |
  <multi-line meta contract text>
writing_rules: |
  <multi-line writing rules text>
```

**Behavioral contract:**
- All 7 top-level keys are required strings
- Values are YAML multi-line strings (block scalar `|`)
- Content must be genuine Pinder creative direction (not boilerplate)
- File must be parseable by `GameDefinition.LoadFrom()` (#543)
- No code changes required — pure data file

**Dependencies:** None (but consumed by #543)

**Files changed:**
- `/root/.openclaw/agents-extra/pinder/data/game-definition.yaml` (new)

---

## Cross-cutting: Separation of Concerns Map

- ConversationSession
  - Responsibility:
    - Accumulate user/assistant messages
    - Build MessagesRequest from accumulated state
  - Interface:
    - AppendUser(string)
    - AppendAssistant(string)
    - BuildRequest(model, maxTokens, temperature)
  - Must NOT know:
    - Game rules or roll resolution
    - Prompt content or structure
    - HTTP transport

- IStatefulLlmAdapter
  - Responsibility:
    - Extend ILlmAdapter with session lifecycle
  - Interface:
    - StartConversation(string systemPrompt)
    - HasActiveConversation
  - Must NOT know:
    - ConversationSession implementation
    - GameSession internals
    - Game rules

- SessionSystemPromptBuilder
  - Responsibility:
    - Assemble session-level system prompt
  - Interface:
    - Build(playerPrompt, opponentPrompt, gameDef?)
  - Must NOT know:
    - HTTP transport
    - Conversation session management
    - Roll resolution or game mechanics

- GameDefinition
  - Responsibility:
    - Carry game-level creative direction data
    - Parse YAML and provide defaults
  - Interface:
    - LoadFrom(string yamlContent)
    - PinderDefaults
    - Name, Vision, WorldDescription, etc.
  - Must NOT know:
    - Prompt assembly
    - LLM transport
    - Game session lifecycle

- EngineInjectionBuilder
  - Responsibility:
    - Format game events as [ENGINE] blocks
  - Interface:
    - BuildOptionsInjection(DialogueContext)
    - BuildDeliveryInjection(DeliveryContext, flavor?)
    - BuildOpponentInjection(OpponentContext, narrative?)
    - BuildInterestBeatInjection(InterestChangeContext)
  - Must NOT know:
    - HTTP transport
    - Session management
    - Roll resolution math

- CharacterAssembler (extended)
  - Responsibility:
    - Assemble FragmentCollection from items + anatomy
    - Filter archetypes by level range (new)
  - Interface:
    - Assemble(items, anatomy, stats, shadows, level?, ranges?)
  - Must NOT know:
    - LLM interactions
    - Prompt building
    - Game session state

---

## Implementation Strategy

### Wave ordering (per CPO recommendation):

**Wave 1 — No dependencies (parallel):**
- #540 (archetype level-range filter)
- #541 (ConversationSession)
- #545 (game-definition.yaml data file)

**Wave 2 — Depends on #541:**
- #542 (IStatefulLlmAdapter + GameSession wiring)

**Wave 3 — Depends on #541, #542:**
- #543 (SessionSystemPromptBuilder + GameDefinition)

**Wave 4 — Depends on #541, #542, #543:**
- #544 ([ENGINE] injection blocks)

### Tradeoffs

1. **Dual code paths in adapter** — Stateful and stateless modes coexist. This adds complexity but is necessary for backward compatibility with NullLlmAdapter and all existing tests. At MVP, consider making stateful the only mode.

2. **YamlDotNet in LlmAdapters** — Adding a second NuGet dependency to LlmAdapters. Acceptable because LlmAdapters is already the "external deps" project. Alternatively, GameDefinition could live in Pinder.Rules (which already has YamlDotNet), but that couples game creative direction to the rules engine, which is wrong conceptually.

3. **ConversationSession unbounded growth** — At prototype, message history grows forever. A 20-turn session might accumulate ~50 messages. With Anthropic's 200k context window, this is fine. At MVP, add a compaction/summarization strategy.

4. **CharacterAssembler gets wider** — Adding 2 more optional params to `Assemble()`. The method already has 4 params. At MVP, consider a builder pattern or options object. At prototype, optional params are fine.

### Risk mitigation

- **If stateful conversation breaks existing behavior**: `NullLlmAdapter` doesn't implement `IStatefulLlmAdapter`, so ALL existing tests use the stateless path. Zero regression risk.
- **If YAML parsing fails**: `GameDefinition.PinderDefaults` provides hardcoded fallback. Session works without YAML.
- **If [ENGINE] injection produces bad LLM output**: Stateless fallback is always available by not implementing `IStatefulLlmAdapter`.
