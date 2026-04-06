# CPO Strategic Review — Sprint 10

## Alignment: ⚠️ ADVISORY
This sprint focuses on essential LLM prompt quality and session architecture improvements. While the goals are aligned with the product vision of distinct character voices and immersive dialogue, several proposed implementations contradict architectural invariants or risk introducing regressions. Specifically, the persistent stateful LLM adapter (#536) violates persona consistency, and removing output format tags (#572) risks conversational filler. These must be addressed before proceeding.

## Data Flow Traces
### Stateful LLM Architecture (#536)
- User action → GameSession calls GetDialogueOptionsAsync / DeliverMessageAsync / GetOpponentResponseAsync → Adapter appends User prompt → Anthropic returns response → Adapter appends Assistant response.
- Required fields: Alternating `user` and `assistant` messages strictly tied to one persona.
- ⚠️ **Missing Constraint**: The `assistant` role in the `messages[]` array is forced to hold three completely different personas (Meta-Engine generating JSON options, Player delivering a message, Opponent reacting). Anthropic models anchor heavily on their own past `assistant` messages. When asked to respond as Velvet, the LLM will see "itself" having just generated JSON options and Gerald's dialogue. This will cause catastrophic instruction confusion and voice bleed.

## Unstated Requirements
- When the LLM generates opponent dialogue, the parser must cleanly extract the in-character text. If we remove the `[RESPONSE]` wrapper requirement (as proposed in #572), the user will eventually see LLM conversational filler ("Here is what the character says: ..."), which breaks immersion.

## Domain Invariants
- **LLM Persona Consistency**: The Anthropic API `messages` array must only contain `assistant` messages that reflect the persona established by the system prompt. Mixing meta-system (JSON options), player, and opponent tasks into a single stateful session breaks the LLM's understanding of its own role.
- **Data Pipeline Accuracy**: Character properties loaded from JSON (e.g., bio) must flow completely into the runtime `CharacterProfile` without being dropped during assembly.

## Gaps
- **Flawed Solution in #536**: The proposal assumes that stateless generation is the cause of "cold" or generic options. However, `SessionDocumentBuilder` already injects `IReadOnlyList<ConversationEntry> _history` into every stateless call. The stateless architecture provides the exact same context without the catastrophic persona bleed of stateful `messages[]` accumulation.
- **Risk in #572**: Removing the format tag entirely removes our mechanism for extracting pure dialogue from potential LLM meta-chatter.
- **Wrong Target in #574**: The issue directs the fix at `CharacterLoader.ParseBio`, but the real root cause of empty bios in the playtest runner is `CharacterDefinitionLoader` failing to pass the parsed bio to the `CharacterProfile` constructor.
- **Missing Game Definition (#575)**: `GameSession` currently bypasses `SessionSystemPromptBuilder.Build()` and uses raw string concatenation, completely dropping the `game-definition.yaml` context.

## Recommendations
1. **Rescope #536**: Do not enforce the stateful LLM adapter architecture across multiple distinct generation tasks. Rely on the existing stateless approach that rebuilds context from `_history` to maintain persona isolation (see previously filed Vision Concern #583).
2. **Update #572**: Do not remove the `[RESPONSE]` tag requirement. Instead, implement graceful fallback parsing to strip the tag when quotes are missing (see new Vision Concern #596).
3. **Correct #574's Scope**: Fix `CharacterDefinitionLoader` to pass the loaded `bio` to the `CharacterProfile` constructor (see previously filed Vision Concern #579).
4. **Fix #575 Architecturally**: Wire `SessionSystemPromptBuilder.Build()` directly into `GameSession` so the game definition is properly injected (see previously filed Vision Concern #576).
