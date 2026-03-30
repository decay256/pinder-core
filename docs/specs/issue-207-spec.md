# Specification: Issue #207 — SessionDocumentBuilder + PromptTemplates

## Overview

`SessionDocumentBuilder` is a pure static utility class that formats conversation history and game state into structured prompt strings for each of the four `ILlmAdapter` method calls. `PromptTemplates` holds the static `const string` instruction templates sourced from the game design document (character-construction.md §3.2–3.8). Together they transform raw game data into the exact text sent to the Anthropic Claude API. A helper class `CacheBlockBuilder` constructs `ContentBlock[]` arrays with `cache_control: ephemeral` annotations for Anthropic prompt caching.

All three classes live in the `Pinder.LlmAdapters` project. They are pure string/data construction — no I/O, no state, no async. All methods must complete in under 1ms.

---

## Function Signatures

### SessionDocumentBuilder

**Namespace:** `Pinder.LlmAdapters`  
**File:** `src/Pinder.LlmAdapters/SessionDocumentBuilder.cs`

```csharp
public static class SessionDocumentBuilder
{
    /// <summary>
    /// Builds the user-message content for GetDialogueOptionsAsync (§3.2).
    /// </summary>
    public static string BuildDialogueOptionsPrompt(
        IReadOnlyList<(string Sender, string Text)> conversationHistory,
        string opponentLastMessage,
        string[] activeTraps,
        int currentInterest,
        int currentTurn,
        string playerName,
        string opponentName);

    /// <summary>
    /// Builds the user-message content for DeliverMessageAsync (§3.3 success / §3.4 failure).
    /// </summary>
    public static string BuildDeliveryPrompt(
        IReadOnlyList<(string Sender, string Text)> conversationHistory,
        DialogueOption chosenOption,
        FailureTier outcome,
        int beatDcBy,
        string[]? activeTrapInstructions);

    /// <summary>
    /// Builds the user-message content for GetOpponentResponseAsync (§3.5).
    /// </summary>
    public static string BuildOpponentPrompt(
        IReadOnlyList<(string Sender, string Text)> conversationHistory,
        string playerDeliveredMessage,
        int interestBefore,
        int interestAfter,
        double responseDelayMinutes,
        string[]? activeTrapInstructions);

    /// <summary>
    /// Builds the user-message content for GetInterestChangeBeatAsync (§3.8).
    /// </summary>
    public static string BuildInterestChangeBeatPrompt(
        string opponentName,
        int interestBefore,
        int interestAfter,
        InterestState newState);
}
```

**Required imports (from `Pinder.Core`):**
- `Pinder.Core.Conversation.DialogueOption`
- `Pinder.Core.Conversation.InterestState`
- `Pinder.Core.Rolls.FailureTier`

### PromptTemplates

**Namespace:** `Pinder.LlmAdapters`  
**File:** `src/Pinder.LlmAdapters/PromptTemplates.cs`

```csharp
public static class PromptTemplates
{
    public const string DialogueOptionsInstruction;    // §3.2
    public const string SuccessDeliveryInstruction;    // §3.3
    public const string FailureDeliveryInstruction;    // §3.4
    public const string OpponentResponseInstruction;   // §3.5 (includes [SIGNALS] block)
    public const string InterestBeatInstruction;       // §3.8
}
```

### CacheBlockBuilder

**Namespace:** `Pinder.LlmAdapters.Anthropic`  
**File:** `src/Pinder.LlmAdapters/Anthropic/CacheBlockBuilder.cs`

```csharp
public static class CacheBlockBuilder
{
    /// <summary>
    /// Builds system blocks with both character prompts cached.
    /// Used by dialogue options and delivery calls.
    /// </summary>
    public static ContentBlock[] BuildCachedSystemBlocks(
        string playerPrompt, string opponentPrompt);

    /// <summary>
    /// Builds system blocks with only the opponent prompt cached.
    /// Used by opponent response calls.
    /// </summary>
    public static ContentBlock[] BuildOpponentOnlySystemBlocks(
        string opponentPrompt);
}
```

`ContentBlock` is a DTO defined by issue #205 (project scaffold). It must have at minimum:
- `string Type` (always `"text"`)
- `string Text`
- `CacheControl? CacheControl` where `CacheControl` has `string Type` (value `"ephemeral"`)

