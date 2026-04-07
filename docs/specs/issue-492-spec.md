**Module**: docs/modules/session-runner.md

## Overview

The `LlmPlayerAgent` replaces the purely deterministic scoring logic with an LLM (Anthropic's Claude 3.5 Sonnet) to pick dialogue options during automated playtesting. It combines the deterministic Expected Value (EV) scores from `ScoringPlayerAgent` (used as an advisory input) with the character's persona (system prompt and texting style) and the conversation history. This allows the agent to make "in-character" choices, sometimes picking higher-risk options if they fit the narrative moment, resulting in more realistic and dramatic test sessions.

## Function Signatures

```csharp
namespace Pinder.SessionRunner
{
    public sealed class PlayerAgentContext
    {
        // ... existing properties ...
        
        /// <summary>The player's assembled system prompt defining their persona.</summary>
        public string? PlayerSystemPrompt { get; }
        
        /// <summary>The texting style constraints for the player.</summary>
        public string? TextingStyleFragment { get; }
        
        /// <summary>Formatted conversation history up to the current turn.</summary>
        public string? ConversationHistory { get; }
        
        // Constructor updated to accept the new optional parameters
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
            string? playerSystemPrompt = null,
            string? textingStyleFragment = null,
            string? conversationHistory = null);
    }

    public sealed class LlmPlayerAgent : IPlayerAgent, IDisposable
    {
        public LlmPlayerAgent(AnthropicOptions options, ScoringPlayerAgent fallback, string playerName = "the player", string opponentName = "the opponent");
        public Task<PlayerDecision> DecideAsync(TurnStart turn, PlayerAgentContext context);
        internal string BuildPrompt(TurnStart turn, PlayerAgentContext context);
        internal static int? ParsePick(string responseText, int optionCount);
    }
}
```

## Input/Output Examples

**Input (TurnStart & Context):**
- **Option A:** "Wow, okay." (Safe, +1 EV)
- **Option B:** "I will literally destroy you." (Bold, -0.5 EV)
- **Conversation History:** "Sable: You're too quiet."
- **Player System Prompt:** "You are Velvet..."
- **Texting Style:** "lowercase only, aggressive."

**Output (PlayerDecision):**
- **OptionIndex:** 1
- **Reasoning:** "While Option A is safer mechanically (EV +1), Option B fits Velvet's aggressive texting style perfectly in response to Sable's provocation. Picking B."
- **Scores:** `[0.8, -0.5]` (passed through from the fallback scoring agent)

## Acceptance Criteria

- **AC1:** Given `--agent llm` CLI arg, when session runs, then `LlmPlayerAgent` is used for option selection.
- **AC2:** Given `LlmPlayerAgent` picks an option, when the result is logged, then a reasoning block appears in the playtest output explaining the choice based on mechanics and persona.
- **AC3:** Given options with different risk profiles, when `LlmPlayerAgent` decides, then it can pick bold/callback/combo plays that ScoringAgent would reject due to risk (at least once per session) to fit the character.
- **AC4:** Given an LLM call fails (e.g., timeout, empty, invalid format), when the fallback triggers, then `ScoringPlayerAgent` is used to make the decision.
- **AC5:** Build clean, all tests pass.

## Edge Cases

1. **Empty Conversation History:** On turn 1, the conversation history is empty. The agent should omit the `## Conversation So Far` section entirely rather than passing an empty section header.
2. **Unbounded History Length:** The conversation history grows linearly. For the prototype, passing the full history is acceptable. (Before MVP, a token budget or trimming mechanism must be introduced).
3. **Missing Pick Format:** The LLM might generate reasoning but fail to output `PICK: [X]`. The agent must gracefully fall back to the scoring agent.
4. **Invalid Option Selected:** The LLM outputs `PICK: [E]` when only options A through D are available. The agent must catch this bounds error and fall back to the scoring agent.
5. **No Options Available:** Calling `DecideAsync` with 0 options throws `InvalidOperationException`.

## Error Conditions

- **API/Network Failures:** `HttpRequestException`, `TaskCanceledException`, or `AnthropicApiException` should be caught, returning a fallback decision from the `ScoringPlayerAgent` and logging the error reason in the output.
- **Missing Context Properties:** If `PlayerSystemPrompt` or `TextingStyleFragment` are null or empty, the agent should degrade gracefully and rely on the generic rules and options provided.

## Dependencies

- Depends on **#346** (IPlayerAgent interface and session runner foundations)
- Depends on **#489** (Voice distinctness - requires `TextingStyleFragment` to be available)
- Requires `AnthropicClient` from `Pinder.LlmAdapters.Anthropic` for API communication.
