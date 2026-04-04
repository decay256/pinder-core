# Issue #491 — Prompt: success delivery — additions must improve existing sentiment, not add new ideas

**Module**: docs/modules/llm-adapters.md

## Overview

The current `SuccessDeliveryInstruction` in `PromptTemplates` uses vague margin bands (1–5, 6–10) that don't align with the game's actual `SuccessScale` tiers (1–4, 5–9, 10+, Nat 20) and instructs the LLM to "add a small flourish" on strong successes — which causes the LLM to inject new ideas the player never wrote. This issue revises the instruction text so that success delivery **improves the quality of what the player already said** (sharper phrasing, better timing, tighter wording) without adding new content. Every idea in the delivered message must have a counterpart in the intended message.

## Function Signatures

No new functions are introduced. The change is confined to a single constant:

```csharp
// File: src/Pinder.LlmAdapters/PromptTemplates.cs
namespace Pinder.LlmAdapters
{
    public static class PromptTemplates
    {
        /// <summary>§3.3 — Deliver the intended message on a successful roll.</summary>
        public const string SuccessDeliveryInstruction = @"..."; // revised text
    }
}
```

**Type**: `public const string`
**Consumer**: `SessionDocumentBuilder.BuildDeliveryPrompt(DeliveryContext context)` — already references `PromptTemplates.SuccessDeliveryInstruction` via string replacement of `{player_name}`. No changes needed in `SessionDocumentBuilder`.

## Input/Output Examples

### Context available to the instruction

The instruction is injected into the user message by `SessionDocumentBuilder.BuildDeliveryPrompt()` on the **success path** (when `DeliveryContext.Outcome == FailureTier.None`). The surrounding context includes:

- `DeliveryContext.ChosenOption.IntendedText` — the player's chosen message (e.g., `"honestly? you're kind of funny for someone who tries this hard"`)
- `DeliveryContext.BeatDcBy` — integer, how much the roll beat the DC by (e.g., `3`, `7`, `14`)
- `DeliveryContext.ChosenOption.Stat` — the stat used (e.g., `StatType.Honesty`)
- Player name (resolved from `DeliveryContext.PlayerName`)

### Example: Clean success (margin 1–4)

**Input**: Intended text = `"honestly? you're kind of funny for someone who tries this hard"`, BeatDcBy = `3`

**Expected LLM behavior**: Deliver the message essentially as-is. Minor natural-voice adjustments only. No new ideas, no flourishes.

**Acceptable output**: `"honestly? you're kind of funny for someone who tries this hard"`

### Example: Strong success (margin 5–9)

**Input**: Intended text = `"honestly? you're kind of funny for someone who tries this hard"`, BeatDcBy = `7`

**Expected LLM behavior**: Sharpen the phrasing — tighter word choice, better rhythm, more precise tone. Every idea in the output must map to an idea in the input.

**Acceptable output**: `"honestly? you're pretty funny for someone trying that hard"`
**Unacceptable output**: `"honestly? you're funny for someone who tries this hard. we should grab coffee sometime"` (added a new idea — the coffee suggestion)

### Example: Critical success / Nat 20 (margin 10+)

**Input**: Intended text = `"honestly? you're kind of funny for someone who tries this hard"`, BeatDcBy = `14`

**Expected LLM behavior**: The message lands with precision — every word earns its place, timing is perfect. Still the same ideas, just at peak execution.

**Acceptable output**: `"you're funny for someone who tries this hard"`
**Unacceptable output**: `"you're funny for someone who tries this hard 😏 maybe I should see how hard you try in person"` (added new flirtatious content)

## Acceptance Criteria

### AC1: SuccessDeliveryInstruction specifies margin-based delivery tiers

The revised `SuccessDeliveryInstruction` constant in `PromptTemplates.cs` must define three tiers that match the game's `SuccessScale` margins:

| Tier | Margin (BeatDcBy) | Delivery behavior |
|------|-------------------|-------------------|
| Clean | 1–4 | Deliver essentially as written with natural voice |
| Strong | 5–9 | Sharpen phrasing — tighter wording, better rhythm, more precise |
| Critical / Nat 20 | 10+ | Peak execution — every word earns its place, lands with precision |