---

## Input/Output Examples

### Example 1: BuildDialogueOptionsPrompt — Turn 1, empty history

**Input:**
```
conversationHistory: [] (empty list)
opponentLastMessage: ""
activeTraps: [] (empty array)
currentInterest: 10
currentTurn: 1
playerName: "GERALD_42"
opponentName: "VELVET"
```

**Output (string):**
```
CONVERSATION HISTORY
[CONVERSATION_START]
[CURRENT_TURN]

OPPONENT'S LAST MESSAGE
""

GAME STATE
- Active traps: none

YOUR TASK
Generate exactly 4 dialogue options for GERALD_42.
{...rest of DialogueOptionsInstruction template with placeholders filled...}
```

### Example 2: BuildDialogueOptionsPrompt — Turn 3, 4 history entries

**Input:**
```
conversationHistory: [
  ("GERALD_42", "Hey, you come here often?"),
  ("VELVET", "Only when I want to regret my choices"),
  ("GERALD_42", "Same, honestly. I'm Gerald."),
  ("VELVET", "I can see that from your name.")
]
opponentLastMessage: "I can see that from your name."
activeTraps: ["Cringe"]
currentInterest: 12
currentTurn: 3
playerName: "GERALD_42"
opponentName: "VELVET"
```

**Output (conversation history section):**
```
CONVERSATION HISTORY
[CONVERSATION_START]
[T1|PLAYER|GERALD_42] "Hey, you come here often?"
[T1|OPPONENT|VELVET] "Only when I want to regret my choices"
[T2|PLAYER|GERALD_42] "Same, honestly. I'm Gerald."
[T2|OPPONENT|VELVET] "I can see that from your name."
[CURRENT_TURN]

OPPONENT'S LAST MESSAGE
"I can see that from your name."

GAME STATE
- Active traps: Cringe
{...rest of template...}
```

**History formatting rules:**
- Each pair of entries (player message + opponent message) constitutes one turn.
- Turn numbers are 1-indexed and increment per pair.
- Entry `i` at index 0 is turn 1 player, index 1 is turn 1 opponent, index 2 is turn 2 player, etc.
- The sender name from the tuple determines `PLAYER` vs `OPPONENT` label. The first sender in history is always the player.
- Marker format: `[T{turn}|{PLAYER or OPPONENT}|{name}] "{text}"`
- Text is wrapped in double quotes.

### Example 3: BuildDeliveryPrompt — Success

**Input:**
```
conversationHistory: (2 entries as above)
chosenOption: DialogueOption(StatType.Wit, "I'd argue with that but I'm too busy being intimidated", callbackTurnNumber: null, comboName: null, hasTellBonus: false, hasWeaknessWindow: false)
outcome: FailureTier.None
beatDcBy: 4
activeTrapInstructions: null
```

**Output (contains):**
```
The player chose option: "I'd argue with that but I'm too busy being intimidated"
Stat used: WIT
They rolled SUCCESS — beat DC by 4.

{SuccessDeliveryInstruction template text}
```

### Example 4: BuildDeliveryPrompt — Failure (Misfire)

**Input:**
```
chosenOption: DialogueOption(StatType.Charm, "You seem really interesting, tell me more")
outcome: FailureTier.Misfire
beatDcBy: -4
activeTrapInstructions: ["You are aware of how you're coming across, which is making it worse."]
```

**Output (contains):**
```
The player chose option: "You seem really interesting, tell me more"
Stat used: CHARM
They rolled FAILED — missed DC by 4.
Failure tier: MISFIRE

{FailureDeliveryInstruction template text with Misfire-specific tier instruction}

Active trap instructions:
You are aware of how you're coming across, which is making it worse.
```

### Example 5: BuildOpponentPrompt

**Input:**
```
conversationHistory: (4 entries — 2 full turns)
playerDeliveredMessage: "Same, honestly. I'm Gerald."
interestBefore: 10
interestAfter: 12
responseDelayMinutes: 3.5
activeTrapInstructions: null
```

