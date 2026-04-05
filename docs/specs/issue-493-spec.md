# Issue #493 — Mechanic: Failure Degradation Legible to Opponent

**Module**: `docs/modules/llm-adapters.md` (update existing), `docs/modules/conversation-game-session.md` (update existing)

---

## Overview

When a player's roll fails, the opponent currently has no awareness of what went wrong — the opponent's response is generated without knowledge of the failure tier. This feature passes the `FailureTier` from the roll result into `OpponentContext` and injects per-tier reaction guidance into the opponent's LLM prompt, so the opponent reacts proportionally to how badly the player's message was corrupted. A Fumble produces slight coolness; a Catastrophe or Legendary failure produces visible discomfort or confusion.

---

## Function Signatures

### Pinder.Core — `OpponentContext` (DTO extension)

```csharp
namespace Pinder.Core.Conversation
{
    public sealed class OpponentContext
    {
        // ... all existing properties unchanged ...

        /// <summary>
        /// The failure tier of the player's roll. None means success.
        /// Used by the opponent prompt to calibrate reaction to corrupted messages.
        /// </summary>
        public FailureTier DeliveryTier { get; }

        public OpponentContext(
            string playerPrompt,
            string opponentPrompt,
            IReadOnlyList<(string Sender, string Text)> conversationHistory,
            string opponentLastMessage,
            IReadOnlyList<string> activeTraps,
            int currentInterest,
            string playerDeliveredMessage,
            int interestBefore,
            int interestAfter,
            double responseDelayMinutes,
            Dictionary<ShadowStatType, int>? shadowThresholds = null,
            string[]? activeTrapInstructions = null,
            string playerName = "",
            string opponentName = "",
            int currentTurn = 0,
            FailureTier deliveryTier = FailureTier.None)  // NEW — backward-compatible default
        {
            // ... existing assignments ...
            DeliveryTier = deliveryTier;
        }
    }
}
```

**Required import**: `using Pinder.Core.Rolls;` (Core-to-Core reference — allowed).

### Pinder.Core — `GameSession.ResolveTurnAsync()` (wiring change)

The existing `OpponentContext` construction site (around line 651 of `GameSession.cs`) gains one new argument:

```csharp
var opponentContext = new OpponentContext(
    // ... all existing params unchanged ...
    shadowThresholds: opponentShadowThresholds,
    deliveryTier: rollResult.Tier);  // NEW — passes the roll's FailureTier
```

No new public methods. No signature changes to `ResolveTurnAsync`.

### Pinder.LlmAdapters — `PromptTemplates` (new constants + method)

```csharp
namespace Pinder.LlmAdapters
{
    public static class PromptTemplates
    {
        // ... existing constants unchanged ...

        /// <summary>Opponent reaction guidance for Fumble (miss by 1-2).</summary>
        public const string OpponentFumbleGuidance;     // string constant

        /// <summary>Opponent reaction guidance for Misfire (miss by 3-5).</summary>
        public const string OpponentMisfireGuidance;    // string constant

        /// <summary>Opponent reaction guidance for TropeTrap (miss by 6-9).</summary>
        public const string OpponentTropeTrapGuidance;  // string constant

        /// <summary>Opponent reaction guidance for Catastrophe (miss by 10+).</summary>
        public const string OpponentCatastropheGuidance; // string constant

        /// <summary>Opponent reaction guidance for Legendary (Nat 1).</summary>
        public const string OpponentLegendaryGuidance;  // string constant

        /// <summary>
        /// Returns the per-tier opponent reaction guidance text.
        /// Returns empty string for FailureTier.None (success).
        /// </summary>
        /// <param name="tier">The failure tier from the player's roll.</param>
        /// <returns>Guidance string for the LLM, or empty string on success.</returns>
        public static string GetOpponentFailureGuidance(FailureTier tier);
    }
}
```

**Return type**: `string` (never null — returns `""` for `FailureTier.None`).

**Required import**: `using Pinder.Core.Rolls;` (LlmAdapters already references Pinder.Core).

### Pinder.LlmAdapters — `SessionDocumentBuilder.BuildOpponentPrompt()` (prompt injection)

The existing `BuildOpponentPrompt(OpponentContext context)` method gains a new section in the assembled prompt. **No signature change** — the `DeliveryTier` is read from the existing `context` parameter.

```csharp
// Injected after "PLAYER'S LAST MESSAGE" section, before "INTEREST CHANGE":
if (context.DeliveryTier != FailureTier.None)
{
    sb.AppendLine();
    sb.AppendLine("DELIVERY NOTE");
    sb.AppendLine(PromptTemplates.GetOpponentFailureGuidance(context.DeliveryTier));
}
```