The instruction must **not** use the old bands (1–5, 6–10) which don't align with `SuccessScale`.

### AC2: Strong success sharpens without adding new ideas

The instruction text must explicitly state that on a strong success, the LLM should **improve the existing sentiment** — sharper phrasing, better timing, tighter word choice — without introducing new ideas, topics, or content that wasn't in the intended message.

### AC3: Critical success lands with precision, not expansion

The instruction text must explicitly state that on a critical success (including Nat 20), the message should be at its absolute best version — but still contain only the ideas present in the intended message. Precision, not expansion.

### AC4: Every idea in delivered has a counterpart in intended

The instruction text must include a rule or constraint stating that every idea or topic in the delivered message must have a corresponding idea in the intended message. The LLM may refine, condense, or sharpen — but not add.

### AC5: Build clean, all tests pass

- The project must compile without errors or warnings related to this change.
- All existing tests (2295+) must continue to pass unchanged.
- No changes are needed outside `PromptTemplates.cs` since `SessionDocumentBuilder.BuildDeliveryPrompt()` already references `SuccessDeliveryInstruction` and performs `{player_name}` substitution.

## Edge Cases

### Margin exactly at tier boundaries

- `BeatDcBy = 4` → Clean tier (1–4 range, inclusive upper bound)
- `BeatDcBy = 5` → Strong tier (5–9 range, inclusive lower bound)
- `BeatDcBy = 9` → Strong tier (5–9 range, inclusive upper bound)
- `BeatDcBy = 10` → Critical tier (10+ range)

Note: These boundaries are enforced by the `SuccessScale` class in game logic — the prompt instruction must use the same boundaries so the LLM's behavior matches the mechanical tier.

### Nat 20 vs high margin

A Nat 20 always maps to `+4` interest in `SuccessScale` regardless of margin. The delivery instruction should treat Nat 20 identically to the critical tier (10+). The current instruction already mentions "critical success / Nat 20" — the revised version should preserve this grouping.

### Very short intended messages

If the intended text is a single word or very short phrase (e.g., `"hey"`), the instruction should not encourage the LLM to pad it. A clean success on `"hey"` should still be `"hey"` — there's nothing to sharpen.

### Intended message already at peak quality

If the player's intended text is already excellent, the instruction should not force changes. "Improve existing sentiment" means "make it better if possible" — not "always change something."

## Error Conditions

### No new error paths

This change modifies a string constant only. No runtime errors are introduced. The existing error handling in `SessionDocumentBuilder.BuildDeliveryPrompt()` (null checks on `DeliveryContext`) is unaffected.

### LLM non-compliance

The LLM may still add new ideas despite the instruction. This is a prompt engineering limitation, not a code error. Mitigation is through instruction clarity — the stronger the constraint language, the better compliance. This is verified via session playtesting, not automated tests.

## Dependencies

### Internal

- **`PromptTemplates.cs`** (`src/Pinder.LlmAdapters/PromptTemplates.cs`) — the only file modified.
- **`SessionDocumentBuilder.BuildDeliveryPrompt()`** — consumer of `SuccessDeliveryInstruction`. Already performs `{player_name}` substitution. No changes needed.
- **`DeliveryContext`** (`src/Pinder.Core/Conversation/DeliveryContext.cs`) — provides `BeatDcBy`, `Outcome`, `ChosenOption`. No changes needed.
- **`SuccessScale`** (`src/Pinder.Core/Rolls/SuccessScale.cs`) — defines the canonical margin tiers (1–4, 5–9, 10+, Nat 20). The instruction must align with these bands.

### External

- None. No new libraries, services, or dependencies.

### Issue dependencies

- None. This is a standalone prompt text change with no dependencies on other sprint issues. Per the sprint contract, this is in Wave 1 (parallel, no interdependencies).

### Placeholder token

The instruction must retain the `{player_name}` placeholder token. `SessionDocumentBuilder` replaces this at runtime with the resolved player display name. If the placeholder is removed or renamed, delivery prompts will contain a literal `{player_name}` string.
