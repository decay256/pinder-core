# Prompts

This document is the authoritative map of every LLM prompt template
that pinder-core / pinder-web emit, and the migration plan to lift
them out of C# constants into yaml files under `data/prompts/`.

Issue [#843](https://github.com/decay256/pinder-core/issues/843)
sequenced the migration into 5 phases, each shipping as its own PR:

1. **Phase 1 — Loader + first call-site (stake).** Land the catalog
   loader, the substitution helper, and migrate `LlmStakeGenerator`.
   Const fallback retained for backward compat. **Status: shipped.**
2. **Phase 2 — `PromptTemplates` batch.** Migrate
   `DialogueOptionsInstruction`, `DateeResponseInstruction`,
   `InterestBeatInstruction`, `FailureDeliveryInstruction`, and the
   delivery-tier defaults (`DefaultClean`, `DefaultStrong`, etc).
3. **Phase 3 — `PromptBuilder` + `SessionSystemPromptBuilder` structural
   strings.** Section labels, separators, header glyphs.
4. **Phase 4 — `ArchetypeCatalog` migration.** ~273 LOC of archetype
   behavior + sample lines — the largest single file.
5. **Phase 5 — Cleanup + CI grep gate + docs.** Delete every remaining
   `const string` carrying prompt content. Add a CI gate that fails
   any PR re-introducing one. Refresh this doc.

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
  raises `KeyNotFoundException` so a prompt referencing
  `{undefined_token}` fails loudly at the call-site rather than
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

## Prompt registry (status as of Phase 1)

The table below tracks every prompt site in the engine. **Status**
column reads as: `yaml` = read from `data/prompts/`, `const` = still
embedded in C#, `partial` = phase wired the call-site but the const
fallback is still present (Phase 1-4).

- `stake` — `LlmStakeGenerator.SystemPrompt` + `BuildUserMessage` —
  **status: partial** (Phase 1).
- `outfit` — `LlmOutfitDescriber.SystemPrompt` — **status: const**
  (Phase 2 candidate).
- `dialogue_options` — `PromptTemplates.DialogueOptionsInstruction`
  — **status: const** (Phase 2).
- `datee_response` — `PromptTemplates.DateeResponseInstruction`
  — **status: const** (Phase 2).
- `interest_beat` — `PromptTemplates.InterestBeatInstruction` —
  **status: const** (Phase 2).
- `failure_delivery` — `PromptTemplates.FailureDeliveryInstruction`
  — **status: const** (Phase 2).
- Delivery-tier defaults
  (`DefaultClean`, `DefaultStrong`, `DefaultCritical`,
  `DefaultExceptional`, `DefaultTest`, `DefaultRegisterInstruction`,
  `DefaultMediumRule`) — **status: const** (Phase 2).
- `prompt_builder_section_labels` —
  `PromptBuilder.BuildSystemPrompt` headers + glyphs — **status:
  const** (Phase 3).
- `session_system_prompt` — `SessionSystemPromptBuilder.Build` /
  `BuildPlayer` / `BuildDatee` — **status: const** (Phase 3).
- `archetype_catalog` — `ArchetypeCatalog._behaviors` (12 archetypes)
  — **status: const** (Phase 4).
- `player_response_delay` —
  `PlayerResponseDelayEvaluator.DefaultTestPrompt` — **status: const**
  (Phase 5 cleanup batch).

## File layout (target after Phase 5)

```
data/prompts/
  stake.yaml                 (Phase 1)
  outfit.yaml                (Phase 2 candidate)
  dialogue-options.yaml      (Phase 2)
  datee-response.yaml     (Phase 2)
  interest-beat.yaml         (Phase 2)
  delivery.yaml              (Phase 2; delivery-tier defaults)
  prompt-builder.yaml        (Phase 3; section labels + glyphs)
  session-prompt.yaml        (Phase 3; section ordering)
  archetypes.yaml            (Phase 4)
  misc.yaml                  (Phase 5 catch-all if needed)
```

## Admin-editor wiring (target end of Phase 5)

Post-migration, every file in `data/prompts/` is registered in
`pinder-backend`'s existing GET / PUT / list endpoints (the same
mechanism that round-trips `data/items`, `data/anatomy`, etc.).
ruamel preserves comments and key ordering on PUT. An operator can
edit a prompt in the admin UI, save, and a process restart picks up
the change without a code redeploy.

## CI grep gate (Phase 5)

Phase 5 adds a test that:

1. Greps `src/Pinder.Core/`, `src/Pinder.LlmAdapters/`,
   `src/Pinder.SessionSetup/` for `const string` declarations whose
   value contains any of the prompt-content sentinels (e.g. `"You are"`,
   `"OUTPUT:"`, `"# OPTION_1"`).
2. Fails the build if any such const survives.
3. Has an explicit allow-list of strings that are non-prompt
   content (e.g. error messages, log templates) to avoid false
   positives.

## Round-trip determinism contract

For every migrated prompt: the yaml string and the legacy C# const
string must produce **byte-identical** rendered output (after
`{token}` substitution if applicable) until the const is deleted in
Phase 5. Tests pin this byte-equality so a Phase-N PR that
accidentally tunes a string in the yaml without touching the const —
or vice versa — fails loudly. After Phase 5 the const is gone and
the test is rewritten to lock the yaml render alone.