---

## Input/Output Examples

### Example 1: Fumble (miss by 1-2) — subtle coolness

**Input state**:
- Player intended message: `"haha yeah coffee sounds nice, maybe we could—"`
- Delivered (degraded) message: `"haha yeah coffee sounds nice, maybe we could—"` (minimal degradation)
- `rollResult.Tier` = `FailureTier.Fumble`
- `interestBefore` = 12, `interestAfter` = 11

**OpponentContext constructed with**:
- `deliveryTier: FailureTier.Fumble`

**Prompt section injected after PLAYER'S LAST MESSAGE**:
```
DELIVERY NOTE
The player's message landed slightly off — timing was a beat late, or phrasing
felt a little rehearsed. React with slight coolness: a shorter reply, a beat of
hesitation, or a subtle topic redirect. Do NOT acknowledge the awkwardness
explicitly — just let it show in your tone.
```

### Example 2: Catastrophe (miss by 10+) — visible discomfort

**Input state**:
- `rollResult.Tier` = `FailureTier.Catastrophe`
- `interestBefore` = 14, `interestAfter` = 10

**Prompt section injected**:
```
DELIVERY NOTE
The player's message went badly wrong — it came across as tone-deaf, uncomfortable,
or wildly inappropriate for the moment. React with visible discomfort or confusion.
You might pull back sharply, change the subject with obvious discomfort, or give a
short, clipped response that signals "that was NOT it."
```

### Example 3: Success (FailureTier.None) — no injection

**Input state**:
- `rollResult.Tier` = `FailureTier.None` (success)

**Result**: No "DELIVERY NOTE" section is injected into the opponent prompt. The prompt structure is identical to the current behavior.

### Example 4: Legendary (Nat 1) — maximum embarrassment

**Input state**:
- `rollResult.Tier` = `FailureTier.Legendary`
- `interestBefore` = 15, `interestAfter` = 10

**Prompt section injected**:
```
DELIVERY NOTE
The player's message was a complete disaster — spectacularly wrong in a way that's
almost impressive. Something deeply embarrassing was said or implied. React with a
mix of shock and secondhand embarrassment. You might screenshot this for your group
chat. A "..." or "wow" or stunned silence is appropriate.
```

---

## Acceptance Criteria

### AC1: OpponentContext includes DeliveryTier

**Given** a roll fails, **when** `OpponentContext` is constructed in `GameSession.ResolveTurnAsync()`, **then** it includes a `DeliveryTier` property set to the `FailureTier` enum value from the roll result. The property defaults to `FailureTier.None` for backward compatibility.

**Verification**: Construct `OpponentContext` with `deliveryTier: FailureTier.TropeTrap`. Assert `context.DeliveryTier == FailureTier.TropeTrap`. Construct without the parameter. Assert `context.DeliveryTier == FailureTier.None`.

### AC2: GameSession passes roll tier to OpponentContext

**Given** `GameSession` resolves a turn with a failed roll, **when** opponent context is built, **then** `rollResult.Tier` is passed as the `deliveryTier` argument.

**Verification**: Mock `ILlmAdapter.GetOpponentResponseAsync()` to capture the `OpponentContext` argument. Inject a dice roller that produces a known failure. Assert the captured context's `DeliveryTier` matches the expected `FailureTier`.

### AC3: BuildOpponentPrompt injects failure context for non-None tiers

**Given** `BuildOpponentPrompt` receives an `OpponentContext` with `DeliveryTier != FailureTier.None`, **when** the prompt is assembled, **then** a "DELIVERY NOTE" section is injected containing per-tier guidance text from `PromptTemplates.GetOpponentFailureGuidance()`.

**Verification**: Call `SessionDocumentBuilder.BuildOpponentPrompt()` with a context having `DeliveryTier = FailureTier.Misfire`. Assert the returned string contains `"DELIVERY NOTE"`. Assert it contains the Misfire-specific guidance text.

### AC4: Fumble produces slight coolness guidance

**Given** a Fumble tier (miss by 1-2), **when** `GetOpponentFailureGuidance(FailureTier.Fumble)` is called, **then** the returned text instructs the opponent to show slight coolness without explicit acknowledgment of failure.

