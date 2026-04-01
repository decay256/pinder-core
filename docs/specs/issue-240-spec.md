# Spec: Issue #240 — DialogueOptionsInstruction Missing Output Format

## Overview

`PromptTemplates.DialogueOptionsInstruction` tells the LLM to generate 4 dialogue options with metadata tags but does **not** specify the structured output format that `ParseDialogueOptions` expects. The parser requires `OPTION_N` headers followed by `[STAT: X] [CALLBACK: ...] [COMBO: ...] [TELL_BONUS: ...]` metadata and `"quoted text"`, but the prompt never instructs the LLM to produce this structure. As a result, every dialogue options call returns 4 placeholder options with `IntendedText = "..."`, breaking all downstream mechanics (delivery, failure degradation, UI display).

## Function Signatures

No new functions or methods are introduced. The fix modifies the **value** of an existing constant:

```csharp
// Pinder.LlmAdapters/PromptTemplates.cs
public static class PromptTemplates
{
    public const string DialogueOptionsInstruction = "..."; // modified value
}
```

The parser that consumes this output is unchanged:

```csharp
// Pinder.LlmAdapters/Anthropic/AnthropicLlmAdapter.cs
internal static DialogueOption[] ParseDialogueOptions(string? llmResponse)
```

- **Input**: `string?` — raw LLM text output (may be null or empty)
- **Output**: `DialogueOption[]` — always exactly 4 elements; pads with defaults (`IntendedText = "..."`, stat from `DefaultPaddingStats`) if fewer than 4 valid options are parsed
- **Never throws**: all parse failures are swallowed; fewer than 4 parsed options are padded to 4

```csharp
// Pinder.Core/Conversation/DialogueOption.cs (existing, unchanged)
public sealed class DialogueOption
{
    public StatType Stat { get; }
    public string IntendedText { get; }
    public int? CallbackTurnNumber { get; }
    public string? ComboName { get; }
    public bool HasTellBonus { get; }
    public bool HasWeaknessWindow { get; }
}
```

## Input/Output Examples

### Example: Well-Formed LLM Response (after fix)

**Input to `ParseDialogueOptions`:**

```
OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
"hey so I noticed you're into marine biology… is that like, a career thing or more of a 'watches too many documentaries' thing"

OPTION_2
[STAT: HONESTY] [CALLBACK: turn_2] [COMBO: The Reveal] [TELL_BONUS: yes]
"okay real talk I looked at your profile for way too long and I have questions about the penguin photo"

OPTION_3
[STAT: CHAOS] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
"what if penguins had tinder. like what would their bios say. I need to know your thoughts on this"

OPTION_4
[STAT: WIT] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
"your bio says 'looking for someone who gets it' which is either deeply profound or deeply vague and I respect both options"
```

**Output:**

An array of 4 `DialogueOption` instances:

| Index | Stat | IntendedText | CallbackTurnNumber | ComboName | HasTellBonus | HasWeaknessWindow |
|-------|------|--------------|--------------------|-----------|--------------|-------------------|
| 0 | Charm | `"hey so I noticed you're into marine biology…"` (full text) | `null` | `null` | `false` | `false` |
| 1 | Honesty | `"okay real talk I looked at your profile…"` (full text) | `2` | `"The Reveal"` | `true` | `false` |
| 2 | Chaos | `"what if penguins had tinder…"` (full text) | `null` | `null` | `false` | `false` |
| 3 | Wit | `"your bio says 'looking for someone who gets it'…"` (full text) | `null` | `null` | `false` | `false` |

### Example: Current Broken Behavior (before fix)

Without the format block, the LLM generates freeform prose like:

```
Here are 4 options for Chad:

1. **Charm** - "hey so I noticed..." This option plays it safe...
2. **Honesty** - "okay real talk..." A bolder move that...
```

`ParseDialogueOptions` finds no `OPTION_N` headers → `parsed` list is empty → `PadToFour` returns 4 placeholder options with `IntendedText = "..."`.

## Acceptance Criteria

### AC1: `DialogueOptionsInstruction` includes explicit output format with OPTION_N headers and quoted text

The `PromptTemplates.DialogueOptionsInstruction` constant must be extended with an output format specification block that includes:

- The literal strings `OPTION_1`, `OPTION_2`, `OPTION_3`, `OPTION_4` as headers
- `[STAT: X]` metadata tag format on the line after each header
- `[CALLBACK: ...]`, `[COMBO: ...]`, `[TELL_BONUS: ...]` metadata tags
- Message text wrapped in double quotes on the line after metadata
- A complete example showing at least 2 options in the exact expected format

