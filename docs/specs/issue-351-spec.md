# Spec: Session Runner — Show Pick Reasoning in Playtest Output

**Issue:** #351
**Module:** docs/modules/session-runner.md (create new)

---

## Overview

After each turn in the session runner's playtest markdown output, display the player agent's reasoning for its pick and a compact score table showing all options with their computed metrics. This makes the sim output useful for understanding game balance and agent decision quality, rather than just showing which option was chosen.

This issue depends on #347 (ScoringPlayerAgent) and #348 (LlmPlayerAgent), which define the `PlayerDecision` type containing `Reasoning` (string) and `Scores` (array of `OptionScore`).

---

## Function Signatures

All changes are in `session-runner/Program.cs`. No new public types are introduced by this issue — it consumes types defined by #346/#347/#348.

### Formatting Functions (new, in Program.cs)

```csharp
/// <summary>
/// Formats the player agent's reasoning as a markdown blockquote.
/// Each line of the reasoning string is prefixed with "> ".
/// </summary>
/// <param name="decision">The PlayerDecision returned by the agent.</param>
/// <param name="agentTypeName">The simple class name of the agent (e.g. "ScoringPlayerAgent").</param>
/// <returns>A multi-line string containing the formatted reasoning block.</returns>
static string FormatReasoningBlock(PlayerDecision decision, string agentTypeName);

/// <summary>
/// Formats the option score table as a markdown table.
/// The chosen option row is marked with ✓ and its score is bold.
/// </summary>
/// <param name="decision">The PlayerDecision containing Scores and OptionIndex.</param>
/// <param name="options">The DialogueOption[] from TurnStart, used for stat labels.</param>
/// <returns>A multi-line string containing the markdown table.</returns>
static string FormatScoreTable(PlayerDecision decision, DialogueOption[] options);
```

### Types Consumed (defined by #346/#347/#348, NOT by this issue)

```csharp
// From IPlayerAgent pipeline:
public sealed class PlayerDecision
{
    public int OptionIndex { get; }       // 0-based index of chosen option
    public string Reasoning { get; }      // Free-text reasoning from agent
    public OptionScore[] Scores { get; }  // One per option, ordered by option index
}

public sealed class OptionScore
{
    public int OptionIndex { get; }              // 0-based
    public float Score { get; }                  // Overall score (higher = better)
    public float SuccessChance { get; }          // 0.0–1.0 probability
    public float ExpectedInterestGain { get; }   // Expected interest delta
    public string[] BonusesApplied { get; }      // e.g. ["📖", "🔗"]
}
```

---

## Input/Output Examples

### Example 1: ScoringPlayerAgent pick

**Input state:**
- `PlayerDecision.OptionIndex` = 0
- `PlayerDecision.Reasoning` = `"Charm +7 at DC19 (45%) vs Honesty +7 at DC22 (30%) — 15pp advantage.\n🔗 Callback on Honesty (+2) narrows gap to 40% vs 45% — Charm still wins.\nMomentum at 2 — prioritize success to reach streak bonus.\nPick: A"`
- `PlayerDecision.Scores`:
  - `{ OptionIndex: 0, Score: 8.3, SuccessChance: 0.45, ExpectedInterestGain: 1.8, BonusesApplied: [] }`
  - `{ OptionIndex: 1, Score: 1.2, SuccessChance: 0.05, ExpectedInterestGain: 0.9, BonusesApplied: [] }`
  - `{ OptionIndex: 2, Score: 6.1, SuccessChance: 0.30, ExpectedInterestGain: 1.4, BonusesApplied: ["📖", "🔗"] }`
  - `{ OptionIndex: 3, Score: 0.0, SuccessChance: 0.00, ExpectedInterestGain: 0.0, BonusesApplied: [] }`
- `TurnStart.Options`: 4 options with stats CHARM, RIZZ, HONESTY, CHAOS
- Agent type name: `"ScoringPlayerAgent"`

**Expected output (appended after the `► Player picks: A (CHARM)` line):**

```markdown
**Player reasoning (ScoringPlayerAgent):**
> Charm +7 at DC19 (45%) vs Honesty +7 at DC22 (30%) — 15pp advantage.
> 🔗 Callback on Honesty (+2) narrows gap to 40% vs 45% — Charm still wins.
> Momentum at 2 — prioritize success to reach streak bonus.
> Pick: A

| Option | Stat | Pct | Expected ΔI | Score |
|---|---|---|---|---|
| A ✓ | CHARM | 45% | +1.8 | **8.3** |
| B | RIZZ | 5% | +0.9 | 1.2 |
| C | HONESTY | 30% | +1.4 📖🔗 | 6.1 |
| D | CHAOS | 0% | +0.0 | 0.0 |
```

