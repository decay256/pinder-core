# Contract: Sprint — Dramatic Arc + Voice Fixes

## Architecture Overview

**This sprint continues the existing architecture with no structural changes.** All changes target prompt engineering in `Pinder.LlmAdapters` (PromptTemplates, SessionDocumentBuilder, CacheBlockBuilder, AnthropicLlmAdapter) plus minor DTO extensions in `Pinder.Core` (CharacterProfile, OpponentContext) and a rework of `LlmPlayerAgent` in `session-runner/`.

**Existing architecture summary**: Pinder.Core is a zero-dependency .NET Standard 2.0 RPG engine. `GameSession` orchestrates single-conversation turns, calling `ILlmAdapter` methods with context DTOs (`DialogueContext`, `DeliveryContext`, `OpponentContext`). `Pinder.LlmAdapters` implements `ILlmAdapter` via `AnthropicLlmAdapter`, which uses `SessionDocumentBuilder` for prompt assembly, `CacheBlockBuilder` for Anthropic prompt caching, and `PromptTemplates` for instruction text constants. The dependency is strictly one-way: `LlmAdapters → Core`. The `session-runner/` is a .NET 8 console app with player agent types (`IPlayerAgent`, `ScoringPlayerAgent`, `LlmPlayerAgent`).

### Components being extended

- `CacheBlockBuilder` (LlmAdapters) — #487: `GetDialogueOptionsAsync` switches from `BuildCachedSystemBlocks` (both prompts) to `BuildPlayerOnlySystemBlocks` (player only)
- `SessionDocumentBuilder` (LlmAdapters) — #487: injects opponent profile into user message; #489: injects texting style block before task; #490: adds resistance descriptor to opponent prompt; #493: injects failure context into opponent prompt
- `PromptTemplates` (LlmAdapters) — #489: voice check instruction; #490: resistance rule + descriptors; #491: revised success delivery tiers; #493: per-tier opponent reaction guidance
- `AnthropicLlmAdapter` (LlmAdapters) — #487: `GetDialogueOptionsAsync` uses player-only system blocks
- `CharacterProfile` (Core) — #489/#492: gains `TextingStyleFragment` property
- `OpponentContext` (Core) — #493: gains `DeliveryTier` field
- `GameSession` (Core) — #493: passes `FailureTier` to `OpponentContext`
- `LlmPlayerAgent` (session-runner) — #492: gains character context (system prompt, texting style, conversation history, scoring agent EV advisory)

### What is NOT changing

- All Pinder.Core game logic (Stats, Rolls, Traps, Progression, Data)
- GameSession public API (no new public methods — only wiring changes)
- `GetOpponentResponseAsync` and `DeliverMessageAsync` system block strategy (both already correct)
- NullLlmAdapter
- Pinder.Rules
- Existing test behavior (all DTO changes use optional params with defaults)

### Implicit assumptions for implementers

1. **netstandard2.0 + LangVersion 8.0** in Pinder.Core — no `record` types
2. **Zero NuGet dependencies in Pinder.Core**
3. **All 2295 existing tests must pass unchanged**
4. **Context DTO changes use optional constructor params with defaults** — backward-compatible
5. **PromptBuilder already emits TEXTING STYLE section** — CharacterProfile.TextingStyleFragment is extracted from the assembled prompt or passed through from fragments
6. **`FailureTier.None` means success** — existing convention in DeliveryContext
7. **LlmPlayerAgent already exists** — #492 enhances it, not creates it
8. **Qualitative LLM output** — voice distinctness, resistance, delivery quality are prompt engineering. Verification is via session playtest, not automated tests.

---

## Issue #487 — Voice bleed fix (player-only system blocks for options)

### Components

- `AnthropicLlmAdapter.GetDialogueOptionsAsync()` (LlmAdapters)
- `CacheBlockBuilder` (LlmAdapters) — no changes needed, `BuildPlayerOnlySystemBlocks` already exists
- `SessionDocumentBuilder.BuildDialogueOptionsPrompt()` (LlmAdapters)
- `DialogueContext` (Core) — already has `OpponentPrompt` field, no change needed

### Interface changes

**`AnthropicLlmAdapter.GetDialogueOptionsAsync()`** — change only:

