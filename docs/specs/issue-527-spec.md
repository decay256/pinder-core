# Spec: Session runner: bio as bold italic paragraph, not table row

**Issue:** #527  
**Module:** `docs/modules/session-runner.md`

---

## Overview

The playtest output from `session-runner` currently renders character bios as a row in the markdown stat table. This format treats the bio as tabular data, making it hard to read. This feature moves the bios out of the characters table and renders them as bold italic paragraphs immediately above the table.

## Function Signatures

This feature does not introduce new classes or functions; it modifies the inline console output formatting logic in `session-runner/Program.cs`.

## Input/Output Examples

**Previous Output**:
```markdown
## Characters

| | **Gerald_42** | **Velvet_Void** |
|---|---|---|
| Bio | "Just a normal guy who loves the gym, good food, and real connections." | "I will absolutely judge your taste in music." |
| Level | 5 | 7 |
```

**New Output**:
```markdown
## Characters

***Gerald_42 bio:*** *"Just a normal guy who loves the gym, good food, and real connections."*
***Velvet_Void bio:*** *"I will absolutely judge your taste in music."*

| | **Gerald_42** | **Velvet_Void** |
|---|---|---|
| Level | 5 | 7 |
```

## Acceptance Criteria

- **Bio as bold italic paragraph**: The player and opponent bios are printed immediately before the stat table using the exact format specified by the architecture contract: `***{Player} bio:*** *{Bio text}*`. (If quotes are desired, `***{Player} bio:*** *"{Bio text}"*` is acceptable).
- **Bio row removed**: The `| Bio | "{bio}" | "{bio}" |` row is removed entirely from the `## Characters` markdown table.
- **Build clean**: The changes compile without warnings or errors.

## Edge Cases

- **Empty Bio**: If the bio text is empty or missing, it should still render `***{Player} bio:*** **` or `***{Player} bio:*** *""*`, preserving the format without crashing.
- **Special characters in Bio**: Any markdown or quotes within the bio must be output as-is, safely interpolated.

## Error Conditions

- None expected. This is a pure string formatting change within `Console.WriteLine` statements.

## Dependencies

- Complies with `contracts/sprint-11-session-runner-ux.md` which mandates the format: `***{Player} bio:*** *{Bio text}*`.
- Modifies `session-runner/Program.cs`.
