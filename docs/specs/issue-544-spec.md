# Spec: Add [ENGINE] Injection Blocks to LLM Calls

**Issue**: #544
**Module**: docs/modules/llm-adapters.md

---

## Overview

When the LLM adapter operates in stateful conversation mode (via `IStatefulLlmAdapter`), all game mechanics context — dice rolls, interest changes, trap activations, horniness levels, callbacks — must be injected into the growing conversation as structured `[ENGINE]` blocks instead of rebuilding full prompt context from scratch each call (which is the stateless `SessionDocumentBuilder` path). This keeps the accumulated conversation thread clean while providing the LLM with per-turn mechanical updates. In stateless mode (e.g., `NullLlmAdapter`), behavior is completely unchanged.

---

## Function Signatures

All functions live in a new static class within the `Pinder.LlmAdapters` namespace.

```csharp
namespace Pinder.LlmAdapters
{
    /// <summary>
    /// Builds [ENGINE] injection blocks for stateful conversation mode.
    /// Each block wraps game state deltas as structured text for the LLM.
    /// Pure static utility — no I/O, no state, no async.
    /// </summary>
    public static class EngineInjectionBuilder
    {
        /// <summary>
        /// Builds the [ENGINE] block for dialogue option generation.
        /// Injected as a user message into ConversationSession before the
        /// LLM generates the player's 4 options.
        /// </summary>
        /// <param name="context">The DialogueContext for this turn.</param>
        /// <returns>A formatted [ENGINE] block string.</returns>
        public static string BuildOptionsInjection(DialogueContext context);

        /// <summary>
        /// Builds the [ENGINE] block for message delivery (success or failure).
        /// Injected after the player picks an option so the LLM can apply
        /// roll-based degradation or enhancement.
        /// </summary>
        /// <param name="context">The DeliveryContext for this turn.</param>
        /// <param name="rollFlavorText">
        /// Optional flavor text sourced from enriched YAML 'flavor' fields
        /// for the specific failure/success tier. Null means generic.
        /// </param>
        /// <returns>A formatted [ENGINE] block string.</returns>
        public static string BuildDeliveryInjection(
            DeliveryContext context,
            string? rollFlavorText = null);

        /// <summary>
        /// Builds the [ENGINE] block for opponent response generation.
        /// Injected after message delivery so the opponent LLM persona
        /// knows how the conversation shifted.
        /// </summary>
        /// <param name="context">The OpponentContext for this turn.</param>
        /// <param name="interestNarrative">
        /// Optional narrative descriptor for the current interest band.
        /// If null, the builder generates a default from the interest value.
        /// </param>
        /// <returns>A formatted [ENGINE] block string.</returns>
        public static string BuildOpponentInjection(
            OpponentContext context,
            string? interestNarrative = null);

        /// <summary>
        /// Builds the [ENGINE] block for interest threshold crossing beats.
        /// Injected when interest crosses a state boundary (e.g., Lukewarm → Interested).
        /// </summary>
        /// <param name="context">The InterestChangeContext for this event.</param>
        /// <returns>A formatted [ENGINE] block string.</returns>
        public static string BuildInterestBeatInjection(
            InterestChangeContext context);
    }
}
```

### AnthropicLlmAdapter Changes

`AnthropicLlmAdapter` gains a second code path in each of its four `ILlmAdapter` methods. When a `ConversationSession` is active (i.e., `HasActiveConversation` is true — set up by `IStatefulLlmAdapter.StartConversation`), the adapter:

1. Calls the corresponding `EngineInjectionBuilder.BuildXxxInjection()` method to produce the user message content.
2. Appends it as a user message to the `ConversationSession`.
3. Sends the full accumulated `messages[]` via `AnthropicClient`.
4. Appends the assistant response to the `ConversationSession`.
5. Parses and returns the result as normal.

When no session is active (stateless mode), the existing `SessionDocumentBuilder` + `CacheBlockBuilder` path is used unchanged.

No new public methods are added to `AnthropicLlmAdapter`. The routing is internal.

---

## Input/Output Examples

### Example 1: Options Injection (Turn 3)

**Input** — `DialogueContext` with:
- `CurrentTurn`: 3
- `CurrentInterest`: 14
- `ActiveTraps`: `[]`
- `HorninessLevel`: 6
- `RequiresRizzOption`: true
- `CallbackOpportunities`: `[{ TopicKey: "music taste", TurnIntroduced: 1 }]`
- `ShadowThresholds`: `{ Horniness: 8 }`
- `PlayerTextingStyle`: `"lowercase, dry humor, never uses exclamation marks"`

