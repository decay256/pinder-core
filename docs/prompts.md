# Prompts

This document is the authoritative map of every LLM prompt template that pinder-core / pinder-web emit.

As of Phase 5 (shipped), all C# constants have been removed. The YAML catalog is the Single Source of Truth (SSOT) for all prompts.

## Catalog API

Pinder-core exposes a `PromptCatalog` type in `Pinder.LlmAdapters`:

```csharp
var catalog = PromptCatalog.LoadFromDirectory("data/prompts");
PromptEntry? entry = catalog.TryGet("stake");
string rendered = PromptCatalog.Substitute(
    entry!.UserTemplate!,
    new Dictionary<string, string> {
        { "character_profile", assembledSystemPrompt },
    });
```

### Loader contract

- Scans every `*.yaml` file in the directory.
- Each file declares `schema_version: 1` at the top. Any other version
  fails fast at load time.
- Each file declares an optional `prompts:` mapping; keys are prompt
  names, values are objects with `system_prompt` and / or
  `user_template` string fields.
- Duplicate prompt keys across files raise `InvalidDataException`.
- Files with no `prompts:` block are tolerated (reserved surface for
  future phases).

### Substitution contract

- `{token}`-style. Token names match `/[a-zA-Z_][a-zA-Z0-9_]*/`.
- Stray braces in prose / JSON blobs in the template body pass
  through verbatim — only well-formed `{name}` sequences are
  recognised.
- An unrecognised token (well-formed but absent from the values dict)
  raises `KeyNotFoundException` (fail-fast wiring), meaning a prompt referencing
  `{undefined_token}` fails fast at the call-site rather than
  shipping an unrendered token to the LLM. 

### Substitution flavour

