# Pinder.SessionSetup

Provider-independent LLM helpers used by `Pinder.GameApi` for character
synthesis and pre-session scene setup. Every prompt comes from the shared
`data/prompts` catalog and every call goes through `ILlmTransport`.

## Character synthesis

`Pinder.GameApi.Services.CharacterSynthesisService` owns the production
orchestration. A full regeneration makes six logical LLM calls in this order:

1. Consolidate personality fragments using only personality fragments, texting
   style, stats, and the game system prompt plus the configurable personality
   consolidation prompt.
2. Consolidate backstory fragments using only backstory fragments, texting
   style, stats, and the game system prompt plus the configurable backstory
   consolidation prompt. Steps 1 and 2 run concurrently.
3. Generate the lies-versus-reality backstory from both consolidated outputs.
4. Generate the 15-line psychological stake from the generated backstory and
   consolidated personality.
5. Generate the therapist diagnosis from the stake and generated backstory.
6. Generate the bio from backstory, stake, and diagnosis.

All required outputs are validated before `PatchSynthesisDataAsync` mutates the
character. Transport, parsing, validation, and persistence failures propagate to
the caller and are recorded as failed operations. Generators may perform bounded
validation retries, so the physical provider-call count can exceed six.

`SequentialSynthesisPipeline` remains a compatibility helper. It does not run
the two consolidation calls and is not the authoritative production workflow.

## Session scene setup

Session setup consumes the character's persisted generated bio, generated
lies-versus-reality backstory, stake, diagnosis, and consolidated personality.
It does not receive raw personality fragments. The dramatic arc generator then
makes one logical LLM call from the two characters' bios and stakes; bounded
validation retries can add provider calls.

Turn-0 conversation history is seeded with exactly two bio scene entries. Outfit
generation is not part of production session setup. `LlmOutfitDescriber` remains
only for legacy tests and tooling.

## Output and failure contracts

- Psychological stake is exactly 15 top-level `- ` markdown bullets. Headings,
  emphasis, nested or numbered lists, code, and blockquotes are forbidden.
- Backstory, diagnosis, bio, and dramatic arc use their prompt-catalog contracts
  and parsers; malformed required output is an observable failure, not an empty
  success.
- Cancellation propagates unchanged.
- Optional dramatic-arc degradation is available only when the caller supplies
  an explicit degradation callback. There is no implicit text fallback.
- Streaming helpers yield raw fragments and throw transport or cancellation
  failures. The web tier owns SSE stage events and final sanitization.

## Prompt wiring

Call `PromptWiring.Wire(data/prompts)` before constructing generators. It loads
the catalog, structural fragments, archetype behavior, and texting-style
conflicts. Missing required prompt entries fail explicitly; hardcoded prompt
fallbacks are not permitted.
