**Module**: docs/modules/session-runner.md

## Overview

This feature adds a pre-game LLM-generated character analysis at the start of an automated playtest session. It leverages the Anthropic API in a zero-shot manner to analyze the player and opponent's stats, shadow traits, and bios alongside the matchup's DC table, providing a brief strategic summary of the matchup, best lanes, and shadow risks to give playtesters immediate strategic context.

## Function Signatures

```csharp
// In session-runner/MatchupAnalyzer.cs
public sealed class MatchupAnalyzer
{
    public MatchupAnalyzer(AnthropicOptions options, HttpClient? httpClient = null);
    public Task<string> AnalyzeAsync(CharacterProfile player, CharacterProfile opponent);
}
```

## Input/Output Examples

**Input:**
`AnalyzeAsync(geraldProfile, velvetProfile)` called within `Program.cs`.

**Output:**
```markdown
**Gerald_42** (Level 5, Peacock/Nice Guy): Charm is his strongest lane (+10 vs DC 19 = 60%). 
Madness 5 means Charm options carry a slightly uncanny quality. Denial 2 is low — Honesty could be attempted but DC 23 makes it 5% base.
Shadow risk: Fixation is at 4 — three consecutive same-stat picks will trigger Fixation growth.

**Velvet_Void** (Level 7, Philosopher/Slow Fader): Her SA+12 walls Gerald's Charm at DC 19 (60%). 
Wit is the only lane Gerald can use with her Wit+9 defense (DC 14, 85%). 
Dread 6 (T1) means her wit options will carry melancholy undertones.

**Prediction:** Gerald's best strategy is Wit-heavy. If he falls back to Charm spam, Fixation will grow and Velvet will test harder as interest stagnates.
```

*(Note: The output is injected under a `## Matchup Analysis` header in `Program.cs`)*

## Acceptance Criteria

### Analysis appears before Turn 1 header
In `session-runner/Program.cs`, the output of `MatchupAnalyzer.AnalyzeAsync` must be printed to the session log output after the Characters section, but before Turn 1 commences, under the header `## Matchup Analysis`. This call must happen exactly once per session.

### Covers: best lane, shadow risks, matchup prediction
The system prompt passed to the LLM must explicitly ask for the characters' best lanes (factoring in the opponent's defense stats and DC table), their shadow state implications/risks, and a strategic prediction of the matchup. The DC table and both character profiles must be included in the LLM context.

### ~3-4 sentences per character
The system prompt must explicitly limit the response length to approximately 3 to 4 sentences per character, plus a short prediction block, to keep the analysis brief and readable (~100-150 tokens).

### Build clean
The solution must compile successfully without warnings, and all existing and new tests must pass.

## Edge Cases

- **Missing Bio or Fields**: If a `CharacterProfile` has an empty bio or missing archetype, the prompt should still format properly and the LLM should infer based on the raw stats.
- **Identical Characters**: If the player and opponent are the same character build (e.g., mirror match), the analyzer should still provide valid tactical insights based on the stats vs defense.

## Error Conditions

- **API/Network Failure**: If `AnthropicClient.SendMessagesAsync` throws an `AnthropicApiException`, `HttpRequestException`, or times out, the `AnalyzeAsync` method should catch the exception and return a graceful fallback string (e.g., `*Matchup analysis unavailable due to API error.*`) so that the playtest session does not abruptly crash before Turn 1.

## Dependencies

- **`Pinder.LlmAdapters.Anthropic.AnthropicClient`**: Used to construct the underlying API requests inside `MatchupAnalyzer`.
- **`Pinder.Core.Characters.CharacterProfile`**: Passed to the analyzer to provide the raw stats, shadow traits, and bio data.
- **`session-runner/Program.cs`**: Integrates `MatchupAnalyzer` by instantiating it, invoking it, and printing the output.
