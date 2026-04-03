# Spec: LlmPlayerAgent — Anthropic-Backed Player Agent

**Issue:** #348 — Player agent: LlmPlayerAgent — Sonnet/Opus plays the game with full rules context  
**Module:** docs/modules/session-runner.md (create new)

---

## Overview

`LlmPlayerAgent` is an LLM-backed player agent that sends the full game state and rules context to Anthropic's Claude API and parses a strategic pick from the response. It implements `IPlayerAgent` (defined in #346) and lives in the `session-runner/` project (per vision concern #355). On any failure — API error, parse error, timeout — it falls back to the deterministic `ScoringPlayerAgent` (#347), guaranteeing every call to `DecideAsync` returns a valid decision. The LLM's full reasoning text is captured in `PlayerDecision.Reasoning` for display in session playtest output (#351).

---

## Function Signatures

All types live in the `session-runner/` project. The project targets `net8.0` with **LangVersion 8.0** (no records, no init-only setters).

### LlmPlayerAgent

```csharp
// File: session-runner/LlmPlayerAgent.cs

using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.LlmAdapters.Anthropic;

public sealed class LlmPlayerAgent : IPlayerAgent
{
    /// <summary>
    /// Creates an LLM-backed player agent.
    /// </summary>
    /// <param name="options">Anthropic API configuration (API key, model, etc.).</param>
    /// <param name="fallback">Deterministic scoring agent used on LLM failure.</param>
    /// <exception cref="ArgumentNullException">If options or fallback is null.</exception>
    public LlmPlayerAgent(AnthropicOptions options, ScoringPlayerAgent fallback);

    /// <summary>
    /// Sends the full game state to Claude, parses the pick, and returns a decision.
    /// Falls back to ScoringPlayerAgent on any failure.
    /// </summary>
    /// <param name="turn">TurnStart with dialogue options and game state snapshot.</param>
    /// <param name="context">Additional agent context (stats, interest, momentum, etc.).</param>
    /// <returns>PlayerDecision with LLM reasoning and scoring agent's score breakdown.</returns>
    /// <exception cref="ArgumentNullException">If turn or context is null.</exception>
    /// <exception cref="InvalidOperationException">If turn.Options is empty (length 0).</exception>
    public Task<PlayerDecision> DecideAsync(TurnStart turn, PlayerAgentContext context);
}
```

### Internal Members (not public, but specified for implementer clarity)

```csharp
/// <summary>
/// Builds the full LLM prompt from turn data and agent context.
/// </summary>
private string BuildPrompt(TurnStart turn, PlayerAgentContext context);

/// <summary>
/// Parses "PICK: [A/B/C/D]" from the LLM response text.
/// Returns the 0-based option index, or null if parsing fails.
/// </summary>
private static int? ParsePick(string responseText, int optionCount);
```

### Dependencies from Other Issues

From #346 (must be implemented first):
- `IPlayerAgent` — the interface this class implements
- `PlayerDecision` — the return type
- `OptionScore` — per-option score breakdown (populated by the fallback `ScoringPlayerAgent`)
- `PlayerAgentContext` — input context with stats, interest, momentum, shadows, etc.

From #347 (must be implemented first):
- `ScoringPlayerAgent` — used as the fallback and to compute `OptionScore[]` for every decision

From `Pinder.LlmAdapters`:
- `AnthropicClient` — HTTP transport for Anthropic Messages API
- `AnthropicOptions` — configuration carrier (API key, model, max tokens, temperature)
- `MessagesRequest`, `MessagesResponse`, `ContentBlock`, `Message` — Anthropic DTO types

From `Pinder.Core`:
- `TurnStart`, `DialogueOption`, `GameStateSnapshot` — turn data
- `StatType`, `ShadowStatType` — stat enums
- `InterestState` — interest state enum
- `StatBlock` — for stat lookups in prompt formatting

---

## LLM Prompt Structure

The prompt sent to Claude is a single user message containing all game context and a rules reminder. The system message should be minimal (role framing only).

### System Message

```
You are a strategic player in Pinder, a comedy dating RPG. You analyze game mechanics
and choose the optimal dialogue option each turn. Your goal is to reach Interest 25
(date secured) while avoiding Interest 0 (unmatched/ghosted).
```

### User Message Template

```
You are playing as {player_character_name}, a sentient penis on a dating app.
You are talking to {opponent_character_name}. Choose one of the dialogue options below.

## Current State
- Interest: {interest}/25 ({interest_state_name}) — {modifier_note}
- Momentum: {streak} consecutive wins{momentum_note}
- Active traps: {trap_list_or_none}
- Shadow levels: {shadow_summary}
- Turn: {turn_number}

## Your Options
A) [{STAT_NAME} +{modifier}] DC {dc} | Need {need}+ on d20 | {pct}% success | {risk_tier}{bonus_icons}
   Text: "{intended_text}"
{repeat for B, C, D as applicable}

## Rules Reminder
- Roll d20 + stat modifier + bonuses vs DC. Meet or beat DC = success.
- Success tiers: beat by 1-4 → +1 interest, 5-9 → +2, 10+ → +3. Nat 20 → +4.
- Failure tiers: miss by 1-2 → Fumble (−1), 3-5 → Misfire (−1), 6-9 → Trope Trap (−2 + trap), 10+ → Catastrophe (−3 + trap). Nat 1 → Legendary Fail (−4).
- Risk tier bonus on success: Hard → +1 interest, Bold → +2 interest.
- Momentum: 3+ wins → +2 to next roll. 5+ wins → +3.
- 🔗 = callback bonus: hidden +1/+2/+3 added to roll.
- 📖 = tell bonus: hidden +2 added to roll.
- ⭐ = combo: +1 interest on success.
- 🔓 = weakness window: DC is already reduced by 2-3.

Explain your reasoning step by step, weighing success probability, interest gain, risk,
and any active bonuses or traps. Then state your final choice as:
PICK: [A/B/C/D]
```

### Field Formatting Rules

| Field | Format | Source |
|---|---|---|
| `{player_character_name}` | Display name string | From session runner (not available on PlayerAgentContext — pass via constructor or hardcode for prototype) |
| `{interest}` | Integer 0–25 | `context.CurrentInterest` |
| `{interest_state_name}` | Enum name (e.g. "Interested", "Bored") | `context.InterestState.ToString()` |
| `{modifier_note}` | e.g. "grants advantage" or "grants disadvantage" or empty | Derived from `InterestState`: VeryIntoIt/AlmostThere → "grants advantage", Bored → "grants disadvantage", else empty |
| `{streak}` | Integer ≥ 0 | `context.MomentumStreak` |
| `{momentum_note}` | e.g. " (+2 to next roll)" or empty | Streak ≥ 5 → " (+3 to next roll)", ≥ 3 → " (+2 to next roll)", else empty |
| `{trap_list_or_none}` | Comma-separated names or "none" | `context.ActiveTrapNames` joined, or "none" if empty |
| `{shadow_summary}` | e.g. "Denial 3, Fixation 5, Madness 0, ..." | From `context.ShadowValues` if non-null, else "unknown" |
| `{turn_number}` | Integer | `context.TurnNumber` |
| `{STAT_NAME}` | Uppercase stat name (e.g. "CHARM") | `option.Stat.ToString().ToUpperInvariant()` |
| `{modifier}` | Signed integer | `context.PlayerStats.GetEffective(option.Stat)` |
| `{dc}` | Integer | `context.OpponentStats.GetDefenceDC(option.Stat)` |
| `{need}` | Integer | `dc - modifier` (before hidden bonuses) |
| `{pct}` | Integer 0–100 | `Math.Max(0, Math.Min(100, (21 - need) * 5))` |
| `{risk_tier}` | "Safe" / "Medium" / "Hard" / "Bold" | Based on `need`: ≤5 Safe, 6–10 Medium, 11–15 Hard, ≥16 Bold |
| `{bonus_icons}` | Space-separated icons | 🔗 if `CallbackTurnNumber != null`, 📖 if `HasTellBonus`, ⭐ if `ComboName != null`, 🔓 if `HasWeaknessWindow` |
| `{intended_text}` | Quoted string | `option.IntendedText` |

**Important:** The `{pct}` and `{need}` shown in the prompt do NOT include hidden bonuses (tell, callback, momentum). This is intentional — these bonuses are hidden from the player in the game rules. The LLM is told about them via icons so it can reason about them qualitatively, not quantitatively.

### Prompt Size Considerations

The prompt is expected to be ~800–1200 tokens (well within limits). No prompt caching is needed for the player agent — each turn is a one-shot call with no conversation history. `MaxTokens` for the response should be set to 512 (reasoning + pick fits easily).

---

## Anthropic API Integration

### Client Construction

`LlmPlayerAgent` creates its own `AnthropicClient` from the provided `AnthropicOptions`. It does NOT share the `AnthropicClient` used by `AnthropicLlmAdapter` (the game's narrative LLM). The player agent's client is a separate concern — different temperature, different max tokens, different prompt structure.

```
Constructor:
  _client = new AnthropicClient(options.ApiKey)
  _fallback = fallback
  _model = options.Model   // e.g. "claude-sonnet-4-20250514"
```

### Request Construction

```
MessagesRequest:
  Model = _model
  MaxTokens = 512
  Temperature = 0.3   // Low temperature for strategic reasoning (deterministic-ish)
  System = [ ContentBlock { Type = "text", Text = <system message> } ]
  Messages = [ Message { Role = "user", Content = <user message> } ]
```

**Temperature:** Use 0.3 (low) to encourage consistent strategic reasoning. This is distinct from the narrative LLM's 0.9 temperature. The player agent should be analytical, not creative.

### Response Parsing

1. Get response text from `MessagesResponse.GetText()`
2. Search for `PICK:` pattern (case-insensitive)
3. Extract the letter immediately following `PICK:` (after optional whitespace and brackets)
4. Map letter to 0-based index: A→0, B→1, C→2, D→3
5. Validate index is within `[0, turn.Options.Length)`

Parsing regex pattern (conceptual): `PICK:\s*\[?([A-Da-d])\]?`

If the response contains multiple `PICK:` lines, use the **last** one (the LLM may revise its choice during reasoning).

---

## Fallback Behavior

On ANY of the following failures, `DecideAsync` falls back to `_fallback.DecideAsync(turn, context)`:

| Failure | Detection |
|---|---|
| Anthropic API returns an error (4xx, 5xx) | `AnthropicApiException` caught |
| Anthropic API times out | `TaskCanceledException` or `HttpRequestException` caught |
| Response text is empty or null | `string.IsNullOrWhiteSpace(responseText)` |
| `PICK:` pattern not found in response | `ParsePick()` returns null |
| Parsed letter is outside option range | Index ≥ `turn.Options.Length` |
| Any other unexpected exception | Catch-all `Exception` handler |

**On fallback:**
- `PlayerDecision.Reasoning` should indicate the fallback occurred, e.g.: `"[LLM fallback: {error description}] {scoring agent reasoning}"`
- `PlayerDecision.OptionIndex` comes from the scoring agent
- `PlayerDecision.Scores` come from the scoring agent

**The fallback must NEVER throw.** If even the scoring agent throws (which it shouldn't given valid inputs), that exception propagates up — but `LlmPlayerAgent` itself must not add failure modes beyond what `ScoringPlayerAgent` already has.

---

## Scores Population

`PlayerDecision.Scores` is ALWAYS populated by the `ScoringPlayerAgent`, not by the LLM. The workflow is:

1. Call `_fallback.DecideAsync(turn, context)` to get `scoringDecision`
2. Attempt LLM call
3. On LLM success: return `new PlayerDecision(llmPickIndex, llmReasoningText, scoringDecision.Scores)`
4. On LLM failure: return `scoringDecision` (with modified reasoning prefix)

This means the `Scores` array is always available for the playtest output table (#351), regardless of whether the LLM succeeded.

---

## Input/Output Examples

### Example 1: Successful LLM pick

**Input — TurnStart.Options:**

| Index | Stat | IntendedText | HasTellBonus | CallbackTurnNumber | ComboName |
|---|---|---|---|---|---|
| 0 | Charm | "Hey gorgeous, come here often?" | false | null | null |
| 1 | Rizz | "Your curves are... mathematical" | false | null | null |
| 2 | Honesty | "I'm nervous but you seem cool" | true | null | null |
| 3 | Chaos | "I once fought a raccoon and lost" | false | null | "WitChaosSA" |

**Input — PlayerAgentContext:**
- PlayerStats: Charm +4, Rizz +1, Honesty +3, Chaos +2, Wit +2, SA +3
- OpponentStats: (defences yield DCs: Charm→15, Rizz→14, Honesty→13, Chaos→15)
- CurrentInterest: 12
- InterestState: Interested
- MomentumStreak: 2
- ActiveTrapNames: []
- SessionHorniness: 4
- ShadowValues: { Denial: 3, Fixation: 1, Madness: 0, Horniness: 4, Dread: 0, Obsession: 2 }
- TurnNumber: 5

**Prompt sent to Claude (user message):**
```
You are playing as Sable, a sentient penis on a dating app.
You are talking to Brick. Choose one of the dialogue options below.

## Current State
- Interest: 12/25 (Interested)
- Momentum: 2 consecutive wins
- Active traps: none
- Shadow levels: Denial 3, Fixation 1, Madness 0, Horniness 4, Dread 0, Obsession 2
- Turn: 5

## Your Options
A) [CHARM +4] DC 15 | Need 11+ on d20 | 50% success | Hard
   Text: "Hey gorgeous, come here often?"
B) [RIZZ +1] DC 14 | Need 13+ on d20 | 40% success | Hard
   Text: "Your curves are... mathematical"
C) [HONESTY +3] DC 13 | Need 10+ on d20 | 55% success | Medium 📖
   Text: "I'm nervous but you seem cool"
D) [CHAOS +2] DC 15 | Need 13+ on d20 | 40% success | Hard ⭐
   Text: "I once fought a raccoon and lost"

## Rules Reminder
- Roll d20 + stat modifier + bonuses vs DC. Meet or beat DC = success.
- Success tiers: beat by 1-4 → +1 interest, 5-9 → +2, 10+ → +3. Nat 20 → +4.
- Failure tiers: miss by 1-2 → Fumble (−1), 3-5 → Misfire (−1), 6-9 → Trope Trap (−2 + trap), 10+ → Catastrophe (−3 + trap). Nat 1 → Legendary Fail (−4).
- Risk tier bonus on success: Hard → +1 interest, Bold → +2 interest.
- Momentum: 3+ wins → +2 to next roll. 5+ wins → +3.
- 🔗 = callback bonus: hidden +1/+2/+3 added to roll.
- 📖 = tell bonus: hidden +2 added to roll.
- ⭐ = combo: +1 interest on success.
- 🔓 = weakness window: DC is already reduced by 2-3.

Explain your reasoning step by step, weighing success probability, interest gain, risk,
and any active bonuses or traps. Then state your final choice as:
PICK: [A/B/C/D]
```

**Claude response (example):**
```
Let me analyze each option:

Option A (Charm, 50%): Solid probability at Hard tier, meaning +1 bonus interest on success.
The 50% is decent but risky — a miss by 6-9 would trigger a Trope Trap on Charm.

Option B (Rizz, 40%): Lower probability and Hard tier. Not ideal with only 40% success chance.

Option C (Honesty, 55%): Highest visible success rate, plus the 📖 tell bonus adds a hidden +2
to the roll, pushing real probability even higher (~65%). Medium risk tier means no bonus interest
but much safer. With momentum at 2, one more win activates the +2 bonus for turn 6.

Option D (Chaos, 40%): The ⭐ combo is tempting (+1 interest on success), but 40% is low for
Hard tier. The combo bonus doesn't compensate for the higher failure risk.

The tell bonus on C makes it secretly the best option. With momentum at 2, securing this win
activates the momentum bonus next turn. Safety-first is the right call at Interest 12.

PICK: [C]
```

**Output — PlayerDecision:**
```
OptionIndex: 2  (C → index 2)
Reasoning: "Let me analyze each option:\n\nOption A (Charm, 50%)...PICK: [C]"  (full LLM text)
Scores: [  // from ScoringPlayerAgent
  { OptionIndex: 0, Score: ~0.25, SuccessChance: 0.50, ... },
  { OptionIndex: 1, Score: ~-0.10, SuccessChance: 0.40, ... },
  { OptionIndex: 2, Score: ~1.40, SuccessChance: 0.55, ... },
  { OptionIndex: 3, Score: ~0.05, SuccessChance: 0.40, ... }
]
```

### Example 2: LLM failure — fallback to ScoringPlayerAgent

**Scenario:** Anthropic API returns 529 (overloaded) after retries.

**Output — PlayerDecision:**
```
OptionIndex: 2  (from ScoringPlayerAgent)
Reasoning: "[LLM fallback: Anthropic API error (529 Overloaded)] Honesty at 55% beats Charm at 50% — 5pp advantage. Momentum at 2 — prioritizing success to reach +2 bonus."
Scores: [  // from ScoringPlayerAgent
  { OptionIndex: 0, Score: ~0.25, ... },
  { OptionIndex: 1, Score: ~-0.10, ... },
  { OptionIndex: 2, Score: ~1.40, ... },
  { OptionIndex: 3, Score: ~0.05, ... }
]
```

### Example 3: LLM returns unparseable response

**Scenario:** Claude responds with reasoning but no `PICK:` line.

**Output:** Same as Example 2, with `Reasoning: "[LLM fallback: Could not parse PICK from response] ..."`.

### Example 4: Single option (Horniness-forced Rizz)

**Input:** `TurnStart.Options` has 1 option (Rizz, forced by Horniness ≥ 18).

**Prompt:** Shows only `A) [RIZZ ...]`. Rules reminder still included.

**Output:**
```
OptionIndex: 0
Reasoning: "Only one option available (Horniness-forced Rizz). PICK: [A]"
Scores: [ { OptionIndex: 0, ... } ]
```

---

## Acceptance Criteria

### AC1: `LlmPlayerAgent` implements `IPlayerAgent`

`LlmPlayerAgent` must be a `sealed class` in `session-runner/` that implements the `IPlayerAgent` interface from #346. The `DecideAsync` method must match the exact signature:

```csharp
public Task<PlayerDecision> DecideAsync(TurnStart turn, PlayerAgentContext context)
```

The constructor must accept `AnthropicOptions` and `ScoringPlayerAgent`, both non-null.

### AC2: LLM prompt includes full option context, game state, rules summary

The prompt sent to Claude must include:
- **Game state:** interest value and state name, momentum streak with bonus note, active trap names, shadow levels (if available), turn number
- **All options:** each with stat name, modifier, DC, need, success percentage, risk tier, bonus icons (🔗📖⭐🔓), and intended text
- **Rules reminder:** success/failure tier table, risk tier bonus, momentum, callback/tell/combo/weakness explanations
- **Instruction:** "Explain your reasoning... then PICK: [A/B/C/D]"

### AC3: Parses `PICK: [A/B/C/D]` from response

The response parser must:
- Search for `PICK:` case-insensitively
- Accept optional whitespace and brackets around the letter: `PICK: A`, `PICK: [A]`, `PICK:[B]`, `pick: c`
- Map A→0, B→1, C→2, D→3
- If multiple `PICK:` lines exist, use the **last** one
- Return null (trigger fallback) if no valid `PICK:` is found
- Return null if the parsed index is ≥ `turn.Options.Length`

### AC4: `PlayerDecision.Reasoning` contains the LLM's explanation

On LLM success:
- `Reasoning` = the full text returned by Claude (including the PICK line)
- Must not be null or empty

On LLM fallback:
- `Reasoning` = `"[LLM fallback: {description}] {scoring agent reasoning}"`

### AC5: Falls back to `ScoringPlayerAgent` on API error

The following must all trigger fallback (not throw):
- `AnthropicApiException` (any HTTP error from Anthropic)
- `HttpRequestException` (network error)
- `TaskCanceledException` (timeout)
- Empty or null response text
- Unparseable response (no `PICK:` found)
- Parsed pick index out of range
- Any other `Exception`

The fallback call is `_fallback.DecideAsync(turn, context)`. The returned `PlayerDecision` is used directly, with its `Reasoning` prefixed with the fallback description.

### AC6: Session runner uses reasoning in playtest output

This is a wiring concern for the session runner (`Program.cs`):
- When constructing `LlmPlayerAgent`, pass the `AnthropicOptions` and a `ScoringPlayerAgent` instance
- The `PlayerDecision.Reasoning` returned by `LlmPlayerAgent` is displayed in the playtest markdown per #351's output format
- The agent type selection should be configurable via environment variable (e.g., `PLAYER_AGENT=llm` vs `PLAYER_AGENT=scoring`, defaulting to `scoring`)

### AC7: Build clean

The solution must compile with zero errors. All existing tests (1977+) must pass unchanged. No new NuGet packages added to any project.

---

## Edge Cases

| Case | Expected Behavior |
|---|---|
| `turn.Options` is empty (length 0) | Throw `InvalidOperationException("No options available")` before attempting LLM call |
| `turn.Options` has exactly 1 option | Prompt shows only "A)". LLM pick must be "A". Fallback works normally. |
| `turn.Options` has 2 or 3 options | Prompt shows A/B or A/B/C. Parse accepts only valid letters for the option count. |
| `context.ShadowValues` is null | Prompt shows "Shadow levels: unknown" instead of per-shadow breakdown |
| `context.ActiveTrapNames` is empty | Prompt shows "Active traps: none" |
| `context.MomentumStreak` is 0 | Prompt shows "Momentum: 0 consecutive wins" with no bonus note |
| LLM responds with lowercase `pick: a` | Parser accepts case-insensitively → index 0 |
| LLM responds with `PICK: [A]` (bracketed) | Parser strips brackets → index 0 |
| LLM responds with `PICK: E` (out of range for 4 options) | `ParsePick` returns null → fallback |
| LLM responds with `PICK: A` then later `PICK: C` | Use last match → index 2 |
| LLM response is empty string | Fallback triggered (empty response check) |
| LLM includes reasoning but no PICK line | Fallback triggered (parse failure) |
| API key is empty/invalid | `AnthropicClient` constructor or first call throws → caught → fallback |
| Network timeout | `TaskCanceledException` caught → fallback |
| Anthropic returns 429 (rate limited) | `AnthropicClient` retries internally; if exhausted, `AnthropicApiException` → fallback |
| `AnthropicOptions.Model` specifies Opus | Works — model string is passed through to the request. Larger model, slower response. |
| `option.IntendedText` contains quotes or special characters | Must be escaped in prompt (or just included as-is — Claude handles it) |
| `ScoringPlayerAgent` fallback itself throws | Exception propagates up (not caught by LlmPlayerAgent) — this should never happen with valid inputs |

---

## Error Conditions

| Condition | Expected Behavior |
|---|---|
| `turn` is null | Throw `ArgumentNullException("turn")` |
| `context` is null | Throw `ArgumentNullException("context")` |
| `turn.Options` is empty (length 0) | Throw `InvalidOperationException("No options available")` |
| `options` (constructor) is null | Throw `ArgumentNullException("options")` |
| `fallback` (constructor) is null | Throw `ArgumentNullException("fallback")` |
| `options.ApiKey` is empty/whitespace | `AnthropicClient` constructor throws `ArgumentException` — caught by `DecideAsync` → fallback |
| Anthropic API error (4xx/5xx) | Caught as `AnthropicApiException` → fallback |
| Network error | Caught as `HttpRequestException` → fallback |
| Timeout | Caught as `TaskCanceledException` → fallback |
| Unparseable LLM response | Caught internally → fallback |
| Any unexpected exception during LLM call | Caught as `Exception` → fallback |

**Note:** Constructor validation (`ArgumentNullException` for `options` and `fallback`) throws immediately and does NOT fall back — these are programmer errors, not runtime failures.

---

## Dependencies

### Build Dependencies (must be implemented first)

| Dependency | Issue | Purpose |
|---|---|---|
| `IPlayerAgent` interface | #346 | Interface this class implements |
| `PlayerDecision` type | #346 | Return type |
| `OptionScore` type | #346 | Score breakdown (populated by ScoringPlayerAgent) |
| `PlayerAgentContext` type | #346 | Input context |
| `ScoringPlayerAgent` | #347 | Fallback agent + score computation |

### Library Dependencies (already exist)

| Dependency | Project | Purpose |
|---|---|---|
| `AnthropicClient` | `Pinder.LlmAdapters` | HTTP transport for Anthropic Messages API |
| `AnthropicOptions` | `Pinder.LlmAdapters` | Configuration carrier (API key, model, temperature) |
| `AnthropicApiException` | `Pinder.LlmAdapters` | Typed exception for API errors |
| `MessagesRequest` | `Pinder.LlmAdapters` | Request DTO |
| `MessagesResponse` | `Pinder.LlmAdapters` | Response DTO |
| `ContentBlock`, `Message` | `Pinder.LlmAdapters` | DTO building blocks |

### Core Dependencies (read-only — no changes to Core)

| Type | Namespace | Usage |
|---|---|---|
| `TurnStart` | `Pinder.Core.Conversation` | Input to `DecideAsync` |
| `DialogueOption` | `Pinder.Core.Conversation` | Option data (stat, text, bonuses) |
| `GameStateSnapshot` | `Pinder.Core.Conversation` | Game state (interest, momentum, traps) |
| `InterestState` | `Pinder.Core.Conversation` | Enum for interest state display |
| `StatBlock` | `Pinder.Core.Stats` | Stat lookups for prompt formatting |
| `StatType` | `Pinder.Core.Stats` | Stat enum for names and lookups |
| `ShadowStatType` | `Pinder.Core.Stats` | Shadow stat enum for shadow display |

### Downstream Consumers

| Consumer | Usage |
|---|---|
| `session-runner/Program.cs` | Creates `LlmPlayerAgent`, calls `DecideAsync` per turn |
| Pick reasoning output (#351) | Displays `Reasoning` and `Scores` in playtest markdown |

### External Services

| Service | Details |
|---|---|
| Anthropic Messages API | `https://api.anthropic.com/v1/messages`. Requires `ANTHROPIC_API_KEY` environment variable. Model configurable (default: `claude-sonnet-4-20250514`). |

---

## Notes for Implementers

1. **LangVersion 8.0:** No C# 9+ features. Use `sealed class`, not records. No `init` accessors. Use `(StatType)Enum.Parse(typeof(StatType), value, true)` — no generic overload.

2. **Separate AnthropicClient instance:** Do NOT reuse the narrative LLM's client. The player agent needs different temperature (0.3 vs 0.9) and max tokens (512 vs 1024). Construct a dedicated `AnthropicClient` in the `LlmPlayerAgent` constructor.

3. **IDisposable consideration:** `AnthropicClient` implements `IDisposable` (owns an `HttpClient`). `LlmPlayerAgent` should also implement `IDisposable` to dispose its client. Alternatively, the session runner can pass an externally-owned `AnthropicClient` if a constructor overload is added — but this is optional at prototype maturity.

4. **Scores always from ScoringPlayerAgent:** Call `_fallback.DecideAsync(turn, context)` first (or in parallel — both work since scoring is sync). Use its `Scores` array for the returned `PlayerDecision` regardless of LLM success/failure. This ensures the score table in playtest output is always populated.

5. **Character names:** `PlayerAgentContext` does not carry character names. For the prototype, the implementer may hardcode names, accept them as constructor parameters, or derive them from the session runner's existing variables. The prompt should include character names for immersion but they are not mechanically significant.

6. **Environment variable for agent selection:** The session runner should select between `ScoringPlayerAgent` and `LlmPlayerAgent` based on an environment variable (e.g., `PLAYER_AGENT`). This is a wiring concern in `Program.cs`, not in `LlmPlayerAgent` itself.

7. **No prompt caching:** Unlike `AnthropicLlmAdapter` which caches character system prompts across turns, the player agent sends a fresh one-shot prompt each turn. The prompt is small (~1000 tokens) and changes every turn, making caching counterproductive.

8. **CallbackBonus values in prompt:** The prompt shows the 🔗 icon but does NOT reveal the exact callback bonus value (+1/+2/+3). This mirrors the game's hidden-bonus design. The LLM should reason qualitatively about callbacks ("the callback icon suggests a hidden bonus").