**Output (contains):**
```
CONVERSATION HISTORY
[CONVERSATION_START]
[T1|PLAYER|GERALD_42] "Hey, you come here often?"
[T1|OPPONENT|VELVET] "Only when I want to regret my choices"
[CURRENT_TURN]

PLAYER'S LAST MESSAGE
"Same, honestly. I'm Gerald."

INTEREST CHANGE
Interest moved from 10 to 12 (+2).
Current Interest: 12/25

RESPONSE TIMING
Your reply arrives in approximately 3.5 minutes.

{OpponentResponseInstruction template text including interest behaviour block and [SIGNALS] instruction}
```

### Example 6: BuildInterestChangeBeatPrompt

**Input:**
```
opponentName: "VELVET"
interestBefore: 14
interestAfter: 16
newState: InterestState.VeryIntoIt
```

**Output:**
```
VELVET's Interest just moved from 14 to 16.

{InterestBeatInstruction text — threshold-crossed-above-15 variant}

Output only the message or gesture text.
```

### Example 7: BuildCachedSystemBlocks

**Input:**
```
playerPrompt: "You are playing the role of Gerald_42, a sentient penis on the dating app Pinder..."
opponentPrompt: "You are playing the role of Velvet, a sentient penis on the dating app Pinder..."
```

**Output:**
```csharp
ContentBlock[] {
  [0] = { Type = "text", Text = playerPrompt, CacheControl = { Type = "ephemeral" } },
  [1] = { Type = "text", Text = opponentPrompt, CacheControl = { Type = "ephemeral" } }
}
```

### Example 8: BuildOpponentOnlySystemBlocks

**Input:**
```
opponentPrompt: "You are playing the role of Velvet..."
```

**Output:**
```csharp
ContentBlock[] {
  [0] = { Type = "text", Text = opponentPrompt, CacheControl = { Type = "ephemeral" } }
}
```

---

## Acceptance Criteria

### AC1: SessionDocumentBuilder with all 4 builder methods

`SessionDocumentBuilder` must be a `public static class` in namespace `Pinder.LlmAdapters` with all four methods as specified in the Function Signatures section above. Each method must:
- Accept the exact parameter types shown
- Return a `string`
- Be pure (no side effects, no I/O, no state)

### AC2: Full conversation history formatted with [T{n}|PLAYER|name] markers (never truncated)

The conversation history formatting must follow these rules exactly:

1. History always begins with `[CONVERSATION_START]` on its own line.
2. Each history entry is formatted as: `[T{turn}|{role}|{name}] "{text}"`
   - `turn` is 1-indexed, incrementing every 2 entries (indices 0–1 = turn 1, 2–3 = turn 2, etc.)
   - `role` is `PLAYER` if the sender matches `playerName`, otherwise `OPPONENT`
   - `name` is the sender's display name (from the tuple, or from method params)
   - `text` is the message text, wrapped in double quotes
3. History always ends with `[CURRENT_TURN]` on its own line.
4. Empty history produces exactly: `[CONVERSATION_START]\n[CURRENT_TURN]`
5. **History is NEVER truncated.** All entries must be present regardless of count. This is critical because callbacks can reference the opener (+3 bonus) and the model needs the full conversational arc.
6. The `BuildOpponentPrompt` method formats history up to but NOT including the current player message (since that is supplied separately as `playerDeliveredMessage`).

### AC3: PromptTemplates with all 5 instruction templates sourced from character-construction.md

`PromptTemplates` must be a `public static class` in namespace `Pinder.LlmAdapters` with five `public const string` fields:

1. **`DialogueOptionsInstruction`** — sourced from §3.2. Must instruct the LLM to generate exactly 4 dialogue options, each tagged with `[STAT: X]`, `[CALLBACK: turn_N or none]`, `[COMBO: name or none]`, `[TELL_BONUS: yes/no]` metadata. Must include instructions about varying tone/risk, callbacks, combos, and considering the opponent's profile.

2. **`SuccessDeliveryInstruction`** — sourced from §3.3. Must include the three success tiers:
   - Clean success (margin 1–5): deliver essentially as written
   - Strong success (margin 6–10): add flourish
   - Critical success / Nat 20: peak delivery
   Must instruct output to be only the message text.