### Example 2: LlmPlayerAgent pick

**Input state:**
- `PlayerDecision.OptionIndex` = 2
- `PlayerDecision.Reasoning` = `"The 🔗 callback on option C (Honesty) closes the probability gap to near-tie. But Charm is Safe tier — Honesty is Hard. At Interest 19 I need one more win, not a risky bet. Going Charm — cleaner, lower trap exposure, gets us home.\nPick: C"`
- Agent type name: `"LlmPlayerAgent"`

> **Note:** The reasoning text is verbatim from the LLM. The fact that reasoning says "Going Charm" but `OptionIndex` is 2 (HONESTY) reflects that the LLM's natural-language reasoning may be inconsistent with its final `PICK:` directive. The `OptionIndex` is authoritative (parsed from the structured `PICK: C` line by LlmPlayerAgent); the reasoning prose is informational only.

**Expected output:**

```markdown
**Player reasoning (LlmPlayerAgent):**
> The 🔗 callback on option C (Honesty) closes the probability gap to near-tie. But Charm is Safe
> tier — Honesty is Hard. At Interest 19 I need one more win, not a risky bet. Going Charm —
> cleaner, lower trap exposure, gets us home.
> Pick: C

| Option | Stat | Pct | Expected ΔI | Score |
|---|---|---|---|---|
| A | CHARM | 45% | +1.8 | 8.3 |
| B | RIZZ | 5% | +0.9 | 1.2 |
| C ✓ | HONESTY | 30% | +1.4 📖🔗 | **6.1** |
| D | CHAOS | 0% | +0.0 | 0.0 |
```

---

## Acceptance Criteria

### AC1: Playtest output includes reasoning block after each pick

After the existing line `**► Player picks: {letter} ({STAT})**`, the output must include a reasoning block formatted as:

```
**Player reasoning ({AgentTypeName}):**
> {line 1 of reasoning}
> {line 2 of reasoning}
> ...
```

- Each line of `PlayerDecision.Reasoning` is prefixed with `> ` (markdown blockquote).
- The reasoning text is split on `\n` characters.
- A blank line separates the pick line from the reasoning block.
- A blank line separates the reasoning block from the score table.

### AC2: Option score table shown for each turn

After the reasoning block, a markdown table is rendered with columns: `Option`, `Stat`, `Pct`, `Expected ΔI`, `Score`.

- One row per option in `TurnStart.Options`, ordered by index (A, B, C, D).
- `Option` column: letter label (A/B/C/D). The chosen option has ` ✓` appended (e.g. `A ✓`).
- `Stat` column: uppercase stat name from `DialogueOption.Stat` (e.g. `CHARM`, `RIZZ`).
- `Pct` column: `OptionScore.SuccessChance` formatted as integer percentage (e.g. `45%`). Use `Math.Round(score.SuccessChance * 100)` — no decimal places.
- `Expected ΔI` column: `OptionScore.ExpectedInterestGain` formatted as `+{value:F1}` (one decimal, always with sign). If `BonusesApplied` is non-empty, append the bonus emoji strings concatenated (e.g. `+1.4 📖🔗`).
- `Score` column: `OptionScore.Score` formatted to one decimal. The chosen option's score is wrapped in `**bold**`.

### AC3: Reasoning reflects actual decision logic (not hardcoded text)

The reasoning string comes directly from `PlayerDecision.Reasoning` as returned by the `IPlayerAgent.DecideAsync()` call. The session runner MUST NOT generate or substitute its own reasoning text. It is a pass-through display of whatever the agent produced.

### AC4: Works for both ScoringPlayerAgent and LlmPlayerAgent

- The agent type name is derived from the runtime type of the `IPlayerAgent` instance: `agent.GetType().Name`.
- The formatting code must not branch on agent type — it uses the same `PlayerDecision` shape regardless of which agent produced it.
- Both agent types populate `Reasoning` and `Scores` (ScoringPlayerAgent generates structured reasoning; LlmPlayerAgent uses the LLM response text).

### AC5: Build clean

- `dotnet build session-runner/` succeeds with zero errors and zero warnings.
- No new NuGet dependencies.

