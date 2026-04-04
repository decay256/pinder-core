# Spec: Issue #489 — Prompt: voice distinctness — explicit texting style constraint before option generation

**Module**: `docs/modules/llm-adapters.md`

---

## Overview

When generating dialogue options for a player character, the LLM can drift away from the character's established texting style, producing generic Gen-Z text instead of the character's unique voice. This feature adds the character's texting style fragment to `CharacterProfile`, threads it through `DialogueContext` into `SessionDocumentBuilder`, and injects it as an explicit constraint block immediately before the task instruction in the dialogue-options user message. A voice-check reminder is also appended to `PromptTemplates.DialogueOptionsInstruction`.

This issue depends on #487 (voice bleed fix — player-only system blocks for dialogue options) being implemented first, as the texting style reinforcement amplifies the prompt separation that #487 establishes.

---

## Function Signatures

### 1. `CharacterProfile` (Pinder.Core.Characters)

**Existing class — new property and constructor parameter.**

```csharp
public sealed class CharacterProfile
{
    // ... all existing properties unchanged ...

    /// <summary>
    /// The texting style fragment(s) joined, for injection into
    /// option-generation prompts. Empty string if not available.
    /// </summary>
    public string TextingStyleFragment { get; }

    public CharacterProfile(
        StatBlock stats,
        string assembledSystemPrompt,
        string displayName,
        TimingProfile timing,
        int level,
        string bio = "",
        string textingStyleFragment = ""   // NEW — optional, backward-compatible
    )
}
```

- `TextingStyleFragment` is set to `textingStyleFragment ?? string.Empty` in the constructor body.
- The parameter MUST have a default value of `""` so all existing callers compile unchanged.

### 2. `DialogueContext` (Pinder.Core.Conversation)

**Existing class — new property and constructor parameter.**

```csharp
public sealed class DialogueContext
{
    // ... all existing properties unchanged ...

    /// <summary>
    /// The player character's texting style text, for voice reinforcement.
    /// Empty string means no texting style available.
    /// </summary>
    public string PlayerTextingStyle { get; }

    public DialogueContext(
        // ... all existing parameters unchanged ...
        string playerTextingStyle = ""     // NEW — appended after existing params
    )
}
```

- `PlayerTextingStyle` is set to `playerTextingStyle ?? ""` in the constructor body.
- The parameter MUST be appended after all existing optional parameters to preserve backward compatibility.

### 3. `GameSession.StartTurnAsync()` (Pinder.Core.Conversation)

**Existing method — wiring change only.**

When constructing the `DialogueContext` inside `StartTurnAsync()`, pass `_player.TextingStyleFragment` as the `playerTextingStyle` argument:

```csharp
var context = new DialogueContext(
    // ... all existing args ...
    playerTextingStyle: _player.TextingStyleFragment
);
```

No signature change to `StartTurnAsync()` itself.

### 4. `SessionDocumentBuilder.BuildDialogueOptionsPrompt()` (Pinder.LlmAdapters)

**Existing static method — no signature change.**

After all game-state sections and before the `"YOUR TASK"` line, inject the texting style block:

```
If context.PlayerTextingStyle is not null/empty:

  YOUR TEXTING STYLE — follow this exactly, no deviations:
  {context.PlayerTextingStyle}

```

The block MUST appear immediately before the `"YOUR TASK"` heading. If `PlayerTextingStyle` is empty or null, the block is omitted entirely.

### 5. `PromptTemplates.DialogueOptionsInstruction` (Pinder.LlmAdapters)

**Existing constant — text appended.**

Append the following voice-check instruction to the end of the existing `DialogueOptionsInstruction` constant:

```
Before writing each option, verify: does this sound exactly like
the texting style above? If not, rewrite it.
```

This text is appended after the existing rules/format block.

### 6. Session-runner loaders (session-runner/)

**`CharacterLoader`** — When parsing a pre-assembled prompt `.md` file, extract the `TEXTING STYLE` section content and pass it as the `textingStyleFragment` parameter to the `CharacterProfile` constructor. The `TEXTING STYLE` section is emitted by `PromptBuilder.BuildSystemPrompt()` at line 57-59 of `PromptBuilder.cs`.

**`CharacterDefinitionLoader`** — When running the full `CharacterAssembler` → `PromptBuilder` pipeline, join `FragmentCollection.TextingStyleFragments` with `" | "` and pass the result as `textingStyleFragment` to the `CharacterProfile` constructor.

---

## Input/Output Examples

### Example 1: Velvet character with texting style

**Input** — `CharacterProfile` constructed with:
```
textingStyleFragment: "lowercase-with-intent, precise, ironic, uses ellipses for dramatic pauses, never uses exclamation marks"
```

**DialogueContext constructed with:**
```
playerTextingStyle: "lowercase-with-intent, precise, ironic, uses ellipses for dramatic pauses, never uses exclamation marks"
```

**User message output** (relevant section, appearing after GAME STATE and before YOUR TASK):
```
YOUR TEXTING STYLE — follow this exactly, no deviations:
lowercase-with-intent, precise, ironic, uses ellipses for dramatic pauses, never uses exclamation marks

YOUR TASK
Generate exactly 4 dialogue options for Velvet.
...
Before writing each option, verify: does this sound exactly like
the texting style above? If not, rewrite it.
```

### Example 2: No texting style available (backward compat)

**Input** — `CharacterProfile` constructed with default (no `textingStyleFragment`):
```
textingStyleFragment: ""  (default)
```

**User message output**: No `YOUR TEXTING STYLE` block appears. The `YOUR TASK` section follows directly after game state. The voice-check line in `DialogueOptionsInstruction` still appears but has no effect since there is no style block above it.

### Example 3: Sable character

