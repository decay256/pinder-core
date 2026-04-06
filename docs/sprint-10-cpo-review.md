# CPO Strategic Review — Sprint 10

## Alignment: ⚠️ ADVISORY
This sprint is focused on critical infrastructure and UI bugs that affect the product vision. However, there are significant architectural and scoping concerns. Issue #536 proposes stateful generation which will corrupt persona integrity, violating the core design of Anthropic's message context. Removing the format tag in #572 exposes the user to raw LLM meta-talk. Furthermore, #574 misidentifies the root cause of empty bios, and #575 needs a robust architectural fix rather than string concatenation. I have filed 4 vision concern issues to address these risks.

## Data Flow Traces
### Stateful Session Architecture (#536)
- User opens session → GameSession maintains a single Anthropic `messages[]` array → Options generated → Player message added → Opponent response added
- Required fields: System prompt, `user` messages, `assistant` messages
- ⚠️ Missing constraint: The `assistant` role is supposed to represent a single persona. Mixing JSON options, player text, and opponent text into one stateful `messages[]` array breaks the LLM's persona consistency.

### Game Definition Injection (#575)
- Host loads `game-definition.yaml` → `SessionSystemPromptBuilder.Build()` is supposed to be called → System prompt injected into LLM
- Required fields: Game vision, world rules, character profiles
- ⚠️ Missing: `GameSession` bypasses the prompt builder, so the game definition fields never flow into the LLM system prompt.

### Bio Loading (#574)
- JSON definition read → `CharacterDefinitionLoader` parses bio → `CharacterProfile` constructor called
- Required fields: Stats, Level, Bio, TextingStyle
- ⚠️ Missing: Bio is parsed but dropped before being passed to `CharacterProfile`, resulting in an empty bio at runtime.

## Unstated Requirements
- Removing the `[RESPONSE]` wrapper (proposed in #572) will eventually lead to the user seeing raw LLM conversational filler, which breaks the immersive dating app UI. The user expects pure in-character dialogue.
- When NarrativeBeat is removed (#573), the user still needs some indication of a vibe shift in the UI, even if it's not LLM-generated text.

## Domain Invariants
- **LLM Persona Integrity**: The Anthropic `assistant` role must exclusively represent the character voice established by the system prompt.
- **Clean Dialogue**: Game UI must display raw character dialogue, stripped of any LLM formatting or conversational preamble.

## Gaps
- **Assumption**: Issue #536 assumes that stateless context generation is flawed, but the stateless design already injects the full `_history`, and stateful generation will cause voice bleed.
- **Missing**: Issue #574 fixes a fallback loader but misses the primary assembler path, which is dropping the bio property.
- **Unnecessary**: Removing the formatting tag entirely in #572 is an overcorrection that introduces new risks.

## Recommendations
1. Rescope #536 to retain stateless generation, utilizing `_history` to provide context while preserving persona integrity (see #583).
2. For #572, do not remove the tag; instead implement a fallback parser that handles missing quotes (see #596).
3. Correct #574 to fix `CharacterDefinitionLoader` so it passes the bio to the profile (see #579).
4. For #575, wire `SessionSystemPromptBuilder.Build()` into `GameSession` properly instead of raw string concatenation (see #576).
