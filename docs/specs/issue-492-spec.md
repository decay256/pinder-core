# Spec: Issue #492 — LlmPlayerAgent: Sonnet makes option choices based on character fit and narrative moment

**Module**: docs/modules/session-runner.md (create new)

---

## Overview

The existing `LlmPlayerAgent` in `session-runner/` makes option choices using a generic game-strategy prompt with no awareness of the player character's personality, texting style, or conversation history. This issue enhances `LlmPlayerAgent` to receive the player character's system prompt, texting style fragment, and full conversation history, and to incorporate the `ScoringPlayerAgent`'s EV table as advisory input. The result is an LLM-based player agent that makes character-consistent, narratively interesting picks — including bold/callback/combo plays that a pure EV maximizer would reject.

---

## Function Signatures

All types live in the `Pinder.SessionRunner` namespace (project: `session-runner/`).

### LlmPlayerAgent (enhanced constructor)

```csharp
public sealed class LlmPlayerAgent : IPlayerAgent, IDisposable
{
    /// <summary>
    /// Creates an LLM-backed player agent with character context.
    /// </summary>
    /// <param name="options">Anthropic API configuration (API key, model, etc.).</param>
    /// <param name="fallback">Deterministic scoring agent used on LLM failure.</param>
    /// <param name="playerName">Player character display name.</param>
    /// <param name="opponentName">Opponent character display name.</param>
    /// <param name="playerSystemPrompt">
    ///   The player character's full assembled system prompt (from CharacterProfile.AssembledSystemPrompt).
    ///   Empty string if not available. Used to give the LLM the character's personality and voice.
    /// </param>
    /// <param name="playerTextingStyle">
    ///   The player character's texting style fragment (from CharacterProfile.TextingStyleFragment).
    ///   Empty string if not available. Used to reinforce character voice in option selection reasoning.
    /// </param>
    public LlmPlayerAgent(
        AnthropicOptions options,
        ScoringPlayerAgent fallback,
        string playerName = "the player",
        string opponentName = "the opponent",
        string playerSystemPrompt = "",
        string playerTextingStyle = "")
```

The existing `DecideAsync` method signature is **unchanged**:

```csharp
public Task<PlayerDecision> DecideAsync(TurnStart turn, PlayerAgentContext context)
```

### PlayerAgentContext (enhanced with conversation history)

```csharp
public sealed class PlayerAgentContext
{
    // ... all existing properties unchanged ...

    /// <summary>
    /// Conversation history for LLM context. Each tuple is (SenderName, MessageText).
    /// Null if conversation history is not available (e.g., first turn or scoring-only agent).
    /// </summary>
    public IReadOnlyList<(string Sender, string Text)>? ConversationHistory { get; }

    public PlayerAgentContext(
        StatBlock playerStats,
        StatBlock opponentStats,
        int currentInterest,
        InterestState interestState,
        int momentumStreak,
        string[] activeTrapNames,
        int sessionHorniness,
        Dictionary<ShadowStatType, int>? shadowValues,
        int turnNumber,
        StatType? lastStatUsed = null,
        StatType? secondLastStatUsed = null,
        bool honestyAvailableLastTurn = false,
        IReadOnlyList<(string Sender, string Text)>? conversationHistory = null)  // NEW
```

### Program.cs wiring

```csharp
// When constructing LlmPlayerAgent, pass character context:
agent = new LlmPlayerAgent(
    agentOptions,
    new ScoringPlayerAgent(),
    playerName: sable.DisplayName,
    opponentName: brick.DisplayName,
    playerSystemPrompt: sable.AssembledSystemPrompt,
    playerTextingStyle: sable.TextingStyleFragment);  // requires #489

// When constructing PlayerAgentContext each turn, pass conversation history:
var agentContext = new PlayerAgentContext(
    ...,
    conversationHistory: conversationHistory);  // List<(string, string)> built from session turns
```

---

## Input/Output Examples

### Example 1: Character-aware pick with narrative reasoning

**Input TurnStart.Options:**