**Output** — string:
```
[ENGINE — Turn 3: Option Generation]
Interest: 14/25 — Interested 😊
Active traps: none
Horniness: 6 (1 Rizz option required)
Callbacks available: "music taste" (introduced T1, 2 turns ago, +1 hidden)
Shadow state: Horniness elevated — reading subtext that may not be there

TEXTING STYLE — follow this exactly:
lowercase, dry humor, never uses exclamation marks

Generate 4 dialogue options for this turn.
```

### Example 2: Delivery Injection (Success)

**Input** — `DeliveryContext` with:
- `CurrentTurn`: 3
- `ChosenOption.IntendedText`: `"you're judging my playlist aren't you"`
- `ChosenOption.Stat`: `StatType.Wit`
- `Outcome`: `FailureTier.None` (success)
- `BeatDcBy`: 7
- `ShadowThresholds`: `null`

**Input** — `rollFlavorText`: `"Sharp deflection — the kind that makes them laugh before they realize they've been read"`

**Output** — string:
```
[ENGINE — Turn 3: Delivery]
Roll: SUCCESS — beat DC by 7 (Strong hit)
Stat: WIT
Chosen message: "you're judging my playlist aren't you"
Flavor: Sharp deflection — the kind that makes them laugh before they realize they've been read

Deliver this message with sharpened phrasing. Do not add new ideas — improve what's there.
```

### Example 3: Delivery Injection (Failure)

**Input** — `DeliveryContext` with:
- `CurrentTurn`: 5
- `ChosenOption.IntendedText`: `"i actually really like you"`
- `ChosenOption.Stat`: `StatType.Honesty`
- `Outcome`: `FailureTier.TropeTrap`
- `BeatDcBy`: -7
- `ShadowThresholds`: `{ Denial: 14 }`

**Input** — `rollFlavorText`: `null`

**Output** — string:
```
[ENGINE — Turn 5: Delivery]
Roll: FAILED — missed DC by 7
Failure tier: TROPE_TRAP
Stat: HONESTY
Chosen message: "i actually really like you"
Shadow state: Denial elevated — honest options sound rehearsed, truths are technically true but curated

Degrade the message according to the TROPE_TRAP tier: the message transforms into a recognisable bad-texting archetype. The trap is now active.
```

### Example 4: Opponent Injection

**Input** — `OpponentContext` with:
- `CurrentTurn`: 3
- `PlayerDeliveredMessage`: `"you're judging my playlist aren't you"`
- `InterestBefore`: 14
- `InterestAfter`: 16
- `DeliveryTier`: `FailureTier.None`
- `ResponseDelayMinutes`: 2.5

**Input** — `interestNarrative`: `null` (builder generates default)

**Output** — string:
```
[ENGINE — Turn 3: Opponent Response]
Interest: 14 → 16 (+2) — crossed into Very Into It 😍
Player's message: "you're judging my playlist aren't you"
Delivery: success
Response timing: ~2.5 minutes

You are warming to this person. Genuine interest is building, but you are not won over yet. Respond in character.
```

### Example 5: Opponent Injection (after failure)

**Input** — `OpponentContext` with:
- `CurrentTurn`: 5
- `PlayerDeliveredMessage`: `"i actually really like you... i mean, like, your vibe or whatever"`
- `InterestBefore`: 12
- `InterestAfter`: 9
- `DeliveryTier`: `FailureTier.TropeTrap`
- `ResponseDelayMinutes`: 8.0

**Output** — string:
```
[ENGINE — Turn 5: Opponent Response]
Interest: 12 → 9 (-3) — dropped into Lukewarm 🤔
Player's message: "i actually really like you... i mean, like, your vibe or whatever"
Delivery: TROPE_TRAP failure
Response timing: ~8.0 minutes

The player's message was visibly awkward. React with discomfort or confusion appropriate to the severity. You are not impressed. Respond in character.
```

### Example 6: Interest Beat Injection

**Input** — `InterestChangeContext` with:
- `OpponentName`: `"Sable"`
- `InterestBefore`: 14
- `InterestAfter`: 16
- `NewState`: `InterestState.VeryIntoIt`

**Output** — string:
```
[ENGINE — Interest Threshold Crossed]
Interest: 14 → 16
Sable has crossed into Very Into It.
Write a brief internal-monologue beat (1-2 sentences) showing Sable's shift in attitude. Stay in character.
```

---

## Acceptance Criteria

### AC1: Options injection format replaces current BuildDialogueOptionsPrompt user content

In stateful mode, when `AnthropicLlmAdapter.GetDialogueOptionsAsync()` is called with an active `ConversationSession`, the adapter must call `EngineInjectionBuilder.BuildOptionsInjection(context)` to produce the user message content, rather than `SessionDocumentBuilder.BuildDialogueOptionsPrompt(context)`.

