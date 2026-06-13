# Spec: Datee Resistance Below Interest 25

**Issue**: #490  
**Module**: `docs/modules/llm-adapters.md` (existing — `Pinder.LlmAdapters` prompt engineering)

---

## Overview

The datee character in a Pinder conversation should maintain varying degrees of resistance at all interest levels below 25 (DateSecured). Currently, the datee prompt includes a `GetInterestBehaviourBlock()` that describes engagement level but does not explicitly frame the datee as being *in opposition* to the player. This feature adds a **resistance rule** and **per-interest-band resistance descriptors** to the datee prompt so that the LLM generates responses with dramatic tension — the datee is never a willing collaborator until the player earns Interest 25.

This is a prompt engineering change. No game mechanics, roll math, or interest system logic are modified. All changes are confined to `Pinder.LlmAdapters` (`PromptTemplates` and `SessionDocumentBuilder`).

---

## Function Signatures

### `PromptTemplates` (static class in `Pinder.LlmAdapters`)

```csharp
/// <summary>
/// Fundamental resistance rule text injected into every datee response prompt.
/// States that below Interest 25, the datee has NOT been won over and must
/// maintain resistance appropriate to the current interest level.
/// </summary>
public const string DateeResistanceRule;
```

**Type**: `string` (compile-time constant)

**Content requirements**: The constant must communicate these principles:
1. Below Interest 25, the datee is not won over. They are evaluating, skeptical, guarded, or at best cautiously warming.
2. Resistance is not hostility — it is the natural social friction of someone who hasn't decided yet.
3. The datee should never volunteer vulnerability, make plans, or signal commitment below 25.
4. At Interest 25 (DateSecured), resistance dissolves genuinely — not as a switch flip, but as earned arrival.

---

```csharp
/// <summary>
/// Returns a resistance descriptor string appropriate for the given interest level.
/// The descriptor tells the LLM how much resistance the datee should express.
/// </summary>
/// <param name="interest">Current interest value (0–25).</param>
/// <returns>A multi-sentence string describing the datee's resistance posture.</returns>
public static string GetResistanceDescriptor(int interest);
```

**Return type**: `string`  
**Parameter**: `int interest` — the current interest meter value (0–25 inclusive)

**Interest band → resistance descriptor mapping**:

| Interest | Band Name | Resistance Posture |
|----------|-----------|-------------------|
| 0 | Unmatched | No resistance — already gone. Datee is unmatching or has left. |
| 1–4 | Bored | Active disengagement. Short replies, visible disinterest. The datee is looking for a reason to stop talking. They may test the player or send closing signals. |
| 5–9 | Lukewarm | Polite but unconvinced. The datee engages minimally, keeps things surface-level. There's no hostility but no warmth either. They're giving the player a chance but aren't investing. |
| 10–14 | Interested (lower) | Warmth with visible holdback. The datee is engaged and responsive but catches themselves — pulls back from anything too eager, deflects personal questions, keeps a slight guard up. |
| 15–20 | Interested (upper) / VeryIntoIt | Genuinely enjoying the conversation but still maintains boundaries. Flirtation is present but the datee doesn't fully commit. They test the player, create small challenges, hold back just enough to preserve tension. |
| 21–24 | AlmostThere | Resistance is subtle but present. The datee is clearly into it — replies are longer, warmer, more personal — but there's a last layer of self-protection. They won't be the one to suggest the date. The player must earn the final step. |
| 25 | DateSecured | Resistance dissolves genuinely. The datee has been won over. They can now be fully warm, suggest plans, express real enthusiasm. This feels earned, not sudden. |

---

### `SessionDocumentBuilder` (static class in `Pinder.LlmAdapters`)

**Modified method**:

```csharp
/// <summary>
/// Builds the user-message content for GetDateeResponseAsync (§3.5).
/// Now includes a RESISTANCE STANCE section between CURRENT INTEREST STATE
/// and the DateeResponseInstruction.
/// </summary>
/// <param name="context">The DateeContext carrying all turn data.</param>
/// <returns>Assembled user-message string for the LLM.</returns>
public static string BuildDateePrompt(DateeContext context);
```

**Return type**: `string`  
**Parameter**: `DateeContext context` (unchanged — no new fields needed for this feature; `InterestAfter` already exists on the DTO)