```csharp
// BEFORE:
var systemBlocks = CacheBlockBuilder.BuildCachedSystemBlocks(
    context.PlayerPrompt, context.OpponentPrompt);
// AFTER:
var systemBlocks = CacheBlockBuilder.BuildPlayerOnlySystemBlocks(
    context.PlayerPrompt);
```

**`SessionDocumentBuilder.BuildDialogueOptionsPrompt()`** — add opponent profile section in user message:

```csharp
// After CONVERSATION HISTORY, before GAME STATE, insert:
sb.AppendLine();
sb.AppendLine("OPPONENT PROFILE (for context — do NOT adopt this voice)");
sb.AppendLine($"You are talking to {opponentName}. Their profile:");
sb.AppendLine(context.OpponentPrompt);
sb.AppendLine();
sb.AppendLine("Use this to understand what would land with them. Do NOT write in their voice.");
```

### Behavioral contract

- **Pre-condition**: `DialogueContext` has both `PlayerPrompt` and `OpponentPrompt`
- **Post-condition**: System blocks contain ONLY player prompt. User message contains opponent profile as informational context.
- **Invariant**: `GetOpponentResponseAsync` and `DeliverMessageAsync` are NOT changed.
- **Error handling**: No new error paths.

### Dependencies

- None — this is the foundation fix. #489 builds on this.

### NFR

- **Latency**: No change — same number of API calls, similar token count (moved from system to user)
- **Cost**: Slight increase — opponent prompt no longer cached in system. Acceptable at prototype.

---

## Issue #489 — Texting style reinforcement

### Components

- `CharacterProfile` (Core) — gains `TextingStyleFragment` property
- `SessionDocumentBuilder.BuildDialogueOptionsPrompt()` (LlmAdapters)
- `PromptTemplates.DialogueOptionsInstruction` (LlmAdapters)

### Interface changes

**`CharacterProfile`** — new optional constructor parameter:

```csharp
public sealed class CharacterProfile
{
    // ... existing properties ...

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
        string textingStyleFragment = "")  // NEW — backward-compatible
    {
        // ...
        TextingStyleFragment = textingStyleFragment ?? string.Empty;
    }
}
```

**`DialogueContext`** — gains `PlayerTextingStyle` field:

```csharp
// New optional parameter (backward-compatible default "")
public string PlayerTextingStyle { get; }

// Constructor gains: string playerTextingStyle = ""
```

**`GameSession.StartTurnAsync()`** — passes `_player.TextingStyleFragment` as `playerTextingStyle` when constructing `DialogueContext`.

**`SessionDocumentBuilder.BuildDialogueOptionsPrompt()`** — inject texting style block before task:

```
// After all game state sections, before "YOUR TASK":
if (!string.IsNullOrEmpty(context.PlayerTextingStyle))
{
    sb.AppendLine();
    sb.AppendLine("YOUR TEXTING STYLE — follow this exactly, no deviations:");
    sb.AppendLine(context.PlayerTextingStyle);
    sb.AppendLine();
}
```

**`PromptTemplates.DialogueOptionsInstruction`** — append voice check:

```
Before writing each option, verify: does this sound exactly like
the texting style above? If not, rewrite it.
```

### Behavioral contract

- **Pre-condition**: `CharacterProfile.TextingStyleFragment` is populated during character assembly or loader
- **Post-condition**: TEXTING STYLE block appears in user message immediately before YOUR TASK
- **Fallback**: If `TextingStyleFragment` is empty/null, no TEXTING STYLE block is injected (backward-compatible)

### Dependencies

- #487 (voice bleed fix) must be implemented first — texting style reinforcement amplifies the separation

### Session-runner impact

- `CharacterLoader` and `CharacterDefinitionLoader` must extract texting style fragment and pass it to `CharacterProfile` constructor
- `CharacterLoader` can parse the "TEXTING STYLE" section from the `.md` prompt file
- `CharacterDefinitionLoader` already has `FragmentCollection` which has `TextingStyleFragments` — join them with " | "

---

## Issue #490 — Opponent resistance design

### Components

- `PromptTemplates` (LlmAdapters) — new constants for resistance rule and per-level descriptors
- `SessionDocumentBuilder.BuildOpponentPrompt()` (LlmAdapters)

