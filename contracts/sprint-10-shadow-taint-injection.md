# Contract: Issue #242 — Shadow Taint Injection into LLM Prompts

## Component
`Pinder.LlmAdapters` (PromptTemplates, SessionDocumentBuilder) + `Pinder.Core` (GameSession context wiring)

## Problem
Shadow threshold effects (§3.6 taint) are never injected into LLM prompts. When a character has Madness ≥ 6, their messages should carry an "uncanny quality" — but `SessionDocumentBuilder` has no shadow awareness and `PromptTemplates` has no taint text constants.

## Changes Required

### 1. `PromptTemplates` — Add Shadow Taint Constants (Pinder.LlmAdapters/PromptTemplates.cs)

Add taint text constants per shadow stat per threshold tier. These are injected into prompts when the corresponding threshold is reached.

```csharp
// --- Shadow Taint Blocks (§3.6) ---

// Madness (paired with Charm)
internal const string MadnessTaint1 = "SHADOW TAINT — Madness (Tier 1): Messages carry an uncanny quality. Word choices are slightly off — technically correct but with an alien register. Like someone learned charm from a manual.";
internal const string MadnessTaint2 = "SHADOW TAINT — Madness (Tier 2): The uncanny is now obvious. Sentences loop or contradict. The character's charm has a glitching quality — warm one word, hollow the next.";
internal const string MadnessTaint3 = "SHADOW TAINT — Madness (Tier 3): Full dissociation in text. Messages read like someone else is writing them. The charm is a mask and the mask is slipping.";

// Horniness (paired with Rizz)
internal const string HorninessTaint1 = "SHADOW TAINT — Horniness (Tier 1): A slightly desperate edge. Compliments land a beat too heavy. Double entendres appear uninvited.";
internal const string HorninessTaint2 = "SHADOW TAINT — Horniness (Tier 2): Thirst is visible. Messages steer toward physical topics. Attempts at subtlety are failing.";
internal const string HorninessTaint3 = "SHADOW TAINT — Horniness (Tier 3): All pretense abandoned. Every message is a thinly veiled proposition. The character can't help themselves.";

// Denial (paired with Honesty)
internal const string DenialTaint1 = "SHADOW TAINT — Denial (Tier 1): Minor self-contradictions. Claims to be fine when clearly not. The character hedges truths reflexively.";
internal const string DenialTaint2 = "SHADOW TAINT — Denial (Tier 2): Active rewriting of reality. The character flat-out contradicts earlier statements and seems to believe it. Gaslighting energy.";
internal const string DenialTaint3 = "SHADOW TAINT — Denial (Tier 3): The character lives in an alternate timeline. Nothing they say matches observable reality. It's not lying — they genuinely believe it.";

// Fixation (paired with Chaos)
internal const string FixationTaint1 = "SHADOW TAINT — Fixation (Tier 1): One topic keeps resurfacing. The character circles back to something specific even when the conversation moves on.";
internal const string FixationTaint2 = "SHADOW TAINT — Fixation (Tier 2): Obsessive loops. The character cannot let go of a subject. Every response bends back to their fixation.";
internal const string FixationTaint3 = "SHADOW TAINT — Fixation (Tier 3): Mono-topic. The character can only talk about one thing. All roads lead to their obsession.";

// Dread (paired with Wit)
internal const string DreadTaint1 = "SHADOW TAINT — Dread (Tier 1): A melancholy undertone. Jokes have a dark edge. The character's wit carries existential weight.";
internal const string DreadTaint2 = "SHADOW TAINT — Dread (Tier 2): The humor is gone. What passes for wit is now bleak observation. The character sees futility in everything.";
internal const string DreadTaint3 = "SHADOW TAINT — Dread (Tier 3): Full nihilistic spiral. The character questions why they're even on this app. Messages read like resignation letters to hope.";

// Overthinking (paired with SelfAwareness)
internal const string OverthinkingTaint1 = "SHADOW TAINT — Overthinking (Tier 1): Messages are slightly too long. The character over-explains, adds caveats, second-guesses their own points.";
internal const string OverthinkingTaint2 = "SHADOW TAINT — Overthinking (Tier 2): Paralysis by analysis. Messages contain multiple contradictory interpretations of the same thing. The character is arguing with themselves.";
internal const string OverthinkingTaint3 = "SHADOW TAINT — Overthinking (Tier 3): Full meta-spiral. The character is analyzing why they're analyzing. Messages are nested self-reflections. They can't just *say* something.";
```

Add a public accessor method:

```csharp
/// <summary>
/// Returns the shadow taint instruction for a given shadow stat and threshold tier.
/// Returns null if tier is 0 (no threshold reached).
/// </summary>
public static string? GetShadowTaintText(ShadowStatType shadow, int tier)
```

