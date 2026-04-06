# Sprint 10 Interface Contracts

## 1. CharacterDefinitionLoader (Fixes #579 / #574)

**Component**: `session-runner/CharacterDefinitionLoader.cs`

**Responsibility**: Parse a character JSON definition, execute the character assembly pipeline, and return a valid `CharacterProfile`.

**Interface Change**:
The `CharacterProfile` constructor takes an optional `bio` parameter. Previously, the loader skipped this parameter, resulting in an empty bio.
The loader MUST pass the parsed `bio` variable to the `CharacterProfile` constructor.

```csharp
// Expected signature usage
return new CharacterProfile(
    fragments.Stats, systemPrompt, name, fragments.Timing, level,
    bio: bio, // MUST PASS BIO HERE
    textingStyleFragment: textingStyle);
```

**Testing Contract**:
A unit test MUST verify that when `CharacterDefinitionLoader.Parse` is called on a valid JSON definition containing a `bio`, the resulting `CharacterProfile.Bio` is exactly that string.

---

## 2. Fallback Parsing for Opponent Response (Fixes #596 / #572)

**Component**: `Pinder.LlmAdapters/Anthropic/AnthropicLlmAdapter.cs`

**Responsibility**: Parse the raw string returned by the Anthropic API into a clean dialogue string and optional signals.

**Interface Contract (Behavioral)**:
The method `ParseOpponentResponse` MUST strip the `[RESPONSE]` format tag if the LLM includes it, regardless of whether the subsequent text is wrapped in quotes.

**Examples**:
- Input: `[RESPONSE]\n"Hello"` → Output: `Hello`
- Input: `[RESPONSE]\nHello` → Output: `Hello`
- Input: `"Hello"` → Output: `Hello`

**Testing Contract (MANDATORY)**:
The code reviewer explicitly rejected the previous implementation because of missing test coverage. You MUST add a test in `tests/Pinder.LlmAdapters.Tests/Anthropic/AnthropicLlmAdapterTests.cs` that verifies:
```csharp
var input = "[RESPONSE]\nHello there";
var result = AnthropicLlmAdapter.ParseOpponentResponse(input);
Assert.Equal("Hello there", result.MessageText);
```

---

## 3. Architecture Vision: Stateless LLM Architecture (Fixes #583 / #536)

**Component**: `Pinder.LlmAdapters/Anthropic/AnthropicLlmAdapter.cs`

**Decision**: Do NOT implement a stateful session (`messages[]` accumulation) across different generation tasks (Options, Delivery, Opponent). Maintaining state violates the Anthropic persona invariant and causes severe voice bleed.

**Contract**:
- `GetDialogueOptionsAsync`, `DeliverMessageAsync`, and `GetOpponentResponseAsync` MUST remain completely independent, stateless HTTP calls.
- Context is provided entirely via `_history` injected into the stateless prompt.
- No `_session` variable or persistent message accumulation may be used.

---

## 4. NarrativeBeat Removal (Fixes #573)

**Component**: `Pinder.LlmAdapters/Anthropic/AnthropicLlmAdapter.cs`

**Contract**:
`GetInterestChangeBeatAsync` MUST return `null` without making an Anthropic API call to avoid history pollution and unnecessary LLM usage.