### Interface changes

**`PromptTemplates`** — new constants:

```csharp
/// <summary>Fundamental resistance rule injected into opponent prompt.</summary>
public const string OpponentResistanceRule = @"...";

/// <summary>Returns interest-based resistance descriptor.</summary>
// This is a static method, not a constant — interest is dynamic
public static string GetResistanceDescriptor(int interest) { ... }
```

**`SessionDocumentBuilder.BuildOpponentPrompt()`** — inject resistance block:

```
// After CURRENT INTEREST STATE, before YOUR TASK:
sb.AppendLine();
sb.AppendLine("RESISTANCE STANCE");
sb.AppendLine(PromptTemplates.OpponentResistanceRule);
sb.AppendLine($"Current interest: {context.InterestAfter}/25. Resistance level: {PromptTemplates.GetResistanceDescriptor(context.InterestAfter)}");
```

**`PromptTemplates.OpponentResponseInstruction`** — update to include fundamental resistance reference.

### Behavioral contract

- **Pre-condition**: `OpponentContext.InterestAfter` is populated (already is)
- **Post-condition**: Opponent prompt includes resistance context for all interest < 25. At interest 25, resistance dissolves.
- **No DTO changes** — all data already available in `OpponentContext`

### Dependencies

- None — independent of other issues

---

## Issue #491 — Success delivery instruction revision

### Components

- `PromptTemplates.SuccessDeliveryInstruction` (LlmAdapters)

### Interface changes

**`PromptTemplates.SuccessDeliveryInstruction`** — replace existing constant with revised text per issue spec. No structural changes.

### Behavioral contract

- **Pre-condition**: `DeliveryContext.BeatDcBy` is populated (already is)
- **Post-condition**: Success delivery instruction specifies margin-based tiers with "improve existing sentiment, don't add new ideas" principle
- **No code changes outside PromptTemplates** — `SessionDocumentBuilder.BuildDeliveryPrompt()` already uses this constant

### Dependencies

- None — standalone prompt text change

---

## Issue #492 — LlmPlayerAgent character-aware rework

### Components

- `LlmPlayerAgent` (session-runner)
- `PlayerAgentContext` (session-runner)
- `Program.cs` (session-runner)

### Interface changes

**`LlmPlayerAgent` constructor** — gains additional context:

```csharp
public LlmPlayerAgent(
    AnthropicOptions options,
    ScoringPlayerAgent fallback,
    string playerName = "the player",
    string opponentName = "the opponent",
    string playerSystemPrompt = "",       // NEW
    string playerTextingStyle = "")       // NEW
```

**`LlmPlayerAgent.BuildPrompt()`** — enhanced prompt including:
- Player character's personality/texting style summary
- Full conversation history (from `PlayerAgentContext`)
- Scoring agent's EV table as advisory input
- Character-fit and narrative-moment considerations

**`PlayerAgentContext`** — gains conversation history:

```csharp
/// <summary>Conversation history for LLM context.</summary>
public IReadOnlyList<(string Sender, string Text)>? ConversationHistory { get; }

// Constructor gains: optional parameter with null default
```

**`Program.cs`** — passes `CharacterProfile.AssembledSystemPrompt` and `CharacterProfile.TextingStyleFragment` to `LlmPlayerAgent` constructor. Passes conversation history to `PlayerAgentContext`.

### Behavioral contract

- **Pre-condition**: `IPlayerAgent` interface is unchanged — `DecideAsync(TurnStart, PlayerAgentContext)` signature preserved
- **Post-condition**: LlmPlayerAgent produces character-consistent picks with reasoning visible in playtest output
- **Fallback**: On any LLM failure, falls back to `ScoringPlayerAgent` (existing behavior preserved)
- **Invariant**: `ScoringPlayerAgent` always runs first to provide EV table (existing behavior)

### Dependencies

- #489 (texting style) for `CharacterProfile.TextingStyleFragment` — can still implement with empty string fallback
- #346 (IPlayerAgent interface) — already merged

---

## Issue #493 — Failure tier visible to opponent

### Components