3. **`FailureDeliveryInstruction`** — sourced from §3.4. Must include the failure principle ("corrupt the CONTENT, not the delivery") and all five tier-specific instructions:
   - Fumble (miss 1–2): slight fumble, one awkward word choice
   - Misfire (miss 3–5): message goes sideways
   - TropeTrap (miss 6–9): stat-specific social trope failure
   - Catastrophe (miss 10+): worst impulse hijacks message
   - Legendary (Nat 1): maximum humiliation
   Must instruct output to be only the message text.

4. **`OpponentResponseInstruction`** — sourced from §3.5. Must include interest behaviour blocks (dynamically selected based on interest range), response timing instruction, and the `[SIGNALS]` block instruction per #214. The signals instruction must tell the LLM to optionally include:
   ```
   [RESPONSE]
   "actual opponent message text"

   [SIGNALS]
   TELL: {STAT} ({description})
   WEAKNESS: {STAT} -{reduction} ({description})
   ```
   Signals should appear approximately 30–40% of the time (controlled by prompt wording like "occasionally" or "when it feels natural").

5. **`InterestBeatInstruction`** — sourced from §3.8. Must include threshold-specific variants:
   - Crossed above 15: becoming more invested
   - Crossed below 8: cooling signal
   - Reached 25 (DateSecured): suggest meeting up
   - Reached 0 (Unmatched): unmatching message

Templates use `{placeholder}` tokens that `SessionDocumentBuilder` fills at call time.

### AC4: BuildCachedSystemBlocks helper with cache_control: ephemeral on both prompts

`CacheBlockBuilder` must be a `public static class` in namespace `Pinder.LlmAdapters.Anthropic` with two methods:

1. `BuildCachedSystemBlocks(string playerPrompt, string opponentPrompt)` returns `ContentBlock[]` with exactly 2 elements, both having `CacheControl.Type = "ephemeral"`.
2. `BuildOpponentOnlySystemBlocks(string opponentPrompt)` returns `ContentBlock[]` with exactly 1 element having `CacheControl.Type = "ephemeral"`.

### AC5: Unit tests

Tests at `tests/Pinder.LlmAdapters.Tests/SessionDocumentBuilderTests.cs` must verify:
1. Empty history → output contains `[CONVERSATION_START]\n[CURRENT_TURN]` with no entries between.
2. 3-turn history (6 entries) → markers correctly show `[T1|...]`, `[T2|...]`, `[T3|...]`.
3. 8-turn history (16 entries) → all 8 turns present, no truncation.
4. `BuildCachedSystemBlocks` returns 2 blocks both with `cache_control` set to ephemeral.
5. `BuildOpponentOnlySystemBlocks` returns 1 block with `cache_control` set to ephemeral.

### AC6: Build clean

The `Pinder.LlmAdapters` project must compile without errors or warnings under `dotnet build`. All existing Pinder.Core tests (1118+) must continue to pass.

---

## Edge Cases

### Conversation History