The options injection block must include:
- Turn number
- Interest value and state label (with emoji)
- Active trap names (or "none")
- Horniness level and Rizz requirement (if `HorninessLevel >= 6`)
- Callback opportunities with turn distance and bonus tier
- Shadow taint text (if any shadow thresholds exceed their taint trigger values)
- Player texting style (if non-empty)
- A task instruction line ("Generate 4 dialogue options...")

In stateless mode, the existing `SessionDocumentBuilder.BuildDialogueOptionsPrompt()` path must be used unchanged.

### AC2: Delivery injection format includes roll context from rule YAML

`BuildDeliveryInjection()` must format the roll outcome (success/failure, margin, stat, tier name) and include the optional `rollFlavorText` parameter when non-null. This flavor text is sourced by the caller from enriched YAML `flavor` fields.

The delivery injection block must include:
- Turn number
- Roll outcome: "SUCCESS — beat DC by N" or "FAILED — missed DC by N"
- Failure tier name (for failures): FUMBLE, MISFIRE, TROPE_TRAP, CATASTROPHE, or LEGENDARY
- Stat used (uppercase)
- The chosen option's intended text
- The `rollFlavorText` (if provided)
- Shadow taint text (if any thresholds exceed trigger values)
- A delivery instruction appropriate to the outcome tier

### AC3: Opponent injection format includes Interest narrative per band

`BuildOpponentInjection()` must describe the interest movement (before → after, delta, state label) and include a narrative descriptor for the current interest band that guides the LLM's tone.

The opponent injection block must include:
- Turn number
- Interest change: "before → after (±delta) — state label"
- Player's delivered message text
- Delivery result: "success" or failure tier name
- Response timing
- Interest band narrative (see AC4 for the 6 bands)
- Failure context guidance (if `DeliveryTier != FailureTier.None`)

### AC4: Interest narratives configurable (6 bands defined)

The builder must define 6 interest narrative bands used in opponent injection. These map interest values to descriptive text guiding the LLM's response tone:

| Interest Range | Band Label | Narrative Guidance |
|---|---|---|
| 0 | Unmatched | Ghosting territory — the conversation is over |
| 1–4 | Bored | Actively disengaged — minimal effort, closing signals |
| 5–9 | Lukewarm | Not impressed — short replies, testing, not sold |
| 10–15 | Interested | Open but guarded — responsive, seeing where it goes |
| 16–24 | Very Into It / Almost There | Genuine interest — warmer, more playful, but not won over |
| 25 | Date Secured | Resistance dissolved — genuinely won over |

When the `interestNarrative` parameter is null, the builder generates the default narrative from the interest value using these bands. When non-null, the provided narrative is used verbatim.

### AC5: Roll context narratives sourced from enriched YAML `flavor` fields

The `rollFlavorText` parameter on `BuildDeliveryInjection()` carries flavor text that the **caller** (i.e., `AnthropicLlmAdapter`) sources from enriched YAML rule entries' `flavor` fields. The `EngineInjectionBuilder` itself does not load or parse YAML — it receives the text as a parameter.

When `rollFlavorText` is null, the delivery injection includes only the mechanical roll result (no flavor line). When non-null, a `Flavor:` line is included in the block.

### AC6: Unit tests verify injection format correctness

Each of the four builder methods must have unit tests verifying:
- The `[ENGINE — Turn N: ...]` header format is correct
- Required fields are present in the output
- Optional fields are omitted when their source data is null/empty/default
- Interest band narratives map to the correct ranges
- Shadow taint text appears when thresholds exceed trigger values
- Failure tier names match the `FailureTier` enum values
- The texting style block appears in options injection when non-empty
- Callback opportunities format correctly with turn distance and bonus tier

### AC7: Build clean

The solution must compile with zero errors and zero warnings. All existing tests (2979+) must continue to pass. No changes to `ILlmAdapter`, `NullLlmAdapter`, or any Pinder.Core game logic.

---

## Edge Cases

### Null and Empty Inputs

- **`context` is null**: All four builder methods must throw `ArgumentNullException`.
- **`context.ActiveTraps` is empty list**: Options injection must output `Active traps: none`.
- **`context.ShadowThresholds` is null**: No shadow taint block is emitted.
- **`context.ShadowThresholds` is empty dictionary**: No shadow taint block is emitted.
- **`context.CallbackOpportunities` is null or empty**: No callbacks section in options injection.
- **`context.PlayerTextingStyle` is empty string**: No texting style block in options injection.
- **`rollFlavorText` is null**: No flavor line in delivery injection.
- **`rollFlavorText` is empty string**: Treated the same as null — no flavor line.
- **`interestNarrative` is null**: Builder generates default from interest value.
- **`interestNarrative` is empty string**: Treated the same as null — use default.
- **`context.PlayerName` / `context.OpponentName` is empty**: Use fallback names "Player" / "Opponent".

### Boundary Values

