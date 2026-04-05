# Spec: Build Session System Prompt — Issue #543

**Module**: docs/modules/llm-adapters.md (create new)

---

## Overview

This issue introduces two new classes in `Pinder.LlmAdapters`: `GameDefinition` (a data carrier for game-level creative direction parsed from YAML or provided via hardcoded defaults) and `SessionSystemPromptBuilder` (a pure static builder that assembles a session-level system prompt from character profiles and game definition data). Together they produce the persistent system prompt used by the stateful conversation session (#542), ensuring the LLM has consistent game context, character bibles, and creative direction across all turns.

---

## Function Signatures

### `GameDefinition` (Pinder.LlmAdapters)

```csharp
namespace Pinder.LlmAdapters
{
    public sealed class GameDefinition
    {
        // --- Properties (all non-null strings) ---
        public string Name { get; }
        public string Vision { get; }
        public string WorldDescription { get; }
        public string PlayerRoleDescription { get; }
        public string OpponentRoleDescription { get; }
        public string MetaContract { get; }
        public string WritingRules { get; }

        // --- Constructor ---
        public GameDefinition(
            string name,
            string vision,
            string worldDescription,
            string playerRoleDescription,
            string opponentRoleDescription,
            string metaContract,
            string writingRules);

        // --- Static factory: parse YAML string content ---
        // Throws FormatException if YAML is invalid or missing required keys.
        public static GameDefinition LoadFrom(string yamlContent);

        // --- Static property: hardcoded Pinder defaults ---
        // Used when YAML file is unavailable.
        public static GameDefinition PinderDefaults { get; }
    }
}
```

### `SessionSystemPromptBuilder` (Pinder.LlmAdapters)

```csharp
namespace Pinder.LlmAdapters
{
    public static class SessionSystemPromptBuilder
    {
        /// <summary>
        /// Build the full session system prompt from character profiles and game definition.
        /// </summary>
        /// <param name="playerPrompt">
        ///   Player's assembled character system prompt (from CharacterProfile.AssembledSystemPrompt).
        /// </param>
        /// <param name="opponentPrompt">
        ///   Opponent's assembled character system prompt (from CharacterProfile.AssembledSystemPrompt).
        /// </param>
        /// <param name="gameDef">
        ///   Game definition containing vision, world rules, meta contract.
        ///   When null, GameDefinition.PinderDefaults is used.
        /// </param>
        /// <returns>A single string containing the full session system prompt.</returns>
        public static string Build(
            string playerPrompt,
            string opponentPrompt,
            GameDefinition? gameDef = null);
    }
}
```

---

## Input/Output Examples

### GameDefinition.LoadFrom — Valid YAML

**Input** (YAML string):
```yaml
name: "Pinder"
vision: |
  A comedy dating RPG where players are sentient penises
  swiping on a Tinder-like app called Pinder.
world_description: |
  The world of Pinder is absurdist. Characters are anatomical
  beings navigating modern dating culture.
player_role_description: |
  You are the player's character — a sentient being trying to
  secure a date through wit, charm, and questionable decisions.
opponent_role_description: |
  You are the opponent — an NPC on the other side of the chat.
  You have your own personality, boundaries, and interest level.
meta_contract: |
  Never break character. Never acknowledge the game engine.
  Treat [ENGINE] blocks as stage directions, not dialogue.
writing_rules: |
  Write in texting register. Short messages. No essays.
  Match the character's established voice exactly.
```

**Output**: A `GameDefinition` instance where:
- `Name` → `"Pinder"`
- `Vision` → `"A comedy dating RPG where players are sentient penises\nswiping on a Tinder-like app called Pinder.\n"`
- `WorldDescription` → `"The world of Pinder is absurdist. Characters are anatomical\nbeings navigating modern dating culture.\n"`
- (remaining properties populated similarly)

### GameDefinition.PinderDefaults

Returns a `GameDefinition` with all 7 fields populated with hardcoded Pinder-specific creative direction text. The exact content should reflect the game's identity: comedy dating RPG, sentient penises, Tinder-like app, absurdist world. This is the fallback when no YAML file is available.

### SessionSystemPromptBuilder.Build — Full Example

**Input**:
- `playerPrompt`: `"You are Velvet. Lowercase-with-intent. Ironic. Level 7 Veteran..."`
- `opponentPrompt`: `"You are Sable. Fast-talking. Uses omg and 😭. Level 5 Journeyman..."`
- `gameDef`: `GameDefinition.PinderDefaults`

**Output** (string with 5 clearly delineated sections):
```
== GAME VISION ==

A comedy dating RPG where players are sentient penises
swiping on a Tinder-like app called Pinder.
<... rest of vision text ...>

== WORLD RULES ==

The world of Pinder is absurdist. Characters are anatomical
beings navigating modern dating culture.
<... rest of world description ...>

== PLAYER CHARACTER ==

You are Velvet. Lowercase-with-intent. Ironic. Level 7 Veteran...

== OPPONENT CHARACTER ==

You are Sable. Fast-talking. Uses omg and 😭. Level 5 Journeyman...

== META CONTRACT ==

Never break character. Never acknowledge the game engine.
Treat [ENGINE] blocks as stage directions, not dialogue.

Write in texting register. Short messages. No essays.
Match the character's established voice exactly.
```

The section headers use `== SECTION NAME ==` format (or similar clear delimiters). The exact delimiter format is implementation choice, but sections must be visually distinct and parseable.

### SessionSystemPromptBuilder.Build — Null GameDefinition

**Input**:
- `playerPrompt`: `"You are Gerald. Anxious. Overthinks everything..."`
- `opponentPrompt`: `"You are Brick. Blunt. Minimal words..."`
- `gameDef`: `null`

**Output**: Same 5-section structure, but game vision / world rules / meta contract / writing rules sourced from `GameDefinition.PinderDefaults`. Player and opponent character sections contain the provided prompt strings verbatim.

---

## Acceptance Criteria

### AC1: GameDefinition class with all fields

`GameDefinition` must be a `sealed class` in the `Pinder.LlmAdapters` namespace with exactly 7 read-only string properties: `Name`, `Vision`, `WorldDescription`, `PlayerRoleDescription`, `OpponentRoleDescription`, `MetaContract`, `WritingRules`. The constructor accepts all 7 as required `string` parameters (no nulls allowed — throw `ArgumentNullException` for any null argument).

### AC2: GameDefinition.LoadFrom(yamlContent) parses YAML

`LoadFrom` accepts a `string` containing YAML content (not a file path). It parses the YAML and returns a `GameDefinition` populated from these top-level keys:
- `name` → `Name`
- `vision` → `Vision`
- `world_description` → `WorldDescription`
- `player_role_description` → `PlayerRoleDescription`
- `opponent_role_description` → `OpponentRoleDescription`
- `meta_contract` → `MetaContract`
- `writing_rules` → `WritingRules`

YAML parsing requires a library. The architect decision is to add `YamlDotNet 16.3.0` as a `PackageReference` in `Pinder.LlmAdapters.csproj`. This is the same version used by `Pinder.Rules`.

### AC3: GameDefinition.PinderDefaults hardcoded fallback

`PinderDefaults` is a `static` read-only property returning a `GameDefinition` instance with all 7 fields populated with meaningful Pinder-specific content. The content must:
- Identify the game as "Pinder" (Name)
- Describe it as a comedy dating RPG with sentient penises on a dating app (Vision)
- Describe the absurdist world setting (WorldDescription)
- Describe the player's role as the character trying to get a date (PlayerRoleDescription)
- Describe the opponent's role as the NPC with independent personality and boundaries (OpponentRoleDescription)
- Specify immersion rules: never break character, treat [ENGINE] blocks as stage directions (MetaContract)
- Specify texting register, short messages, character voice fidelity (WritingRules)

This is a genuine creative direction fallback, not placeholder text. It must be usable in production playtests.

### AC4: SessionSystemPromptBuilder.Build produces system prompt with all 5 sections

`Build` returns a single string containing exactly 5 sections in this order:
1. **GAME VISION** — sourced from `gameDef.Vision`
2. **WORLD RULES** — sourced from `gameDef.WorldDescription`
3. **PLAYER CHARACTER** — the `playerPrompt` string passed verbatim
4. **OPPONENT CHARACTER** — the `opponentPrompt` string passed verbatim
5. **META CONTRACT** — sourced from `gameDef.MetaContract` concatenated with `gameDef.WritingRules`

Each section is separated by clear delimiters (e.g., `== SECTION NAME ==` headers). The character prompts appear verbatim — they are not summarized, trimmed, or modified.

### AC5: Unit test — prompt contains both character names, game vision, world rules

At least one unit test must:
1. Call `SessionSystemPromptBuilder.Build(playerPrompt, opponentPrompt, gameDef)` with known inputs
2. Assert the output contains the player prompt text
3. Assert the output contains the opponent prompt text
4. Assert the output contains the game vision text
5. Assert the output contains the world description text
6. Assert the output contains the meta contract text

### AC6: Build clean

- Solution compiles with zero errors and zero warnings
- All existing tests pass (2979+ tests at time of writing)
- New tests pass
- `YamlDotNet 16.3.0` added to `Pinder.LlmAdapters.csproj` only (NOT to `Pinder.Core.csproj`)

---

## Edge Cases

### Empty strings

- `GameDefinition` constructor: passing `""` (empty string) for any property is allowed — it is not null. The builder will produce a section with an empty body. This is not an error.
- `SessionSystemPromptBuilder.Build`: passing `""` for `playerPrompt` or `opponentPrompt` produces sections with empty bodies. No error.

### Very large prompts

- Character system prompts can be 3,000–6,000 tokens each. `Build` performs simple string concatenation — no truncation. Output may be 10,000+ tokens. This is within Anthropic's 200k context window and acceptable at prototype maturity.

### YAML with extra keys

- `GameDefinition.LoadFrom` must tolerate extra/unknown YAML keys (ignore them). Only the 7 required keys are read.

### YAML with missing keys

- If any of the 7 required keys is missing from YAML, `LoadFrom` throws `FormatException` with a message identifying which key is missing.

### YAML with null values

- If a YAML key maps to `null` (e.g., `vision: ~`), `LoadFrom` throws `FormatException`.

### Whitespace in YAML block scalars

- YAML block scalar `|` preserves newlines. `LoadFrom` must preserve the string as-is from the YAML parser (including trailing newlines from block scalars). No trimming.

### Null gameDef parameter

- `SessionSystemPromptBuilder.Build(player, opponent, null)` uses `GameDefinition.PinderDefaults`. This is documented and intentional.

### Null playerPrompt or opponentPrompt

- Passing `null` for `playerPrompt` or `opponentPrompt` throws `ArgumentNullException`. These are required parameters.

---

## Error Conditions

| Condition | Method | Exception Type | Message Pattern |
|-----------|--------|---------------|-----------------|
| Null YAML content | `GameDefinition.LoadFrom(null)` | `ArgumentNullException` | `"yamlContent"` |
| Unparseable YAML | `GameDefinition.LoadFrom("{{invalid")` | `FormatException` | Contains "YAML" or "parse" |
| Missing required key | `GameDefinition.LoadFrom("name: Pinder\n")` (missing 6 keys) | `FormatException` | Contains the name of the missing key (e.g., `"vision"`) |
| Null value for key | `GameDefinition.LoadFrom("name: Pinder\nvision: ~\n...")` | `FormatException` | Contains `"vision"` |
| Null constructor arg | `new GameDefinition(null, ...)` | `ArgumentNullException` | Parameter name |
| Null playerPrompt | `SessionSystemPromptBuilder.Build(null, "opp")` | `ArgumentNullException` | `"playerPrompt"` |
| Null opponentPrompt | `SessionSystemPromptBuilder.Build("player", null)` | `ArgumentNullException` | `"opponentPrompt"` |

---

## Dependencies

### External Libraries

| Library | Version | Project | Purpose |
|---------|---------|---------|---------|
| YamlDotNet | 16.3.0 | Pinder.LlmAdapters | Parse `game-definition.yaml` in `GameDefinition.LoadFrom` |
| Newtonsoft.Json | 13.0.3 | Pinder.LlmAdapters | Already present (no change) |

### Internal Dependencies

| Component | Relationship |
|-----------|-------------|
| `Pinder.Core.Characters.CharacterProfile` | `AssembledSystemPrompt` property provides the `playerPrompt` and `opponentPrompt` strings passed to `Build` |
| `IStatefulLlmAdapter` (#542) | `GameSession` calls `StartConversation(systemPrompt)` with the output of `Build` |
| `ConversationSession` (#541) | Stores the system prompt produced by `Build` as system blocks |
| `game-definition.yaml` (#545) | External data file parsed by `GameDefinition.LoadFrom` |

### Dependency Chain

This issue is Wave 3 in the sprint ordering:
- **Depends on**: #541 (ConversationSession), #542 (IStatefulLlmAdapter + GameSession wiring)
- **Depended on by**: #544 ([ENGINE] injection blocks)

### Integration Point

After #543 is implemented, the GameSession constructor wiring from #542 (which uses a placeholder `playerPrompt + "\n\n---\n\n" + opponentPrompt` concatenation) should be updated to call:

```csharp
string systemPrompt = SessionSystemPromptBuilder.Build(
    _player.AssembledSystemPrompt,
    _opponent.AssembledSystemPrompt,
    config?.GameDefinition);  // or however GameDefinition is provided
```

Note: `GameSessionConfig` does not currently have a `GameDefinition` field. The integration strategy is for the host (session-runner) to construct the system prompt externally and pass it to `IStatefulLlmAdapter.StartConversation()`, OR for GameSession to accept an optional `GameDefinition` via config. The exact wiring is an implementation detail for the #542/#543 implementer — the key contract is that `SessionSystemPromptBuilder.Build` produces the string and `IStatefulLlmAdapter.StartConversation` receives it.

### Project File Change

`Pinder.LlmAdapters.csproj` gains:
```xml
<PackageReference Include="YamlDotNet" Version="16.3.0" />
```

`Pinder.Core.csproj` is **not modified**. The zero-dependency invariant for Core is preserved.

### Files Changed

| File | Change Type |
|------|-------------|
| `src/Pinder.LlmAdapters/GameDefinition.cs` | New |
| `src/Pinder.LlmAdapters/SessionSystemPromptBuilder.cs` | New |
| `src/Pinder.LlmAdapters/Pinder.LlmAdapters.csproj` | Modified (add YamlDotNet) |
| `tests/Pinder.LlmAdapters.Tests/GameDefinitionTests.cs` | New |
| `tests/Pinder.LlmAdapters.Tests/SessionSystemPromptBuilderTests.cs` | New |