- **Empty history:** `conversationHistory` is an empty list. Output: `[CONVERSATION_START]\n[CURRENT_TURN]` — no message lines.
- **Odd number of entries:** If history has an odd number of entries (e.g., player spoke but opponent hasn't replied yet), the last entry is a lone player message. The turn number for it is `(index / 2) + 1`. Format it normally.
- **Single entry (turn 1 player only):** `[T1|PLAYER|name] "text"` followed by `[CURRENT_TURN]`.
- **Very long history (e.g., 20+ turns, 40+ entries):** Must never truncate. All entries are present. No sliding window. No summarization.
- **Messages containing double quotes:** The text should be wrapped in double quotes. Interior quotes should be included as-is (no escaping) — the LLM handles them naturally.
- **Messages containing newlines:** Preserve them within the quoted text. Each history entry marker starts on its own line.
- **Empty message text:** Format as `[T{n}|PLAYER|name] ""`.

### Names

- **Names with spaces:** Use as-is in markers: `[T1|PLAYER|Big Gerald]`.
- **Names with special characters:** Use as-is. No sanitization.
- **`playerName` matches no sender in history:** This shouldn't happen in normal usage but if it does, all entries would be labeled `OPPONENT`. No crash.

### Active Traps

- **Empty traps array:** Output `Active traps: none` (or omit the traps line).
- **Multiple traps:** Join with comma: `Active traps: Cringe, Spiral, Overexplain`.
- **Null trap instructions (in delivery/opponent prompts):** Omit the trap instructions section entirely.

### Interest Values

- **Interest at boundaries (0, 25):** Format normally. The interest state enum handles the semantics.
- **Interest delta of 0 (no change):** `interestBefore == interestAfter`. Still format: "Interest moved from 10 to 10 (+0)."

### BuildOpponentPrompt — responseDelayMinutes

- **Very small delay (< 1 minute):** Format as fractional minutes or convert to a human-readable form like "less than 1 minute" or "0.5 minutes".
- **Very large delay (> 1440 = 24 hours):** Format normally. The evaluator handles the semantics.

### CacheBlockBuilder

- **Empty prompt strings:** Return blocks with empty `Text`. Do not throw — the caller is responsible for passing valid prompts.
- **Null prompt strings:** Throw `ArgumentNullException`. Prompts must not be null.

### FailureTier in BuildDeliveryPrompt

- **`FailureTier.None` (success):** Use `SuccessDeliveryInstruction`. Do not include failure tier instructions.
- **Any other FailureTier value:** Use `FailureDeliveryInstruction` with the tier-specific sub-instruction.

---

## Error Conditions

| Condition | Expected Behavior |
|-----------|-------------------|
| `conversationHistory` is `null` | Throw `ArgumentNullException` |
| `playerName` is `null` or empty | Throw `ArgumentNullException` (or `ArgumentException`) |
| `opponentName` is `null` or empty | Throw `ArgumentNullException` (or `ArgumentException`) |
| `opponentLastMessage` is `null` | Throw `ArgumentNullException` |
| `chosenOption` is `null` (in BuildDeliveryPrompt) | Throw `ArgumentNullException` |
| `playerDeliveredMessage` is `null` (in BuildOpponentPrompt) | Throw `ArgumentNullException` |
| `opponentName` is `null` (in BuildInterestChangeBeatPrompt) | Throw `ArgumentNullException` |
| `playerPrompt` or `opponentPrompt` is `null` (in CacheBlockBuilder) | Throw `ArgumentNullException` |
| `activeTraps` is `null` (in BuildDialogueOptionsPrompt) | Throw `ArgumentNullException` |
| `activeTrapInstructions` is `null` (in delivery/opponent prompts) | Allowed — omit trap instructions section |
| Unknown `FailureTier` enum value | Default to generic failure instruction (do not throw) |
| Unknown `InterestState` enum value | Default to generic interest beat (do not throw) |

All methods are synchronous and pure. They never perform I/O, never throw on valid inputs, and never allocate external resources.

---

## Dependencies

### Build Dependencies

- **Pinder.Core** — for types: `DialogueOption`, `FailureTier`, `InterestState`, `StatType`
- **Newtonsoft.Json** — only if `ContentBlock` uses Json attributes (defined in #205). `SessionDocumentBuilder` and `PromptTemplates` themselves do NOT need Newtonsoft directly.
- **Issue #205** — project scaffold must exist. `ContentBlock`, `CacheControl` DTO types must be defined.

### Runtime Dependencies

- None. All methods are pure string construction.

### Consumed By

- **Issue #208** (`AnthropicLlmAdapter`) — calls all four `SessionDocumentBuilder` methods and reads `PromptTemplates` constants. Uses `CacheBlockBuilder` to construct system blocks for API calls.

### Design Document Source

- Template content is sourced from: `design/systems/character-construction.md` §3.2–3.8
- If this file is not available in-repo, the implementer should transcribe the templates from the external design repo at `/root/.openclaw/agents-extra/pinder/design/systems/character-construction.md`.

---

## Conversation History Formatting — Detailed Specification

The conversation history formatter is shared across all four builder methods (though `BuildInterestChangeBeatPrompt` does not take history). The formatting logic should be extracted into a private helper method.

### Algorithm

```
Given: IReadOnlyList<(string Sender, string Text)> history, string playerName, string opponentName

1. Emit "[CONVERSATION_START]\n"
2. For each entry at index i:
   a. turn = (i / 2) + 1
   b. role = (entry.Sender == playerName) ? "PLAYER" : "OPPONENT"
   c. name = entry.Sender
   d. Emit "[T{turn}|{role}|{name}] \"{entry.Text}\"\n"
3. Emit "[CURRENT_TURN]\n"
```

### Name resolution for role

The `Sender` field in each history tuple is compared to `playerName` to determine the role. If the sender matches `playerName`, it's `PLAYER`; otherwise it's `OPPONENT`. The comparison should be ordinal (exact match, case-sensitive).

---

## Interest Behaviour Block Selection (for OpponentResponseInstruction)

The opponent response prompt must include a dynamically selected interest behaviour block. Selection is based on `interestAfter`:

| Interest Range | Behaviour Block |
|---|---|
| 17–20 | "You are very interested. Replies come quickly. Tone is warmer, more playful. You might volunteer personal information." |
| 13–16 | "You are engaged. Normal pacing. Responsive but not eager. You're seeing where this goes." |
| 9–12 | "You are lukewarm. Taking your time. Replies are functional. You might test them a little." |
| 5–8 | "You are cooling. Short replies. A little dry. You're not sold. One or two good messages could change that." |
| 1–4 | "You are disengaged. Minimal effort. You might send a closing signal or go quiet." |
| 0 | "You have lost all interest. You are unmatching." |
| 21–25 | "You are extremely interested. You're looking for excuses to keep talking. The date is basically happening." |

These blocks are selected at runtime by `SessionDocumentBuilder.BuildOpponentPrompt()` based on `interestAfter`, not stored in `PromptTemplates`.

---

## [SIGNALS] Block Specification (in OpponentResponseInstruction)

The `OpponentResponseInstruction` template must include instructions for optional signal generation. The LLM outputs:

```
[RESPONSE]
"the opponent's actual message text"

[SIGNALS]
TELL: CHARM (she laughed a little too hard at the charm attempt — she's susceptible)
WEAKNESS: WIT -2 (she made a self-deprecating joke about her own cleverness)
```

**Rules:**
- `[SIGNALS]` section is optional — the LLM includes it only when natural (~30–40% of turns).
- `TELL:` line format: `TELL: {STAT_NAME} ({description})` where `STAT_NAME` is one of: `CHARM`, `RIZZ`, `HONESTY`, `CHAOS`, `WIT`, `SELF_AWARENESS`.
- `WEAKNESS:` line format: `WEAKNESS: {STAT_NAME} -{reduction} ({description})` where `reduction` is `2` or `3`.
- Both lines are independently optional within a `[SIGNALS]` block.
- Parsing of signals is NOT this component's responsibility — that belongs to #208 (`AnthropicLlmAdapter`). This component only includes the instruction text telling the LLM how to format them.

---

## PromptTemplates Content Reference

The exact template text should be sourced from `character-construction.md`. Below are the section references and key content that MUST be present in each template:

### DialogueOptionsInstruction (§3.2)
Must instruct to generate exactly 4 options with metadata tags. Key content:
- Each option tagged with one of: `CHARM`, `RIZZ`, `HONESTY`, `CHAOS`, `WIT`, `SELF_AWARENESS`
- Options show intended message before roll outcome
- Must vary in tone and risk (at least one safe, one bold)
- If callback exists, 1–2 options should reference earlier topic
- If combo available, one option should use completing stat
- Metadata format: `[STAT: X] [CALLBACK: turn_N or none] [COMBO: name or none] [TELL_BONUS: yes/no]`
- Options should be concise (1–3 sentences)

### SuccessDeliveryInstruction (§3.3)
Must include three success tiers and instruct output-only-message-text.

### FailureDeliveryInstruction (§3.4)
Must include the "corrupt the CONTENT not the delivery" principle, all five tier instructions, and support for active trap injection. Uses `{placeholder}` tokens for `intended_message`, `stat`, `miss_margin`, `tier`, `tier_instruction`, and `active_trap_llm_instructions`.

### OpponentResponseInstruction (§3.5)
Must include interest change info, response timing, interest behaviour block, the generate-your-next-message instruction, and the `[SIGNALS]` output format instruction.

### InterestBeatInstruction (§3.8)
Must include threshold-conditional variants (above 15, below 8, reached 25, reached 0). Uses `{placeholder}` tokens for `opponent_name`, `interest_before`, `interest_after`.