### 2. `SessionDocumentBuilder` — Accept and render shadow taint (Pinder.LlmAdapters/SessionDocumentBuilder.cs)

#### `BuildDialogueOptionsPrompt` — add optional parameter

```csharp
public static string BuildDialogueOptionsPrompt(
    IReadOnlyList<(string Sender, string Text)> conversationHistory,
    string opponentLastMessage,
    string[] activeTraps,
    int currentInterest,
    int currentTurn,
    string playerName,
    string opponentName,
    Dictionary<ShadowStatType, int>? shadowThresholds = null)  // NEW optional param
```

When `shadowThresholds` is non-null and contains any tier > 0, append a `SHADOW TAINT` section before `YOUR TASK`:

```
SHADOW TAINT
{taint text for each shadow with tier > 0}
```

#### `BuildDeliveryPrompt` — add optional parameter

```csharp
public static string BuildDeliveryPrompt(
    ...,
    string playerName,
    string opponentName,
    Dictionary<ShadowStatType, int>? shadowThresholds = null)  // NEW optional param
```

Same rendering logic — append shadow taint section when applicable.

#### `BuildOpponentPrompt` — add optional parameter

```csharp
public static string BuildOpponentPrompt(
    ...,
    string playerName,
    string opponentName,
    Dictionary<ShadowStatType, int>? opponentShadowThresholds = null)  // NEW optional param
```

Note: for opponent prompts, shadow thresholds should be the **opponent's** shadows (affects how the opponent's messages read). This requires `GameSession` to compute opponent shadow thresholds.

### 3. `AnthropicLlmAdapter` — Pass shadow thresholds through (Pinder.LlmAdapters/Anthropic/AnthropicLlmAdapter.cs)

Update each method to pass `context.ShadowThresholds` to the corresponding builder call:

```csharp
// In GetDialogueOptionsAsync:
var userContent = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
    ..., shadowThresholds: context.ShadowThresholds);

// In DeliverMessageAsync:
var userContent = SessionDocumentBuilder.BuildDeliveryPrompt(
    ..., shadowThresholds: context.ShadowThresholds);

// In GetOpponentResponseAsync:
var userContent = SessionDocumentBuilder.BuildOpponentPrompt(
    ..., opponentShadowThresholds: context.ShadowThresholds);
```

### 4. `GameSession` — Wire shadow thresholds to DeliveryContext and OpponentContext

`DialogueContext` already receives `shadowThresholds` (line ~269). But `DeliveryContext` and `OpponentContext` do not.

**DeliveryContext (line ~503)**: Add `shadowThresholds: shadowThresholds` (variable already computed at line ~227).

**OpponentContext (line ~538)**: Compute opponent shadow thresholds (new code, similar to player shadow computation at line ~230 but using `_opponentShadows`), pass as `shadowThresholds`.

## Interface Changes

### `PromptTemplates` — new public method
```
GetShadowTaintText(ShadowStatType shadow, int tier) → string?
```
- Pre: tier 0–3
- Post: returns taint instruction text, or null if tier == 0
- Pure function, no side effects

### `SessionDocumentBuilder` — signature extensions (backward compatible)
All three build methods gain `Dictionary<ShadowStatType, int>? shadowThresholds = null` as trailing optional parameter. Existing callers (including tests) are unaffected.

### `DeliveryContext` / `OpponentContext` — already have `ShadowThresholds` property
Both DTOs already have `Dictionary<ShadowStatType, int>? ShadowThresholds` fields. GameSession just needs to populate them.

## Tests Required
1. Unit test: `PromptTemplates.GetShadowTaintText` returns correct text per shadow/tier combo
2. Unit test: `PromptTemplates.GetShadowTaintText` returns null for tier 0
3. Unit test: `BuildDialogueOptionsPrompt` with shadowThresholds includes "SHADOW TAINT" section
4. Unit test: `BuildDialogueOptionsPrompt` without shadowThresholds does NOT include "SHADOW TAINT"
5. Unit test: `BuildDeliveryPrompt` with shadowThresholds includes taint text
6. Unit test: `BuildOpponentPrompt` with opponent shadowThresholds includes taint text

## Dependencies
- #240 must land first (prompt format fix)
- Independent of #241 (can be implemented in parallel after #240)

## Files Changed
- `src/Pinder.LlmAdapters/PromptTemplates.cs` (taint constants + GetShadowTaintText)
- `src/Pinder.LlmAdapters/SessionDocumentBuilder.cs` (optional shadowThresholds param + rendering)
- `src/Pinder.LlmAdapters/Anthropic/AnthropicLlmAdapter.cs` (pass-through of shadow thresholds)
- `src/Pinder.Core/Conversation/GameSession.cs` (wire shadowThresholds to DeliveryContext/OpponentContext)
- Tests in Pinder.LlmAdapters.Tests