---

## Integration Point: Where to Insert in Program.cs

The current flow in `Program.cs` (in the turn loop, after the UI panel `Console.WriteLine("```")` and before the roll resolution) contains the pick logic. Locate this by searching for the pattern `BestOption(turnStart.Options` or `► Player picks`:

```csharp
int pick = BestOption(turnStart.Options, sableStats);
var chosen = turnStart.Options[pick];
Console.WriteLine($"**► Player picks: {letters[pick]} ({StatLabel(chosen.Stat)})**");
Console.WriteLine();
```

After this issue (and its dependencies #347/#348), this becomes:

```
var decision = await agent.DecideAsync(turnStart, agentContext);
int pick = decision.OptionIndex;
var chosen = turnStart.Options[pick];
Console.WriteLine($"**► Player picks: {letters[pick]} ({StatLabel(chosen.Stat)})**");
Console.WriteLine();
Console.WriteLine(FormatReasoningBlock(decision, agent.GetType().Name));
Console.WriteLine(FormatScoreTable(decision, turnStart.Options));
Console.WriteLine();
```

The `BestOption()` static method is replaced by `IPlayerAgent.DecideAsync()` (from #346/#348). This issue (#351) is specifically about the two `Format*` calls and their output — the agent wiring is done by #348.

---

## Edge Cases

### Empty reasoning string
If `PlayerDecision.Reasoning` is `null` or empty, emit:
```
**Player reasoning ({AgentTypeName}):**
> (no reasoning provided)
```

### Fewer than 4 options
If `TurnStart.Options` has fewer than 4 entries (e.g. due to Horniness forced Rizz replacing options), the score table should have exactly as many rows as there are options. Letter labels still follow A, B, C, D sequence for the indices present.

### Scores array mismatch
If `PlayerDecision.Scores` has fewer entries than `TurnStart.Options` (defensive case), show `—` for missing score data in that row. If `Scores` is `null`, skip the score table entirely and log a warning to stderr.

### OptionIndex out of range
If `PlayerDecision.OptionIndex` is outside `[0, Options.Length)`, this is an error from the agent. The session runner should still render the table (no row gets ✓) and log a warning to stderr. The pick line should show the raw index.

### Very long reasoning text
LlmPlayerAgent may return multi-paragraph reasoning. No truncation — display the full text. Each line is blockquoted. The output is markdown, so length is acceptable.

### BonusesApplied contains multiple entries
Concatenate them without separators: `📖🔗` not `📖 🔗`. This matches the issue example.

### Reasoning text contradicts pick
LlmPlayerAgent reasoning may appear to contradict the chosen option (e.g. reasoning says "Going Charm" but picks HONESTY). This is expected LLM behavior — the `OptionIndex` is authoritative (parsed from structured `PICK:` line), while reasoning prose is informational. The session runner displays both without attempting to reconcile them.

---

## Error Conditions

| Condition | Behavior |
|---|---|
| `PlayerDecision` is null | Should not happen (agent contract requires non-null return). If it does, skip reasoning + table, write warning to stderr. |
| `PlayerDecision.Scores` is null | Skip score table. Still show reasoning block. Write warning to stderr. |
| `PlayerDecision.Reasoning` is null or empty | Show `(no reasoning provided)` in blockquote. |
| `OptionScore.SuccessChance` is NaN or negative | Display as `0%`. |
| `OptionScore.Score` is NaN or negative | Display score as-is (negative scores are valid, e.g. `-1.8`). Display NaN as `0.0`. |

No exceptions should be thrown from the formatting functions. They are display-only and must be defensive.

---

## Dependencies

| Dependency | Type | Status |
|---|---|---|
| #346 — IPlayerAgent interface + types | Hard (defines `PlayerDecision`, `OptionScore`) | Must be implemented first |
| #347 — ScoringPlayerAgent | Hard (first concrete agent) | Must be implemented first |
| #348 — LlmPlayerAgent | Hard (second agent, wires into Program.cs) | Must be implemented first |
| `session-runner/Program.cs` | File being modified | Shared with #354, #353, #350, #348 |
| `Pinder.Core.Conversation.DialogueOption` | Read-only dependency | Existing, stable |
| `Pinder.Core.Conversation.TurnStart` | Read-only dependency | Existing, stable |
| `Pinder.Core.Stats.StatType` | Read-only dependency (for stat labels) | Existing, stable |

No external services or new libraries required.
