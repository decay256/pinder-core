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
  background.yaml
  templates.yaml
  archetypes.yaml
  structural.yaml
  narrative.yaml
  stake.yaml
```

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