**Input** — `CharacterProfile` constructed with:
```
textingStyleFragment: "omg, 😭, fast-talk, run-on sentences, excessive emoji, ALL CAPS for emphasis"
```

**User message output** (relevant section):
```
YOUR TEXTING STYLE — follow this exactly, no deviations:
omg, 😭, fast-talk, run-on sentences, excessive emoji, ALL CAPS for emphasis

YOUR TASK
Generate exactly 4 dialogue options for Sable.
...
```

---

## Acceptance Criteria

### AC1: TEXTING STYLE block injected in BuildDialogueOptionsPrompt

**Given** `BuildDialogueOptionsPrompt` is called with a `DialogueContext` where `PlayerTextingStyle` is non-empty,
**When** the user message is assembled,
**Then** the block `YOUR TEXTING STYLE — follow this exactly, no deviations:` followed by the texting style text appears immediately before the `YOUR TASK` heading.

### AC2: TextingStyleFragment accessible on CharacterProfile

**Given** a `CharacterProfile` is constructed with `textingStyleFragment: "some style"`,
**When** `TextingStyleFragment` is read,
**Then** it returns `"some style"`.

**Given** a `CharacterProfile` is constructed without providing `textingStyleFragment`,
**When** `TextingStyleFragment` is read,
**Then** it returns `""` (empty string).

### AC3: Voice distinctness in generated options

**Given** a Velvet vs Sable session,
**When** options are generated for Velvet,
**Then** Velvet's texting style fragment is in the prompt (not Sable's). This is a qualitative verification via session playtest — the spec only requires the texting style to be correctly threaded into the prompt.

### AC4: Build clean, all tests pass

**Given** the changes are applied,
**When** the solution is built and all existing tests are run,
**Then** there are zero compilation errors and all existing tests pass unchanged. No new tests are required for prompt text content (qualitative), but structural tests verifying `CharacterProfile.TextingStyleFragment` property existence and `DialogueContext.PlayerTextingStyle` threading are expected.

---

## Edge Cases

| Case | Expected Behavior |
|------|-------------------|
| `textingStyleFragment` is `null` | `CharacterProfile.TextingStyleFragment` returns `""` (null-coalesced in constructor) |
| `textingStyleFragment` is `""` (empty) | No `YOUR TEXTING STYLE` block injected in user message |
| `textingStyleFragment` is whitespace-only (e.g. `"   "`) | Block IS injected (whitespace is non-empty). Implementer may optionally use `string.IsNullOrWhiteSpace` check — both behaviors are acceptable at prototype maturity |
| `playerTextingStyle` is `null` on `DialogueContext` | Coalesced to `""` in constructor; no block injected |
| `CharacterProfile` constructed with old signature (no `textingStyleFragment`) | Default `""` — fully backward-compatible |
| `DialogueContext` constructed with old signature (no `playerTextingStyle`) | Default `""` — fully backward-compatible |
| Multiple texting style fragments joined with `" | "` | Entire joined string is a single `TextingStyleFragment` value — no special handling needed |
| Very long texting style fragment (>1000 chars) | No truncation. Passed through verbatim. LLM context window is the only practical limit. |

---

## Error Conditions

| Condition | Expected Behavior |
|-----------|-------------------|
| `CharacterProfile` constructor receives `null` for `stats`, `assembledSystemPrompt`, `displayName`, or `timing` | Throws `ArgumentNullException` (existing behavior, unchanged) |
| `CharacterProfile` constructor receives `null` for `textingStyleFragment` | Coalesced to `""` — NO exception |
| `DialogueContext` constructor receives `null` for `playerTextingStyle` | Coalesced to `""` — NO exception |
| `SessionDocumentBuilder.BuildDialogueOptionsPrompt` receives `null` context | Throws `ArgumentNullException` (existing behavior, unchanged) |

There are no new exception types or error paths introduced by this feature. All changes are additive and null-safe.

---

## Dependencies

| Dependency | Type | Details |
|------------|------|---------|
| Issue #487 (voice bleed fix) | Must be implemented first | #487 switches `GetDialogueOptionsAsync` to player-only system blocks and moves opponent profile to user message. Texting style reinforcement (#489) layers on top of this prompt structure. Without #487, the texting style block would compete with the opponent's voice in the system prompt. |
| `Pinder.Core.Characters.CharacterProfile` | Modified | Gains `TextingStyleFragment` property |
| `Pinder.Core.Conversation.DialogueContext` | Modified | Gains `PlayerTextingStyle` property |
| `Pinder.Core.Conversation.GameSession` | Modified | Wires `_player.TextingStyleFragment` to `DialogueContext` |
| `Pinder.LlmAdapters.SessionDocumentBuilder` | Modified | Injects texting style block in `BuildDialogueOptionsPrompt` |
| `Pinder.LlmAdapters.PromptTemplates` | Modified | Voice-check text appended to `DialogueOptionsInstruction` |
| `session-runner/CharacterLoader` | Modified | Extracts texting style from prompt file |
| `session-runner/CharacterDefinitionLoader` | Modified | Joins `FragmentCollection.TextingStyleFragments` for `CharacterProfile` |
| `Pinder.Core.Characters.FragmentCollection` | Read-only | `TextingStyleFragments` property already exists — source data for the fragment |
| `Pinder.Core.Prompts.PromptBuilder` | Read-only | Already emits `TEXTING STYLE` section in system prompt (lines 57-59) — informs parsing strategy for `CharacterLoader` |

### Platform Constraints

- **netstandard2.0 + LangVersion 8.0** in Pinder.Core — no `record` types
- **Zero NuGet dependencies** in Pinder.Core
- **All existing tests must pass unchanged** — constructor changes use optional params with defaults