- `OpponentContext` (Core) — gains `DeliveryTier` field
- `GameSession.ResolveTurnAsync()` (Core) — passes tier to `OpponentContext`
- `SessionDocumentBuilder.BuildOpponentPrompt()` (LlmAdapters)
- `PromptTemplates` (LlmAdapters) — per-tier opponent reaction guidance

### Interface changes

**`OpponentContext`** — new optional parameter:

```csharp
public sealed class OpponentContext
{
    // ... existing properties ...

    /// <summary>
    /// The failure tier of the player's roll. None means success.
    /// Used by the opponent prompt to calibrate reaction to corrupted messages.
    /// </summary>
    public FailureTier DeliveryTier { get; }

    // Constructor gains: FailureTier deliveryTier = FailureTier.None
}
```

**`GameSession.ResolveTurnAsync()`** — passes `rollResult.Tier` when constructing `OpponentContext`:

```csharp
var opponentContext = new OpponentContext(
    // ... existing params ...
    deliveryTier: rollResult.Tier);  // NEW
```

**`PromptTemplates`** — new constants:

```csharp
/// <summary>Per-tier opponent reaction guidance for BuildOpponentPrompt.</summary>
public const string OpponentFumbleGuidance = "...";
public const string OpponentMisfireGuidance = "...";
public const string OpponentTropeTrapGuidance = "...";
public const string OpponentCatastropheGuidance = "...";
public const string OpponentLegendaryGuidance = "...";

public static string GetOpponentFailureGuidance(FailureTier tier) { ... }
```

**`SessionDocumentBuilder.BuildOpponentPrompt()`** — inject failure context:

```csharp
// After PLAYER'S LAST MESSAGE, if tier != None:
if (context.DeliveryTier != FailureTier.None)
{
    sb.AppendLine();
    sb.AppendLine("DELIVERY NOTE");
    sb.AppendLine(PromptTemplates.GetOpponentFailureGuidance(context.DeliveryTier));
}
```

### Behavioral contract

- **Pre-condition**: `GameSession` has `rollResult.Tier` available when building `OpponentContext`
- **Post-condition**: On failure, opponent prompt includes tier-appropriate reaction guidance. On success (None), no failure context injected.
- **Backward compatibility**: `DeliveryTier` defaults to `FailureTier.None` — all existing callers unaffected
- **Import**: `OpponentContext` needs `using Pinder.Core.Rolls;` for `FailureTier` (Core → Core reference, allowed)

### Dependencies

- None — standalone

---

## Separation of Concerns Map

- PromptTemplates
  - Responsibility:
    - Static instruction text constants
    - Resistance descriptors
    - Failure tier guidance text
    - Voice check instruction
  - Interface:
    - DialogueOptionsInstruction
    - SuccessDeliveryInstruction
    - OpponentResponseInstruction
    - OpponentResistanceRule
    - GetResistanceDescriptor(int)
    - GetOpponentFailureGuidance(FailureTier)
  - Must NOT know:
    - HTTP transport
    - Game state
    - Session lifecycle