**Verification**: Assert `PromptTemplates.GetOpponentFailureGuidance(FailureTier.Fumble)` returns a non-empty string that does NOT contain words like "failed" or "messed up" (the opponent shouldn't break the fourth wall).

### AC5: TropeTrap/Catastrophe produces visible discomfort guidance

**Given** a TropeTrap or Catastrophe tier, **when** `GetOpponentFailureGuidance()` is called, **then** the returned text instructs the opponent to show visible discomfort or confusion.

**Verification**: Assert the guidance strings for `FailureTier.TropeTrap` and `FailureTier.Catastrophe` are both non-empty and distinct from each other (different tiers produce different guidance).

### AC6: Success (None) produces no failure context

**Given** a success (tier = `FailureTier.None`), **when** `BuildOpponentPrompt` is called, **then** no "DELIVERY NOTE" section appears in the assembled prompt.

**Verification**: Call `BuildOpponentPrompt()` with `DeliveryTier = FailureTier.None`. Assert the result does NOT contain `"DELIVERY NOTE"`.

### AC7: Per-tier guidance text exists in PromptTemplates

All five failure tiers have corresponding guidance constants and the `GetOpponentFailureGuidance()` method maps each tier correctly.

**Verification**: For each value in `{ Fumble, Misfire, TropeTrap, Catastrophe, Legendary }`, call `GetOpponentFailureGuidance()` and assert the return is non-empty and distinct.

### AC8: Build clean, all tests pass

All existing tests (2295+) must continue to pass. The solution must compile without warnings on netstandard2.0 (Pinder.Core) and netstandard2.0 (Pinder.LlmAdapters).

---

## Edge Cases

### Default value backward compatibility
All existing code that constructs `OpponentContext` without a `deliveryTier` argument must compile and behave identically to current behavior. The default `FailureTier.None` means no "DELIVERY NOTE" is injected.

### Success roll passes FailureTier.None
When a roll succeeds, `rollResult.Tier` is `FailureTier.None`. GameSession passes this value. `BuildOpponentPrompt` sees `None` and skips the DELIVERY NOTE section entirely.

### Legendary failure (Nat 1)
Legendary is the most severe tier. The guidance should be the most extreme reaction. It must NOT be confused with Catastrophe — Legendary is a Nat 1 regardless of DC, while Catastrophe is miss by 10+.

### All FailureTier enum values covered
`GetOpponentFailureGuidance()` must handle every value in the `FailureTier` enum:
- `None` → returns `""` (empty string)
- `Fumble` → Fumble guidance
- `Misfire` → Misfire guidance
- `TropeTrap` → TropeTrap guidance
- `Catastrophe` → Catastrophe guidance
- `Legendary` → Legendary guidance

If a new enum value is added in the future, the method should return `""` (safe default) rather than throw.

### Read/Recover/Wait actions
`ReadAsync()`, `RecoverAsync()`, and `Wait()` do NOT call `GetOpponentResponseAsync` — they are self-contained actions. This feature only applies to the Speak path (`StartTurnAsync` → `ResolveTurnAsync`). No changes needed for non-Speak actions.

### NullLlmAdapter
`NullLlmAdapter` ignores the `OpponentContext` contents and returns a hardcoded response. The `DeliveryTier` field has no effect when using `NullLlmAdapter`. This is correct and expected.

---

## Error Conditions

### Invalid FailureTier value
If an unrecognized `FailureTier` integer is cast to the enum and passed to `GetOpponentFailureGuidance()`, the method should return `""` (empty string). Do not throw — degrade gracefully.

### Null OpponentContext
`BuildOpponentPrompt` already throws `ArgumentNullException` if `context` is null. No change needed.

### Missing FailureTier import in OpponentContext
`OpponentContext.cs` must add `using Pinder.Core.Rolls;` for the `FailureTier` type. This is a Core-to-Core namespace reference (both in `Pinder.Core` assembly), which is permitted.

---

## Dependencies

### Internal (Pinder.Core)
- `FailureTier` enum (`Pinder.Core.Rolls`) — already exists, no changes needed
- `RollResult.Tier` property (`Pinder.Core.Rolls`) — already exists, already populated by `RollEngine.Resolve()`
- `GameSession.ResolveTurnAsync()` (`Pinder.Core.Conversation`) — existing method, wiring change only
- `OpponentContext` (`Pinder.Core.Conversation`) — DTO extension (new optional parameter)

### Internal (Pinder.LlmAdapters)
- `PromptTemplates` (`Pinder.LlmAdapters`) — new constants + method
- `SessionDocumentBuilder.BuildOpponentPrompt()` (`Pinder.LlmAdapters`) — prompt injection

### External
- None. No new NuGet packages. No new projects. No external service changes.

### Issue Dependencies
- None. This issue is standalone — it does not depend on #487, #489, #490, #491, or #492.
