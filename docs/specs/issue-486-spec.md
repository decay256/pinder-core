# Specification: Session Runner Intended vs Delivered Diff (Issue #486)

**Module**: docs/modules/session-runner.md

## Overview

When a player makes a choice, their selected option includes `IntendedText`. Depending on the outcome of the roll, the LLM may modify this intended message during the delivery phase (e.g., adding nervous hesitation on a failure, or confident embellishments on a strong success) resulting in `DeliveredMessage`. This feature updates the `session-runner` playtest output to show both the original intended message and the modified delivered message whenever they differ. This makes it transparent to playtesters how the rules engine and LLM are transforming character actions.

## Function Signatures

No new public functions are introduced in `Pinder.Core`. The logic is entirely contained within the `session-runner` console application's output formatting layer.

- **Component**: `session-runner/Program.cs`
- **Dependencies**: 
  - `chosenOption.IntendedText` (string)
  - `result.DeliveredMessage` (string)

## Input/Output Examples

**Input Data (Difference Detected):**
- `chosenOption.IntendedText` = `"that's not nothing"`
- `result.DeliveredMessage` = `"that's not nothing well i mean it's not everything either but you know what i mean"`

**Output Format:**
```markdown
**📨 Velvet sends:**
> *Intended: "that's not nothing"*
> *Delivered:*
> that's not nothing well i mean it's not everything either but you know what i mean
```

**Input Data (No Difference Detected):**
- `chosenOption.IntendedText` = `"she never had to learn"`
- `result.DeliveredMessage` = `"she never had to learn"`

**Output Format:**
```markdown
**📨 Velvet sends:**
> she never had to learn
```

## Acceptance Criteria

*(Note: Per the Architect's Contract for Sprint 11, the inline diff formatting using strikethrough/italics has been simplified. The session runner will simply display both messages clearly separated rather than attempting a text diff algorithm.)*

### 1. Clean success (or exact match)
When `result.DeliveredMessage.Trim()` equals `chosenOption.IntendedText.Trim()`, only the delivered message is displayed.

### 2. Strong success / Nat 20 / Transformed Messages
When the delivered message differs from the intended message (e.g., strong success margin 5+ or Nat 20), both are displayed. The blockquote must be prefixed with `*Intended: "{chosenOption.IntendedText}"*` on one line, followed by `*Delivered:*` on the next.

### 3. Fail tiers / Nat 1
When a fumble, misfire, trope trap, catastrophe, or legendary failure modifies the intended text, both the intended and the delivered texts are printed to the playtest log using the same prefix format.

### 4. Skip Empty Intention Placeholder
If the intended text is exactly `"..."` (used as a fallback or placeholder), the diff logic must be skipped, and only the delivered message is shown.

## Edge Cases

- **Whitespace Changes**: Leading or trailing whitespace should not trigger the diff display. The comparison must use `.Trim()`.
- **Placeholder Intended Text**: If `chosenOption.IntendedText` is `"..."`, the diff format is suppressed to avoid unhelpful output like `*Intended: "..."*`.
- **Null Values**: If either text is null or empty, the logic must handle it gracefully without throwing a null reference exception.

## Error Conditions

- There are no structural error conditions since this is a UI formatting change in the console runner.
- If the output formatting logic throws an exception, the host application will crash. Ensure null-safety on strings.

## Dependencies

- **`session-runner/Program.cs`**: The main host loop and standard output formatter.
- Relies on `DialogueOption.IntendedText` and `TurnResult.DeliveredMessage` being accurately populated by `GameSession` and the `ILlmAdapter`.