The method injects a new **RESISTANCE STANCE** section into the user message. The section is placed after the existing "CURRENT INTEREST STATE" block and before the final `DateeResponseInstruction`.

---

## Input/Output Examples

### Example 1: Interest = 3 (Bored)

**Input**: `DateeContext` with `InterestAfter = 3`

**Injected block in user message** (after CURRENT INTEREST STATE):

```
RESISTANCE STANCE
Below Interest 25, you have NOT been won over. You are evaluating this person.
Your resistance is not hostility — it is the natural friction of someone who hasn't decided.
Do not volunteer vulnerability, make concrete plans, or signal commitment.

Current interest: 3/25. Resistance level: Active disengagement. Short replies, visible disinterest. You are looking for a reason to stop talking. You may test the player or send closing signals.
```

### Example 2: Interest = 12 (Interested, lower band)

**Input**: `DateeContext` with `InterestAfter = 12`

**Injected block**:

```
RESISTANCE STANCE
Below Interest 25, you have NOT been won over. You are evaluating this person.
Your resistance is not hostility — it is the natural friction of someone who hasn't decided.
Do not volunteer vulnerability, make concrete plans, or signal commitment.

Current interest: 12/25. Resistance level: Warmth with visible holdback. You are engaged and responsive but catch yourself — pull back from anything too eager, deflect personal questions, keep a slight guard up.
```

### Example 3: Interest = 22 (AlmostThere)

**Input**: `DateeContext` with `InterestAfter = 22`

**Injected block**:

```
RESISTANCE STANCE
Below Interest 25, you have NOT been won over. You are evaluating this person.
Your resistance is not hostility — it is the natural friction of someone who hasn't decided.
Do not volunteer vulnerability, make concrete plans, or signal commitment.

Current interest: 22/25. Resistance level: Resistance is subtle but present. You are clearly into it — replies are longer, warmer, more personal — but there's a last layer of self-protection. You won't be the one to suggest the date. The player must earn the final step.
```

### Example 4: Interest = 25 (DateSecured)

**Input**: `DateeContext` with `InterestAfter = 25`

**Injected block**:

```
RESISTANCE STANCE
You have been won over. Interest is 25/25.
Resistance dissolves genuinely. You can now be fully warm, suggest plans, express real enthusiasm. This feels earned, not sudden.
```

---

## Acceptance Criteria

### AC1: DateeResponseInstruction includes fundamental resistance rule

**Given** `PromptTemplates.DateeResistanceRule` exists as a public constant,  
**When** `SessionDocumentBuilder.BuildDateePrompt()` assembles the user message,  
**Then** the `DateeResistanceRule` text is included in the RESISTANCE STANCE section of the output string.

**Verification**: Assert that the return value of `BuildDateePrompt()` contains `PromptTemplates.DateeResistanceRule` (or its key phrases).

### AC2: Interest 1–4 shows active disengagement

**Given** `DateeContext.InterestAfter` is in the range 1–4,  
**When** `BuildDateePrompt()` is called,  
**Then** the output contains a resistance descriptor indicating active disengagement (e.g., the string returned by `GetResistanceDescriptor(interestValue)` for values 1–4 must mention disengagement, short replies, or looking for a reason to stop talking).

### AC3: Interest 10–14 shows warmth with holdback

**Given** `DateeContext.InterestAfter` is in the range 10–14,  
**When** `BuildDateePrompt()` is called,  
**Then** the output contains a resistance descriptor indicating warmth with visible holdback.

### AC4: Interest 21–24 shows subtle resistance

**Given** `DateeContext.InterestAfter` is in the range 21–24,  
**When** `BuildDateePrompt()` is called,  
**Then** the output contains a resistance descriptor indicating subtle but present resistance — the datee won't suggest the date.

### AC5: Interest 25 dissolves resistance

**Given** `DateeContext.InterestAfter` is 25,  
**When** `BuildDateePrompt()` is called,  
**Then** the output contains a descriptor indicating resistance has dissolved genuinely and the datee can express full warmth and suggest plans.

### AC6: Build clean, all tests pass

**Given** the changes are applied,  
**When** `dotnet build` and `dotnet test` are run,  
**Then** the build succeeds with no errors and all existing tests (2295+) pass unchanged. New tests for resistance descriptor logic also pass.