Per the parent's pre-locked decision: `{token}` (NOT Scriban). This
matches the existing yaml round-trip pattern used by ruamel in
`pinder-backend` for the admin editor; round-tripping yaml comments +
ordering through ruamel is unaffected by the substitution layer
(substitution happens at C#-side load, not at admin-edit time).

### Hot-reload

Deferred to V2. Process restart is acceptable for V1. The catalog is
loaded once at startup and frozen; admin edits become visible on the
next `pinder-game-api` (or session-runner) start.

## File layout (Current SSOT)

```
data/prompts/
  archetypes.yaml
  backstory.yaml
  backstory_consolidation.yaml
  bio.yaml
  character-generation.yaml
  diagnosis.yaml
  dramatic_arc.yaml
  emotional-reactions.yaml
  narrative.yaml
  outfit.yaml
  overlay-model-comparison.yaml
  personality_consolidation.yaml
  sim_agent.yaml
  stake.yaml
  structural.yaml
  templates.yaml
```

## Static System Prompt vs. Dynamic Prompt Flow

Pinder prompt architecture decouples static identity framing from dynamic turn-by-turn writing direction:

### 1. Streamlined Static System Prompt
The static character system prompt (`AssembledSystemPrompt` / `SessionSystemPromptBuilder`) provides lean identity and baseline dating-app rules:
- **Included**: Character bio, active archetype definition, texting style tendencies, and high-level comedy dating RPG framing.
- **Pruned / Excluded**:
  - *No monolithic engine rulebook*: mechanical dice formula and internal C# system docs were pruned from `game-definition.yaml` / `GameMasterPrompt`.
  - *No raw psychiatric diagnosis dump*: the 11-bullet clinical diagnosis was removed from the static system prompt to prevent clinical psychoanalysis bleed in character dialogue.
  - *No static 20-category Lie/Reality table*: the full 40-entry backstory table was removed from `PromptBuilder.cs` to prevent token bloat and repetitive backstory recitation.

### 2. Dynamic Turn-by-Turn Emotional Direction & Backstory
Instead of dumping full character psychology into static prompts, dynamic direction is generated on a per-turn basis:
- **Psychiatrist / Emotional Director**: Evaluates the character's internal neurosis (from `psychiatric_diagnosis`), the current emotional turn event, and the active phase goal to produce targeted writing direction (`ego_game`, `improvised_flex_or_slip`, `texting_tactics`).
- **Dynamic Backstory Injection**: `EmotionStemSelector` selects a single authoritative reality fact (`tragic_reality`) at runtime based on the turn's dramatic phase and manner, providing fresh conversational fuel.
- **4 Dramatic Arc Phases**:
  1. **Phase 1 (Setup)**: Concrete actionable dating instructions — testing if the match can hold a conversation, establishing high-status positioning vs. playful intrigue, and probing vibe/humor with punchy 1-a.m. dating texts without giving away personal history.
  2. **Phase 2 (Escalation)**: Escalating tension and status — taking mundane real character facts or quirks and improvising flattering, high-status, or intriguing lies/flexes on the fly to tease or impress the match.
  3. **Phase 3 (Turning Point)**: Creating genuine intimate tension through a cracked facade — admitting underlying insecurity or reality as a vulnerable slip or tired honesty, immediately followed by a flirtatious pivot back to the match.
  4. **Phase 4 (Resolution)**: Closing the hookup or meeting logistics on their terms — 100% focused on sealing the date/hookup.
- **Tone Modulation**: Character behavior across all phases is modulated by individual character neurosis and emotional posture rather than hardcoded universal cynicism.

## Emotional Reaction Direction

`data/prompts/emotional-reactions.yaml` contains the internal DATEE emotional
reaction direction library for the post-delivery response pass. The catalog is
keyed by typed engine concepts rather than score math:

- `emotional-reaction-interest-*` describes the seven canonical
  `InterestState` meanings in prose.
- `emotional-reaction-transition-*` describes strengthened, preserved, damaged,
  and transformed relationship transitions. These entries require
  `{prior_relationship}` and `{resulting_relationship}` placeholders.
- `emotional-reaction-event-<stat>-<outcome>` describes how the delivered
  player message emotionally lands for the recipient for every
  `StatType` x outcome key combination.
- `emotional-reaction-director` owns the private director LLM system/user
  wrapper plus temperature and max-token settings.
- `emotional-reaction-director-repair-*` templates provide targeted retry instructions
  for specific rejection reasons (e.g. `response-posture-omits-primary-emotion`,
  `unsupported-primary-emotion`, and `drafted-chat-reply`).
- `emotional-reaction-performance-direction` owns the reusable DATEE performance
  wrapper that renders the validated director fields before the final DATEE
  response instruction.

The outcome keys intentionally reuse the delivery instruction vocabulary:
`clean`, `strong`, `critical`, `exceptional`, `nat20`, `fumble`, `misfire`,
`trope_trap`, `catastrophe`, and `nat1`. These entries are direction prompts,
not final DATEE replies.

`EmotionalReactionEventCompiler` composes these catalog entries with the current
`DateeContext.EmotionalTurnEvent`, the delivered player message, recent visible
history, and the datee's generated therapist diagnosis fields. The result is a
private `PromptTraceResult` consumed by
`PinderLlmAdapter.GenerateEmotionalDirectionAsync`. Delivered message and history
sender/message values are code-owned JSON string literals, so transcript
newlines and heading-like text remain data inside the private packet. Trace
spans continue to attribute the complete encoded literals to their runtime keys;
escaping behavior is not prompt-catalog configuration.

The director operation is callable inside `Pinder.LlmAdapters` and returns a
validated seven-field private direction object:
`primary_emotion`, `intensity`, `underlying_feeling`, `interpretation`,
`impulse`, `restraint`, and `response_posture`. `intensity` describes the
emotion's strength and movement while `underlying_feeling` identifies the
feeling beneath it.

The production `PinderLlmAdapter.GetDateeResponseAsync(context, history, ct)`
path treats DATEE response generation as a two-call operation: it requires
`DateeContext.EmotionalTurnEvent`, runs the private director once, then builds
the visible DATEE performance prompt with the validated direction. Before
parsing or constructing history, the performance recovery attempt rejects the
private direction header and seven labeled field prefixes with the sanitized
`private_direction_leak` reason. Matching is ordinal case-insensitive after
normalizing line-leading Unicode punctuation, symbols, and whitespace, including
Markdown, quotes, inline code, and strikethrough decoration. Numbered-list
prefixes are handled separately. Exact field prefixes and the header boundary
remain required to avoid matching ordinary prose. Catalog startup validation
requires the exact plain header and
seven `Label: {placeholder}` structural lines in
`emotional-reaction-performance-direction`; reusable surrounding prose remains
editable YAML. The public
ordinary `SessionDocumentBuilder.BuildDateePrompt/BuildDateePromptEx` methods
remain unchanged for non-production/test prompt callers; the adapter uses an
internal performance builder so reusable prompt prose stays in YAML and
director field values are traced to runtime source keys.

## Admin-editor wiring

Post-migration, every file in `data/prompts/` is registered in
`pinder-backend`'s existing GET / PUT / list endpoints (the same
mechanism that round-trips `data/items`, `data/anatomy`, etc.).
ruamel preserves comments and key ordering on PUT. An operator can
edit a prompt in the admin UI, save, and a process restart picks up
the change without a code redeploy.

> **Historical const-migration note.** During the Phase 1-5 const-migration,
> CI grep gates were added to ensure byte-identical rendered output. The legacy
> `delivery-prompt` instructions were also removed at that time. These are kept
> for historical reference only.

For every migrated prompt: the yaml string and the legacy C# const
string must produce **byte-identical** rendered output (after
`{token}` substitution if applicable) until the const is deleted in
Phase 5. Tests pin this byte-equality so a Phase-N PR that
accidentally tunes a string in the yaml without touching the const —
or vice versa — fails loudly. After Phase 5 the const is gone and
the test is rewritten to lock the yaml render alone.

## Overlay Transport Routing

Overlay calls (horniness/trap/shadow-corruption) use a second, optional ILlmTransport passed to PinderLlmAdapter's constructor. When omitted, overlays use the same transport as primary game-turn calls. There is no vendor-specific overlay routing inside the adapter — the host application controls which model/vendor handles overlays purely by which transport instance it constructs and passes in. (GameApi wiring for this is tracked in a separate follow-up ticket.)

## DATEE Performance Structured Output

`datee-response-instruction` now describes the configurable semantics for the `datee_performance.v1` structured output contract. The visible `message` field is the only text that may enter chat history. Private `signals.tell` and `signals.weakness` are typed engine diagnostics and must stay out of semantic conversation messages.