| Index | Stat | IntendedText | Bonuses |
|-------|------|-------------|---------|
| 0 (A) | Charm +3 | "hey, you come here often?" | — |
| 1 (B) | Wit +5 | "your bio says 'adventurous' but your photos say 'ikea showroom'" | 🔗 callback |
| 2 (C) | Honesty +1 | "honestly? i just swiped right because of the hat" | 📖 tell |
| 3 (D) | Chaos +4 | "what if we just skipped the small talk and went straight to existential dread" | ⭐ combo |

**Input PlayerAgentContext:**
- CurrentInterest: 14
- InterestState: Lukewarm
- MomentumStreak: 2
- TurnNumber: 5
- ConversationHistory: 4 prior exchanges

**ScoringAgent EV table (advisory):**
- A: Score 1.2, 60% success, EV +0.4
- B: Score 2.8, 75% success, EV +1.1 ← scoring pick
- C: Score 0.9, 40% success, EV −0.1
- D: Score 2.1, 65% success, EV +0.7

**Expected LLM Response (illustrative):**
```
Looking at this as Sable — Wit is my strongest stat at +5, and option B has a callback
bonus from turn 2. That's mechanically strong. But the conversation has been getting more
real after that last Honesty exchange, and option D with the Chaos combo would fit the
energy shift I've been building. The existential-dread pivot is very on-brand for Sable's
late-night energy, and the combo bonus means +1 interest on success.

Going with the narrative momentum over pure EV.

PICK: D
```

**Output PlayerDecision:**
- OptionIndex: 3
- Reasoning: (full LLM response text above)
- Scores: (from ScoringPlayerAgent — unchanged)

### Example 2: LLM failure → fallback

**Scenario:** Anthropic API returns HTTP 529 (overloaded).