---

## Edge Cases

### Interest exactly at band boundaries

| Interest | Expected Band |
|----------|--------------|
| 0 | Unmatched (gone) |
| 1 | Bored (active disengagement) |
| 4 | Bored (active disengagement) |
| 5 | Lukewarm |
| 9 | Lukewarm |
| 10 | Interested (lower — warmth with holdback) |
| 14 | Interested (lower — warmth with holdback) |
| 15 | Interested (upper) / VeryIntoIt (boundaries, testing) |
| 20 | VeryIntoIt |
| 21 | AlmostThere (subtle resistance) |
| 24 | AlmostThere (subtle resistance) |
| 25 | DateSecured (resistance dissolves) |

### Interest outside expected range

- `interest < 0`: Should be treated as 0 (Unmatched). `InterestMeter` clamps to `[0, 25]`, so this is unlikely but `GetResistanceDescriptor` should handle it gracefully (return the Unmatched descriptor).
- `interest > 25`: Should be treated as 25 (DateSecured). Same clamping logic applies.

### Empty or null DateeContext fields

- `DateeContext.InterestAfter` is always an `int` (non-nullable), so no null check needed.
- The resistance block is always injected — there is no "skip" condition other than the content varying by interest level.

### Existing `GetInterestBehaviourBlock()` interaction

- The existing `GetInterestBehaviourBlock()` already produces engagement-level descriptions. The new RESISTANCE STANCE section is **additive** — it appears as a separate block after CURRENT INTEREST STATE. Both blocks are present in the output. They serve complementary purposes: `GetInterestBehaviourBlock` describes engagement *behavior* (reply speed, length), while the resistance descriptor frames *opposition posture* (guard level, what the datee will/won't do).

---

## Error Conditions

### No new error paths

- `GetResistanceDescriptor(int interest)` is a pure function that returns a string for any integer input. It cannot fail.
- `DateeResistanceRule` is a compile-time constant. It cannot be null.
- `BuildDateePrompt()` already validates `context != null` via `ArgumentNullException`. No additional validation is needed.

### Backward compatibility

- No DTO changes are required — `DateeContext.InterestAfter` already exists.
- No constructor signature changes to any DTO.
- No changes to `ILlmAdapter` interface.
- The only behavioral change is that `BuildDateePrompt()` returns a longer string with the RESISTANCE STANCE section included. Existing callers (`AnthropicLlmAdapter.GetDateeResponseAsync()`) pass this string as the user message to the Anthropic API — the additional content is transparently included.

---

## Dependencies

### Internal (Pinder.Core)

- `DateeContext` (Conversation/) — read-only dependency on `InterestAfter` property. **No changes to this class.**
- `InterestState` enum (Conversation/) — not directly used by this feature (resistance bands are defined independently in `GetResistanceDescriptor`), but the bands should align conceptually with `InterestState` values.

### Internal (Pinder.LlmAdapters)

- `PromptTemplates` — modified (new constant + new static method)
- `SessionDocumentBuilder.BuildDateePrompt()` — modified (new section injected)

### External

- None. No new NuGet packages. No new projects. No new files beyond the changes to existing `PromptTemplates.cs` and `SessionDocumentBuilder.cs`.

### Sprint dependencies

- This issue (#490) has **no dependencies** on other issues in the sprint. It can be implemented in Wave 1 (parallel with #487, #491, #493).
- #489 (texting style) and #492 (LlmPlayerAgent) do NOT depend on this issue.

---

## Section Placement in `BuildDateePrompt` Output

For clarity, the full section order after implementation:

1. `CONVERSATION HISTORY` (existing)
2. `PLAYER'S LAST MESSAGE` (existing)
3. `INTEREST CHANGE` (existing)
4. `RESPONSE TIMING` (existing)
5. `CURRENT INTEREST STATE` (existing — `GetInterestBehaviourBlock`)
6. `ACTIVE TRAP INSTRUCTIONS` (existing, conditional)
7. `SHADOW STATE` (existing, conditional)
8. **`RESISTANCE STANCE`** (NEW — always present, content varies by interest)
9. `DateeResponseInstruction` (existing — final instruction block)
