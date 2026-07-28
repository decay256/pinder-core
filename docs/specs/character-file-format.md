# Character File Format - v2

**Status:** v2 (`schema_version: 2`).
**Schema file:** [`data/characters/character-schema.json`](../../data/characters/character-schema.json) (Draft 7).
**Wire-shape POCO:** `Pinder.Core.Characters.CharacterDefinition`.
**Reader:** `Pinder.SessionSetup.CharacterDefinitionLoader`.
**Writer:** `Pinder.Core.Characters.CharacterDefinitionWriter`.

This document is the canonical short description of the on-disk Pinder
character file. The schema file and C# POCO are the binding artifacts; this
document explains the contract they enforce.

## What is a character file?

A character file is a single `.json` file in `data/characters/` or any
directory-backed character store. The file is the portable artifact: copy it
between stores and the loader treats the `character_id` inside the payload as
the stable identity.

A v2 character file describes:

- Identity: `character_id`, `name`.
- Public presentation: `gender_identity`, `bio`, and optional timing profile.
- Player-authored build: `level`, `items`, `anatomy`, `allocation`.
- Generated formulation fields used by runtime prompts after synthesis.

It does not describe:

- Computed stat bonuses from items or anatomy; those are recomputed at load.
- Live game state such as turns, current session shadows, traps, or history.
- Account/global progression, XP ledgers, wallet state, or purchases.

## Identity

- `character_id` is a UUIDv4 and is the canonical identity.
- The filename slug, for example `gerald.json`, is presentation only.
- Renaming a file does not change identity. Editing `character_id` forks it.
- A store should reject two files with the same `character_id`.

## Schema Versioning

- v2 files set `schema_version: 2`.
- The current loader rejects missing, malformed, or unknown schema versions.
- v1 files are not accepted by this loader. Legacy compatibility is handled by
  regeneration/migration before runtime use, not by fabricating missing fields
  during load.

v2 introduced normalized float anatomy values and now carries generated
character formulation fields. The loader may parse older v2 payloads that omit
optional generated fields so regeneration tools can complete them, but a
bundled/runtime-ready DATEE must have complete synthesis data.

## Allocation and Anatomy

The on-disk `allocation` block carries only player-authored values:

- `allocation.spent`: build points assigned to each positive stat.
- `allocation.unspent_pool`: build points not yet assigned.
- `allocation.shadows`: starting shadow values.

Bonuses are never serialized. `CharacterDefinitionLoader.Load` assembles a
`CharacterProfile` through `CharacterAssembler`, resolving item ids and anatomy
parameter values against the configured repositories.

In v2, `anatomy` values are normalized numbers in `[0..1]`, keyed by anatomy
parameter id. They are not legacy string tier ids.

## Generated Formulation Fields

The synthesis pipeline can populate these fields:

- `consolidated_personality`: standardized personality source built from raw
  personality fragments.
- `consolidated_backstory`: standardized backstory source built from raw
  backstory fragments.
- `backstory_categories`: 20 named lie/reality facts.
- `psychological_stake`: permanent markdown stake text.
- `stake_lines`: exactly 15 structured psychological stakes.
- `psychiatric_diagnosis`: flat therapist formulation map.

Bundled characters that can be used directly in sessions must include the full
set. Regeneration tooling may temporarily read a v2 character that lacks some
of these fields, but runtime systems that consume therapist diagnosis validate
the required map through `TherapistDiagnosisContract` and fail loudly if it is
missing or incomplete.

## Therapist Formulation

`psychiatric_diagnosis` is a flat object with these required nonblank fields,
in this canonical order:

- `derived_feeling`
- `defense_reaction`
- `safe_connection`
- `hurt_protection`
- `repair_requirement`
- `charm_reaction`
- `rizz_reaction`
- `honesty_reaction`
- `chaos_reaction`
- `wit_reaction`
- `self_awareness_reaction`

These values are prompt-style character instructions, not analysis notes. They
should be directly usable by downstream emotional-reaction compilation.

The authored JSON schema is closed for `psychiatric_diagnosis`; starter files
must not carry extra diagnosis fields. The runtime contract intentionally
validates required fields only so controlled regeneration boundaries can
preserve or normalize legacy extras before writing canonical files.

## Public Boundary

The public/player-facing dating-card surface is name, gender identity, bio, and
an outfit/visible-item signal. Private synthesis fields such as psychological
stake, backstory facts, stake lines, therapist diagnosis, stats, archetype
directives, and the assembled system prompt are not public card data.

Core represents this boundary with `DateeVisibleProfile` and
`PublicProfileCard`; host applications should not expose private formulation
fields through player-visible DTOs.

## Property Ordering and Whitespace

The writer produces deterministic JSON with 2-space indentation, LF line
endings, UTF-8 without BOM, and a single trailing newline.

Top-level writer order is:

`schema_version`, `character_id`, `name`, `gender_identity`, `bio`, `level`,
optional `timing_profile_id`, `items`, `anatomy`, optional `surface_material`, `allocation`, optional
`psychological_stake`, optional `consolidated_personality`, optional
`consolidated_backstory`, optional `backstory_categories`, optional
`stake_lines`, optional `psychiatric_diagnosis`.

Inside `allocation`, order is `spent`, `unspent_pool`, `shadows`. Stat and
shadow keys follow the enum wire order pinned by writer tests.

## Schema Reference

For the binding shape, see
[`data/characters/character-schema.json`](../../data/characters/character-schema.json).
Tests in `tests/Pinder.Core.Tests/CharacterSchemaValidationTests.cs` validate
starter files against this schema, and
`tests/Pinder.Core.Tests/TherapistDiagnosisContractTests.cs` pins parity
between runtime required fields, schema fields, and the diagnosis prompt.

## Related Tickets

- #814 - original POCO/schema.
- #815 - writer and format documentation.
- #1175 - v2 scalar anatomy.
- #1244/#1251 - bundled character synthesis and therapist formulation contract.

## Surface Material

surface_material is an optional typed visual-material block for browser/Unity-equivalent material controls that are not categorical anatomy bands. It carries smoothness in Unity's authored 0-100 range, freckles_pattern_id, and exactly two surface_layers with strength 0-2, tiling 1-50, rotation 0-10, and pattern_id. Pattern ids are host-resolved strings and are intentionally not stored in anatomy.

Legacy anatomy fields venicus and blemishes are not active fields. Reads migrate venicus to veins only when veins is missing; new writes containing either field are rejected.
