# Contract: Issue #240 — DialogueOptionsInstruction Output Format Fix

## Component
`Pinder.LlmAdapters` — `PromptTemplates.DialogueOptionsInstruction` + parse validation

## Problem
`PromptTemplates.DialogueOptionsInstruction` tells the LLM to generate 4 options with metadata tags but does NOT specify the structured output format that `ParseDialogueOptions` expects. The parser looks for `OPTION_N` headers and `"quoted text"` — the prompt never asks for these. Result: all options parse as `"..."` placeholders.

## Changes Required

### 1. `PromptTemplates.DialogueOptionsInstruction` (Pinder.LlmAdapters/PromptTemplates.cs)

**Current**: Ends with "Keep options concise. One to three sentences. Match the opponent's register."

**New**: Append explicit output format block after the current text:

```
Output EXACTLY this format for each option (no deviations):

OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
"The exact text the character would send"

OPTION_2
[STAT: HONESTY] [CALLBACK: turn_2] [COMBO: The Reveal] [TELL_BONUS: yes]
"The exact text the character would send"

(continue for OPTION_3 and OPTION_4)

Rules:
- STAT must be one of: CHARM, RIZZ, HONESTY, CHAOS, WIT, SELF_AWARENESS
- Text must be in double quotes on the line immediately after the metadata
- No extra text before OPTION_1 or after the last option
```

### 2. No changes to `ParseDialogueOptions` (Pinder.LlmAdapters/Anthropic/AnthropicLlmAdapter.cs)

The parser already expects this format. The fix is purely in the prompt template.

### 3. No changes to `SessionDocumentBuilder.BuildDialogueOptionsPrompt`

It already substitutes `{player_name}` and appends the instruction. No structural change needed.

## Interface (unchanged)
```
ParseDialogueOptions(string? llmResponse) → DialogueOption[]
```
- Input: raw LLM text containing `OPTION_N` headers, `[STAT: X]` tags, `"quoted text"`
- Output: exactly 4 `DialogueOption` instances; pads with defaults if parsing yields <4
- Never throws

## Tests Required
1. Unit test: `ParseDialogueOptions` with well-formed 4-option text → 4 options with correct stats and non-empty `IntendedText`
2. Unit test: Verify `DialogueOptionsInstruction` contains "OPTION_1" and "OPTION_2" markers
3. Unit test: Verify `DialogueOptionsInstruction` contains output format rules (STAT must be one of...)

## Dependencies
None — this is the base fix. #241 and #242 depend on this.

## Files Changed
- `src/Pinder.LlmAdapters/PromptTemplates.cs` (DialogueOptionsInstruction constant)
- `tests/Pinder.LlmAdapters.Tests/Anthropic/AnthropicLlmAdapterTests.cs` (new parse tests)