- **Interest = 0 (Unmatched)**: Opponent injection uses "Unmatched 💀" label and ghosting narrative.
- **Interest = 25 (DateSecured)**: Opponent injection uses "Date Secured 🎉" label and resistance-dissolved narrative.
- **Interest = 5 (boundary Bored→Lukewarm)**: Maps to Lukewarm band (5–9).
- **Interest = 16 (boundary Interested→VeryIntoIt)**: Maps to Very Into It band (16–24).
- **`BeatDcBy` = 0**: On success, "beat DC by 0" (edge case: exactly meeting DC is a success). On failure, "missed DC by 0" should not occur (if it does, treat as fumble-tier).
- **Horniness < 6**: No horniness line in options injection.
- **Horniness = 6**: Include line with "1 Rizz option required" (if `RequiresRizzOption` is true).
- **`CurrentTurn` = 0 (default/backward-compat)**: Use 0 in the header; builder does not validate turn number.

### Shadow Taint Thresholds

Shadow taint text is included per shadow stat when the raw value exceeds the taint trigger threshold. The trigger thresholds are:
- Madness > 5
- Horniness > 6
- Denial > 5
- Fixation > 5
- Dread > 5
- Overthinking > 5

Multiple shadow taints can be active simultaneously. Each produces its own line in the shadow state block.

### Stateful vs Stateless Mode

- **Stateless adapter** (e.g., `NullLlmAdapter` which does not implement `IStatefulLlmAdapter`): `EngineInjectionBuilder` is never called. All existing `SessionDocumentBuilder` paths remain unchanged. Zero regression risk.
- **Stateful adapter with no session started** (should not happen per contract, but defensive): Adapter falls back to stateless path.

---

## Error Conditions

| Condition | Expected Behavior |
|---|---|
| Any builder method receives `null` context | Throws `ArgumentNullException` with the parameter name |
| `context.ChosenOption` is null in `BuildDeliveryInjection` | Throws `ArgumentNullException` (propagated from context construction, but builder should validate defensively) |
| `context.Outcome` is an unknown `FailureTier` enum value | Use "UNKNOWN" as tier name; use generic degradation instruction |
| `context.ChosenOption.Stat` is an unrecognized `StatType` | Use `stat.ToString().ToUpperInvariant()` — no special handling needed |
| `interestNarrative` is whitespace-only | Treated as null — generate default narrative |
| `rollFlavorText` is whitespace-only | Treated as null — no flavor line |
| Interest value is negative | Clamp narrative to Unmatched band (defensive) |
| Interest value exceeds 25 | Clamp narrative to Date Secured band (defensive) |

---

## Dependencies

### Internal (Pinder.Core — consumed, not modified)

- `Pinder.Core.Conversation.DialogueContext` — input to `BuildOptionsInjection`
- `Pinder.Core.Conversation.DeliveryContext` — input to `BuildDeliveryInjection`
- `Pinder.Core.Conversation.OpponentContext` — input to `BuildOpponentInjection`
- `Pinder.Core.Conversation.InterestChangeContext` — input to `BuildInterestBeatInjection`
- `Pinder.Core.Conversation.InterestState` — enum for interest band labels
- `Pinder.Core.Conversation.CallbackOpportunity` — callback data in options injection
- `Pinder.Core.Rolls.FailureTier` — enum for delivery/opponent tier labels
- `Pinder.Core.Stats.ShadowStatType` — enum keys for shadow taint lookup

### Internal (Pinder.LlmAdapters — extended)

- `AnthropicLlmAdapter` — gains stateful code path routing to `EngineInjectionBuilder`
- `ConversationSession` — message accumulator (from #541, must exist)
- `IStatefulLlmAdapter` — interface (from #542, must exist)
- `SessionDocumentBuilder` — unchanged; remains the stateless path

### Sprint Dependencies (must be merged before implementation)

- **#541** — `ConversationSession` class (message accumulator)
- **#542** — `IStatefulLlmAdapter` interface + `GameSession` wiring
- **#543** — `SessionSystemPromptBuilder` + `GameDefinition` (session system prompt)

### External

- Enriched YAML files (`rules/extracted/*-enriched.yaml`) — source of `flavor` fields for `rollFlavorText`. The caller (`AnthropicLlmAdapter` or session-runner) loads these; `EngineInjectionBuilder` itself has no YAML dependency.

### Build

- **Target framework**: .NET Standard 2.0
- **Language version**: C# 8.0 (no `record` types)
- **New file**: `src/Pinder.LlmAdapters/EngineInjectionBuilder.cs`
- **Modified file**: `src/Pinder.LlmAdapters/Anthropic/AnthropicLlmAdapter.cs` (add stateful routing)
- **New test file**: `tests/Pinder.LlmAdapters.Tests/EngineInjectionBuilderTests.cs`
