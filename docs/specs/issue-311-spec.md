# Spec: Issue #311 — Tell categories not listed in opponent response prompt

**Module**: docs/modules/llm-adapters.md (create new)

## Overview

The `OpponentResponseInstruction` prompt template in `PromptTemplates.cs` instructs the LLM to generate optional TELL signals but does not provide the canonical tell category mappings from rules §15. Without this reference table, the LLM guesses which stat a tell maps to, producing inconsistent results that break the tell system. This issue adds the 10 tell-category mappings directly into the prompt template so the LLM produces rule-compliant tells.

## Function Signatures

No new functions or methods are introduced. The change is to the value of an existing `public const string` field:

```csharp
// File: src/Pinder.LlmAdapters/PromptTemplates.cs
namespace Pinder.LlmAdapters
{
    public static class PromptTemplates
    {
        /// <summary>
        /// §3.5 — Generate opponent response with optional [SIGNALS] block.
        /// </summary>
        public const string OpponentResponseInstruction = "...";  // modified constant
    }
}
```

**Type**: `public const string`
**Location**: `Pinder.LlmAdapters.PromptTemplates.OpponentResponseInstruction`

## Input/Output Examples

### Before (current template excerpt)

The `OpponentResponseInstruction` ends with:

```
Rules for signals:
- TELL line format: TELL: CHARM|RIZZ|HONESTY|CHAOS|WIT|SELF_AWARENESS (brief description)
- WEAKNESS line format: WEAKNESS: CHARM|RIZZ|HONESTY|CHAOS|WIT|SELF_AWARENESS -2 or -3 (brief description)
- Both lines are independently optional within a [SIGNALS] block
- Only include signals when the conversation naturally reveals them — do not force them
```

### After (with tell categories appended)

The template gains a tell category reference section after "Rules for signals:". The exact text to append:

```
Tell category reference (use ONLY these mappings):
- Opponent compliments player → TELL: HONESTY
- Opponent asks personal question → TELL: HONESTY or SELF_AWARENESS
- Opponent makes joke → TELL: WIT or CHAOS
- Opponent shares vulnerability → TELL: HONESTY
- Opponent pulls back/guards → TELL: SELF_AWARENESS
- Opponent tests/challenges → TELL: WIT or CHAOS
- Opponent sends short reply → TELL: CHARM or CHAOS
- Opponent flirts → TELL: RIZZ or CHARM
- Opponent changes subject → TELL: CHAOS
- Opponent goes quiet/silent → TELL: SELF_AWARENESS
```

### Example LLM output (conforming to updated prompt)

When the opponent flirts in their response, the signals block should now produce:

```
[SIGNALS]
TELL: RIZZ (she's being flirty, matching RIZZ or CHARM per the category table)
```

Not:

```
[SIGNALS]
TELL: HONESTY (she's being genuine about her attraction)
```

The second example is what the LLM might produce without the category table — it uses general reasoning instead of the defined mappings.

## Acceptance Criteria

### AC1: `OpponentResponseInstruction` includes the 10 tell category mappings

The `PromptTemplates.OpponentResponseInstruction` constant string must contain all 10 tell category mapping lines. These are the exact mappings from rules §15:

| Opponent behavior | Tell stat(s) |
|---|---|
| Compliments player | HONESTY |
| Asks personal question | HONESTY or SELF_AWARENESS |
| Makes joke | WIT or CHAOS |
| Shares vulnerability | HONESTY |
| Pulls back/guards | SELF_AWARENESS |
| Tests/challenges | WIT or CHAOS |
| Sends short reply | CHARM or CHAOS |
| Flirts | RIZZ or CHARM |
| Changes subject | CHAOS |
| Goes quiet/silent | SELF_AWARENESS |

The tell categories must appear as a clearly labeled reference section within the prompt. The stat names must use the exact uppercase format: `HONESTY`, `SELF_AWARENESS`, `WIT`, `CHAOS`, `CHARM`, `RIZZ`.

The section must include an explicit instruction that the LLM should use ONLY these mappings (not invent its own).

### AC2: Unit test verifies the tell category text is present in the template

A unit test must assert that `PromptTemplates.OpponentResponseInstruction` contains all 10 tell category mapping strings. The test should verify each mapping line individually (not a single substring for the entire block), so that if one mapping is accidentally removed, the test identifies exactly which one is missing.

Suggested assertions (one per mapping):
- Contains `"Opponent compliments player"` and `"HONESTY"`
- Contains `"Opponent asks personal question"` and `"HONESTY or SELF_AWARENESS"`
- Contains `"Opponent makes joke"` and `"WIT or CHAOS"`
- Contains `"Opponent shares vulnerability"` and `"HONESTY"`
- Contains `"Opponent pulls back/guards"` and `"SELF_AWARENESS"`
- Contains `"Opponent tests/challenges"` and `"WIT or CHAOS"`
- Contains `"Opponent sends short reply"` and `"CHARM or CHAOS"`
- Contains `"Opponent flirts"` and `"RIZZ or CHARM"`
- Contains `"Opponent changes subject"` and `"CHAOS"`
- Contains `"Opponent goes quiet/silent"` and `"SELF_AWARENESS"`

### AC3: Build clean

The solution must compile without errors or warnings. All existing tests (1718 baseline) must continue to pass.

## Edge Cases

- **No behavioral edge cases**: This is a static string constant change. There are no runtime inputs, no branching logic, and no dynamic behavior.
- **String encoding**: The constant uses C# verbatim string literal (`@"..."`). The tell category block must not introduce characters that break the verbatim string (e.g., unescaped double quotes must use `""` within the verbatim string).
- **Prompt length**: Adding ~10 lines of text increases the OpponentResponseInstruction by approximately 500 characters. This is well within Anthropic API limits and should not affect prompt caching behavior (the opponent response prompt is not in the cached system blocks — it's in the user message).

## Error Conditions

- **Compilation failure**: If the verbatim string literal is malformed (e.g., unescaped quotes), the build will fail. This is caught by AC3.
- **Missing mapping**: If any of the 10 mappings is omitted or misspelled, the unit test (AC2) will fail with a clear assertion message identifying the missing mapping.
- **No runtime errors possible**: The field is a compile-time constant (`const string`). It cannot be null, cannot throw, and cannot fail at runtime.

## Dependencies

- **No code dependencies**: This issue has no prerequisite issues. It modifies only a static string constant.
- **Architecture contract**: `contracts/sprint-12-rules-compliance-round2.md` §311 defines the exact tell category text to insert.
- **Rules source**: The 10 tell categories come from rules §15 (game design document, external to this repo).
- **Consumed by**: `AnthropicLlmAdapter` uses `PromptTemplates.OpponentResponseInstruction` when calling `ILlmAdapter.GetOpponentResponseAsync()`. The adapter passes this instruction to `SessionDocumentBuilder`, which embeds it in the prompt sent to the Anthropic Messages API.
- **No Pinder.Core changes**: This issue is entirely within `Pinder.LlmAdapters`. The `Pinder.Core` project is not modified.