The format block must be appended **after** the existing instructional content (the 7 guidelines + metadata line + conciseness note). The existing instructional text must not be removed or altered in meaning.

### AC2: `ParseDialogueOptions` successfully parses the reformatted output in a unit test

A unit test must:

1. Provide a well-formed 4-option string matching the format described in AC1
2. Call `ParseDialogueOptions` with that string
3. Assert that exactly 4 `DialogueOption` objects are returned
4. Assert that each option has a non-empty `IntendedText` (not `"..."`)
5. Assert that the `Stat` property on each option matches the `[STAT: X]` tag in the input
6. Assert that optional metadata (`CallbackTurnNumber`, `ComboName`, `HasTellBonus`) is parsed correctly when present

### AC3: Integration test verifies 4 options with non-empty `IntendedText` from a real Anthropic call

An integration test must:

1. Call `GetDialogueOptionsAsync` on a live `AnthropicLlmAdapter` with a valid `DialogueContext`
2. Assert that the returned array has exactly 4 elements
3. Assert that all 4 have `IntendedText` that is not null, not empty, and not `"..."`

This test may be marked with a category/trait (e.g., `[Category("Integration")]`) so it can be excluded from CI runs that lack API keys.

### AC4: No regression on existing parse tests

All existing tests in the test suite must continue to pass. The change is purely additive to the constant's string value.

## Edge Cases

1. **LLM partially complies**: The LLM outputs only 2 or 3 options in the correct format. `ParseDialogueOptions` parses what it can and pads to 4 with default `"..."` options. This is existing behavior and must not change.

2. **LLM adds preamble text**: The LLM outputs explanatory text before `OPTION_1` (e.g., "Here are your options:"). The `OptionHeaderRegex.Split()` discards text before the first `OPTION_N` match. The format block should instruct "No extra text before OPTION_1 or after the last option" to minimize this.

3. **LLM uses smart quotes**: If the LLM outputs `"text"` (smart/curly quotes) instead of `"text"` (straight quotes), the `QuotedTextRegex` (`""([^""]+)""`) will **not** match because it looks for ASCII `"` (U+0022). The option will be skipped and padded. This is a known limitation; the format block uses straight quotes in its examples to guide the LLM.

4. **LLM outputs more than 4 options**: `ParseDialogueOptions` stops after collecting 4 valid options (`if (parsed.Count >= 4) break`). Extra options are silently discarded.

5. **Empty or null LLM response**: Returns 4 default-padded options with `IntendedText = "..."`. Existing behavior, unchanged.

6. **STAT value is not a valid `StatType`**: The option is skipped (the `Enum.Parse` catch block continues to next section). Padded with defaults.

7. **CALLBACK value is not `none` and not parseable as `turn_N` or integer**: `CallbackTurnNumber` is `null` for that option. No error.

## Error Conditions

`ParseDialogueOptions` **never throws**. All error paths result in padding with default options:

| Condition | Behavior |
|-----------|----------|
| `llmResponse` is `null` | Returns 4 default options (Charm, Honesty, Wit, Chaos stats, all `"..."`) |
| `llmResponse` is empty/whitespace | Same as null |
| No `OPTION_N` headers found | `sections` from regex split contains no parseable options → 4 defaults |
| Some options have invalid stat | Those options skipped, remaining parsed, padded to 4 |
| Any unexpected exception during parse | Caught by outer `catch` block, `parsed` list used as-is, padded to 4 |

For the template constant itself: it is a compile-time `const string`. If it fails to compile (e.g., unterminated verbatim string), the project will not build. This is caught at compile time, not runtime.

## Dependencies

- **No external dependencies** are introduced or changed.
- **No changes to `Pinder.Core`** — this fix is entirely within `Pinder.LlmAdapters`.
- **Architect contract**: `contracts/sprint-10-options-format-fix.md` defines the exact format block to append and confirms no changes to `ParseDialogueOptions` or `SessionDocumentBuilder`.
- **Downstream dependents**: Issues #241 and #242 depend on this fix being merged first. This issue has no upstream dependencies.

### Files Modified

| File | Change |
|------|--------|
| `src/Pinder.LlmAdapters/PromptTemplates.cs` | Append output format block to `DialogueOptionsInstruction` constant |
| `tests/Pinder.LlmAdapters.Tests/Anthropic/AnthropicLlmAdapterTests.cs` | Add unit tests for parse validation and template content assertions |

### Files NOT Modified

| File | Reason |
|------|--------|
| `AnthropicLlmAdapter.cs` | Parser already handles the expected format correctly |
| `SessionDocumentBuilder.cs` | Already substitutes `{player_name}` and appends instruction — no change needed |
| Any `Pinder.Core` file | Bug is entirely in the prompt template layer |
