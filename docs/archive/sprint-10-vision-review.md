# CPO Strategic Review — Sprint 10

## Alignment: ⚠️ ADVISORY
This sprint is focused on resolving LLM response formatting and session runner quality. However, there is a fundamental architectural misunderstanding in #536. The sprint attempts to fix prompt quality issues by accumulating all turns into a single stateful Anthropic `messages[]` array. This violates a core domain invariant regarding LLM personas, and will severely degrade character voice distinctness. The remaining bug fixes are solid, but their implementations need to incorporate the insights from the vision concerns.

## Data Flow Traces
### Stateful LLM Architecture (#536)
- User action → GameSession calls GetDialogueOptionsAsync / DeliverMessageAsync / GetOpponentResponseAsync → Adapter appends User prompt → Anthropic returns response → Adapter appends Assistant response.
- Required fields: Alternating `user` and `assistant` messages strictly tied to one persona.
- ⚠️ **Missing Constraint**: The `assistant` role in the `messages[]` array is forced to hold three completely different personas (Meta-Engine generating JSON options, Player delivering a message, Opponent reacting). Anthropic models anchor heavily on their own past `assistant` messages. When asked to respond as Velvet, the LLM will see "itself" having just generated JSON options and Gerald's dialogue. This will cause catastrophic instruction confusion and voice bleed.

## Domain Invariants
- **LLM Persona Consistency**: The Anthropic API `messages` array must only contain `assistant` messages that reflect the persona established by the system prompt. Mixing meta-system (JSON options), player, and opponent tasks into a single stateful session breaks the LLM's understanding of its own role.

## Gaps
- **Assumption in #536**: The proposal assumes that stateless generation is the cause of "cold" or generic options. However, `SessionDocumentBuilder` already injects `IReadOnlyList<ConversationEntry> _history` into every stateless call. The stateless architecture provides the exact same context without the catastrophic persona bleed of stateful `messages[]` accumulation.
- **Root Cause of #575**: Issue #575 flags that `game-definition.yaml` is not loading. As noted in Vision Concern #576, the real issue is that `GameSession` bypasses `SessionSystemPromptBuilder.Build()` and uses raw string concatenation, dropping the game definition entirely.
- **Root Cause of #574**: Issue #574 flags empty bios. As noted in Vision Concern #579, `CharacterLoader` was already fixed in #513, but the JSON assembler path (`CharacterDefinitionLoader`) simply skips passing the bio to the `CharacterProfile` constructor.

## Recommendations
1. Rescope or close #536. Do not enforce the stateful LLM adapter architecture across multiple distinct generation tasks. (See Vision Concern #583).
2. Fix #575 by wiring `SessionSystemPromptBuilder.Build()` into `GameSession` as detailed in Vision Concern #576.
3. Fix #574 by passing the loaded `bio` to the `CharacterProfile` constructor in `CharacterDefinitionLoader` as detailed in Vision Concern #579.
4. Implement #573 (remove NarrativeBeat API call) using an ephemeral approach to keep meta-content out of the dialogue history, as detailed in Vision Concern #577.