- SessionDocumentBuilder
  - Responsibility:
    - Assemble user-message content for LLM calls
    - Inject opponent profile as context (#487)
    - Inject texting style block (#489)
    - Inject resistance descriptor (#490)
    - Inject failure context (#493)
  - Interface:
    - BuildDialogueOptionsPrompt(DialogueContext)
    - BuildDeliveryPrompt(DeliveryContext)
    - BuildOpponentPrompt(OpponentContext)
    - BuildInterestChangeBeatPrompt(...)
  - Must NOT know:
    - Anthropic API details
    - Game session orchestration
    - Roll resolution

- CacheBlockBuilder
  - Responsibility:
    - Build ContentBlock arrays with cache_control
  - Interface:
    - BuildCachedSystemBlocks(player, opponent)
    - BuildPlayerOnlySystemBlocks(player)
    - BuildOpponentOnlySystemBlocks(opponent)
  - Must NOT know:
    - Prompt content
    - Game state
    - Which method uses which block type

- AnthropicLlmAdapter
  - Responsibility:
    - Orchestrate system blocks + user message per method
    - Parse LLM responses
  - Interface:
    - GetDialogueOptionsAsync(DialogueContext)
    - DeliverMessageAsync(DeliveryContext)
    - GetOpponentResponseAsync(OpponentContext)
    - GetInterestChangeBeatAsync(InterestChangeContext)
  - Must NOT know:
    - Game rules
    - Turn sequencing
    - Character assembly

- CharacterProfile
  - Responsibility:
    - Carry assembled character data for runtime
  - Interface:
    - Stats, AssembledSystemPrompt, DisplayName
    - Timing, Level, Bio
    - TextingStyleFragment (NEW #489)
  - Must NOT know:
    - How assembly was performed
    - LLM adapter details
    - Game session state

- OpponentContext
  - Responsibility:
    - Carry context for opponent response generation
  - Interface:
    - All existing fields
    - DeliveryTier (NEW #493)
  - Must NOT know:
    - How the LLM uses the context
    - Roll resolution details

- LlmPlayerAgent
  - Responsibility:
    - Character-aware option selection via LLM
    - Fallback to ScoringPlayerAgent
  - Interface:
    - DecideAsync(TurnStart, PlayerAgentContext)
  - Must NOT know:
    - GameSession internals
    - Prompt template constants
    - Anthropic caching strategy

---

## Implementation Strategy

### Recommended order

1. **#487** (voice bleed fix) — Foundation. Changes system block strategy for `GetDialogueOptionsAsync` and moves opponent to user message. All other prompt changes layer on top.

2. **#493** (failure tier to opponent) — Independent DTO + prompt change. Can start in parallel with #487 since it touches `BuildOpponentPrompt` not `BuildDialogueOptionsPrompt`.

3. **#491** (success delivery revision) — Standalone constant change in PromptTemplates. Zero code dependencies. Can start in parallel with #487.

4. **#490** (opponent resistance) — Independent opponent prompt change. Can start in parallel with #487 since it touches `BuildOpponentPrompt`.

5. **#489** (texting style) — Depends on #487 being done first (adds to the user message structure #487 establishes). Also requires `CharacterProfile.TextingStyleFragment` which touches Core.

6. **#492** (LlmPlayerAgent rework) — Depends on #489 for `TextingStyleFragment`. Can use empty string fallback if #489 isn't merged yet.

### Wave plan

**Wave 1** (parallel — no interdependencies):
- #487 (voice bleed fix)
- #491 (success delivery revision)
- #490 (opponent resistance)
- #493 (failure tier to opponent)

**Wave 2** (depends on #487):
- #489 (texting style reinforcement)

**Wave 3** (depends on #489):
- #492 (LlmPlayerAgent rework)

### Tradeoffs

- **Prompt engineering over code**: 4 of 6 issues are purely prompt text changes. This is correct for prototype maturity — tune the prompts first, add mechanical enforcement later.
- **Opponent prompt in user message (#487)**: Slight cost increase (no caching for opponent prompt in dialogue options call). Acceptable at prototype. If cost becomes an issue, could move to a second cached system block with explicit "DO NOT ADOPT THIS VOICE" framing.
- **TextingStyleFragment on CharacterProfile**: This adds a field to a Core DTO for a concern that's only relevant to LlmAdapters. Alternative: extract texting style in SessionDocumentBuilder by parsing the system prompt. Decision: put it on CharacterProfile because (a) it's cleaner, (b) session-runner's LlmPlayerAgent also needs it, (c) the data originates from FragmentCollection which Core already owns.

### Risk mitigation

- **LLM still bleeds voices after #487**: Unlikely but possible. Mitigation: #489 adds texting style reinforcement as a second defense layer.
- **Resistance text makes opponent too hostile**: Tune descriptors. All text is in PromptTemplates constants — easy to iterate.
- **CharacterProfile backward compat**: New constructor param has default value. Existing code compiles unchanged.

---

## Known Gaps (as of this sprint)

| Gap | Status |
|-----|--------|
| Shadow persistence across sessions | Not addressed |
| `AddExternalBonus()` deprecated but not removed | Cleanup needed |
| Energy system consumers | `IGameClock.ConsumeEnergy()` unused |
| GameSession god object (1454 lines) | Growing — extraction needed |
| CharacterProfile.TextingStyleFragment source | Populated by session-runner loaders; not by PromptBuilder |
| Prompt caching cost for dialogue options | Opponent prompt no longer cached — monitor |