**Output PlayerDecision:**
- OptionIndex: 1 (ScoringPlayerAgent's pick)
- Reasoning: "[LLM fallback: Anthropic API error (529)] Option B: Wit +5, DC 13, 75% success..."
- Scores: (from ScoringPlayerAgent)

---

## Acceptance Criteria

### AC1: `--agent llm` CLI arg activates LlmPlayerAgent

When the session runner is invoked with `--agent llm`, the `LlmPlayerAgent` instance is created and used for all option selection decisions. The existing `--agent scoring` (default) continues to use `ScoringPlayerAgent` exclusively.

**Verification:** Running `dotnet run --project session-runner -- --player velvet --opponent brick --agent llm` uses `LlmPlayerAgent` for every turn's `DecideAsync` call.

### AC2: Reasoning block appears in playtest output

When `LlmPlayerAgent` picks an option, the full `PlayerDecision.Reasoning` string (which contains the LLM's step-by-step reasoning) is displayed in the playtest markdown output. This is already wired in `Program.cs` — the existing reasoning display code works unchanged because `PlayerDecision.Reasoning` is populated by the LLM response text.

**Verification:** The session markdown contains the LLM's reasoning text (not just the scoring agent's formula output) for each turn when `--agent llm` is used.

### AC3: LlmPlayerAgent can pick bold/callback/combo plays that ScoringAgent would reject

The enhanced prompt gives the LLM enough context (character personality, conversation flow, narrative momentum) to sometimes choose options that have lower EV but better character fit or narrative payoff. This means at least once per session, the LLM picks an option where `ScoringPlayerAgent` would have picked a different one.

**Verification:** In a 20-turn session, compare `LlmPlayerAgent.OptionIndex` to `ScoringPlayerAgent.OptionIndex` (available from the `Scores` array — the scoring agent always runs first). At least one divergence must occur.

### AC4: LLM call failure falls back to ScoringPlayerAgent

On any failure during the LLM call (network error, API error, timeout, empty response, unparseable PICK), the agent returns the `ScoringPlayerAgent`'s decision with a reasoning string prefixed by `[LLM fallback: <reason>]`.

**Verification:** Existing fallback behavior is preserved. The `MakeFallbackDecision` pattern continues to work.

### AC5: Build clean, all tests pass

All existing tests (2295+) continue to pass. The `session-runner` project builds without errors or warnings. No changes to `Pinder.Core` game logic.

---

## Enhanced Prompt Structure

The `BuildPrompt` method is reworked to include character context. The prompt sent to Claude has this structure:

### System message (updated)

The system message gains character identity:

```
You are playing as {playerName} in Pinder, a comedy dating RPG.
You are talking to {opponentName}.

{playerSystemPrompt — if non-empty, trimmed/summarized to key personality traits}

{playerTextingStyle — if non-empty, verbatim}

Choose dialogue options that fit {playerName}'s personality and voice.
You also understand game mechanics and consider expected value, but character fit
and narrative moment take priority over pure optimization.
```

If `playerSystemPrompt` and `playerTextingStyle` are both empty, the system message falls back to the existing generic strategic prompt (backward-compatible).

### User message sections (in order)

1. **CONVERSATION SO FAR** — Full conversation history from `PlayerAgentContext.ConversationHistory`. Each entry formatted as `{Sender}: {Text}`. Omitted if null or empty.

2. **CURRENT STATE** — Same as existing: interest, momentum, traps, shadows, turn number.

3. **YOUR OPTIONS** — Same as existing: stat, modifier, DC, need, success %, risk tier, bonus icons, intended text.

4. **SCORING AGENT ADVISORY** — The `ScoringPlayerAgent`'s EV table for all options, formatted as:
   ```
   ## Scoring Agent Advisory (pure EV — use as input, not gospel)
   A) Score: 1.20 | 60% success | EV: +0.40
   B) Score: 2.80 | 75% success | EV: +1.10 ← scorer pick
   C) Score: 0.90 | 40% success | EV: -0.10
   D) Score: 2.10 | 65% success | EV: +0.70
   ```

5. **RULES REMINDER** — Same as existing: success/failure tiers, risk bonus, momentum, bonus icons.

6. **TASK** — Updated instruction:
   ```
   Consider: (1) Which option fits {playerName}'s personality right now?
   (2) What would make the best narrative moment given the conversation so far?
   (3) The scoring agent's EV analysis — diverge when character or story demands it.
   
   Explain your reasoning in 2-4 sentences. Then state your final choice as:
   PICK: [A/B/C/D]
   ```

### ParsePick (unchanged)

The existing `ParsePick` static method is unchanged. It matches the last `PICK: [A/B/C/D]` in the response.

---

## Edge Cases

### Empty conversation history

When `PlayerAgentContext.ConversationHistory` is null or empty (e.g., turn 1), the CONVERSATION SO FAR section is omitted entirely. The prompt still works because game state and options provide sufficient context.

### Empty player system prompt and texting style

When both `playerSystemPrompt` and `playerTextingStyle` are empty strings (e.g., `CharacterProfile.TextingStyleFragment` not yet available because #489 is not merged), the system message falls back to the existing generic strategic prompt. The user message still includes the scoring advisory and conversation history (if available).

### Single option available

When `TurnStart.Options.Length == 1`, the LLM should still be called (it produces reasoning even for forced picks). The scoring advisory shows one entry. `ParsePick` accepts only `PICK: A`.

### Very long conversation history

Conversation history is included in full. At prototype maturity, no truncation is applied. If the prompt exceeds Anthropic's context window, the API returns an error and the fallback triggers.

### LLM picks same option as scorer

This is normal and expected for many turns. The AC3 requirement is "at least once per session" — most turns the LLM may agree with the scorer.

### ScoringPlayerAgent throws

If `_fallback.DecideAsync()` throws (e.g., zero options), the exception propagates — this is correct because it indicates a bug in the caller, not a recoverable LLM error.

---

## Error Conditions

| Error | Cause | Behavior |
|-------|-------|----------|
| `ArgumentNullException` | `options` or `fallback` is null in constructor | Thrown immediately — programming error |
| `ArgumentNullException` | `turn` or `context` is null in `DecideAsync` | Thrown immediately — programming error |
| `InvalidOperationException` | `turn.Options.Length == 0` | Thrown immediately — caller bug |
| `AnthropicApiException` | HTTP 4xx/5xx from Anthropic | Caught → fallback decision with `"Anthropic API error ({statusCode})"` |
| `HttpRequestException` | Network failure | Caught → fallback decision with `"Network error: {message}"` |
| `TaskCanceledException` | Request timeout | Caught → fallback decision with `"Request timed out"` |
| Empty response | Anthropic returns empty/whitespace text | Caught → fallback decision with `"Empty response from LLM"` |
| Unparseable PICK | LLM response doesn't contain valid `PICK: [A-D]` | Caught → fallback decision with `"Could not parse PICK from response"` |
| Any other `Exception` | Unexpected error | Caught → fallback decision with `"Unexpected error: {message}"` |

All error paths return a valid `PlayerDecision` using `ScoringPlayerAgent`'s choice. No error causes the session to abort.

---

## Dependencies

| Dependency | Type | Status |
|------------|------|--------|
| `IPlayerAgent` interface (#346) | Code | Merged — exists in `session-runner/IPlayerAgent.cs` |
| `ScoringPlayerAgent` (#347) | Code | Merged — exists in `session-runner/ScoringPlayerAgent.cs` |
| `PlayerDecision`, `OptionScore`, `PlayerAgentContext` | Code | Merged — exist in `session-runner/` |
| `AnthropicClient`, `AnthropicOptions`, `MessagesRequest`, `MessagesResponse` | Code | Merged — exist in `Pinder.LlmAdapters` |
| `CharacterProfile.TextingStyleFragment` (#489) | Code | **Not yet merged** — `LlmPlayerAgent` must work with empty string fallback until #489 is implemented |
| `CharacterProfile.AssembledSystemPrompt` | Code | Exists — already a property on `CharacterProfile` |
| `TurnStart`, `DialogueOption`, `GameStateSnapshot` | Code | Exist in `Pinder.Core.Conversation` |
| Anthropic Messages API | External service | Required at runtime — `ANTHROPIC_API_KEY` env var |
| `claude-sonnet-4-20250514` model | External service | Default model — configurable via `AnthropicOptions.Model` or `PLAYER_AGENT_MODEL` env var |

### Dependency on #489 (TextingStyleFragment)

Issue #489 adds `CharacterProfile.TextingStyleFragment`. Until #489 is merged, `LlmPlayerAgent` receives an empty string for `playerTextingStyle`. The implementation must handle this gracefully:

- If `playerTextingStyle` is empty/null, omit the texting style section from the system message
- If `playerSystemPrompt` is empty/null, omit the character personality section from the system message
- Both empty → fall back to existing generic system message

This means #492 can be implemented and merged independently of #489, with degraded (but functional) behavior.

---

## Program.cs Wiring Changes

### Constructor wiring

When `--agent llm` is selected, `Program.cs` must pass character context to the `LlmPlayerAgent` constructor:

```csharp
agent = new LlmPlayerAgent(
    agentOptions,
    new ScoringPlayerAgent(),
    playerName: sable.DisplayName,
    opponentName: brick.DisplayName,
    playerSystemPrompt: sable.AssembledSystemPrompt,
    playerTextingStyle: sable.TextingStyleFragment);  // empty string until #489
```

Note: `CharacterProfile` does not yet have `TextingStyleFragment` (added by #489). Until then, pass `""` explicitly or omit the parameter (defaults to `""`).

### Conversation history accumulation

`Program.cs` must accumulate conversation history across turns and pass it to `PlayerAgentContext`:

```csharp
var conversationHistory = new List<(string Sender, string Text)>();

// After each turn resolves (inside the turn loop):
// Add the player's chosen message
conversationHistory.Add((player1, chosenOption.IntendedText));
// Add the opponent's response (from TurnResult)
if (turnResult.OpponentResponse != null)
    conversationHistory.Add((player2, turnResult.OpponentResponse));

// Pass to agent context:
var agentContext = new PlayerAgentContext(
    ...,
    conversationHistory: conversationHistory);
```

---

## Anthropic API Usage

- **Model**: `claude-sonnet-4-20250514` (or value of `PLAYER_AGENT_MODEL` env var)
- **MaxTokens**: 512 (unchanged — reasoning + PICK fits within this)
- **Temperature**: 0.3 (unchanged — low temperature for strategic consistency with some creative variance)
- **System blocks**: Single text block containing the character-aware system message
- **User message**: Single message containing the full prompt (conversation history + state + options + advisory + rules + task)
- **No streaming**: Uses `SendMessagesAsync` (non-streaming)
- **No caching**: Player agent prompts change every turn; caching provides no benefit
