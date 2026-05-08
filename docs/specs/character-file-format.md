# Character File Format — v1

**Status:** v1 (`schema_version: 1`).
**Schema file:** [`data/characters/character-schema.json`](../../data/characters/character-schema.json) (Draft 7).
**Wire-shape POCO:** `Pinder.Core.Characters.CharacterDefinition`.
**Reader:** `Pinder.SessionSetup.CharacterDefinitionLoader`.
**Writer:** `Pinder.Core.Characters.CharacterDefinitionWriter`.

This document is the canonical description of the on-disk format of a Pinder character. Everything below is intentionally short and prescriptive; the schema file and the C# POCO are the binding artifacts, and this doc explains them.

## What is a character file?

A single `.json` file in `data/characters/` (or, in a future ticket, anywhere a `DirectoryCharacterStore` is pointed at). The file is the artifact: send the file, drop it in someone else's characters directory, their game picks it up.

A character file describes:

- The character's **identity** (`character_id`, `name`).
- The character's **presentation surface** to the opposing player (`gender_identity`, `bio`).
- The character's **build** at creation time (`level`, `items`, `anatomy`, `allocation`).

It does **not** describe:

- Computed stat bonuses from items or anatomy (recomputed every load).
- Live game state (turns played, current shadow stat values during a session, weakness windows, etc.). That belongs to `GameSession`, not the character file.

## Identity: `character_id` vs. filename slug

- `character_id` is a UUIDv4. **It is the canonical identity.** Two files with the same `character_id` describe the same character; they should not coexist in a single store.
- The **filename slug** (e.g. `gerald.json`) is presentation only — a human-friendly handle for the file on disk. Renaming the file does not change the character's identity. Stores (see issue #816) MUST resolve `character_id → file` by reading each file's `character_id`, not by parsing the filename.
- Renaming a file is safe. Editing a file's `character_id` is not (it forks the identity).

## Schema versioning: `schema_version`

- v1 files set `schema_version: 1`. The reader rejects any other value with a `FormatException`.
- v1 has no migration story. There is no v0 → v1 shim; the prototype format was dropped wholesale when v1 was introduced.
- Future versions (v2+) will need a migration step. The expected pattern is: bump the const in the schema, write a `CharacterDefinitionMigrator` that reads the lower-version POCO and projects to the higher-version POCO, and surface the migration result through `CharacterDefinitionLoader` so callers see the latest version regardless of what's on disk.

## Allocation vs. bonuses split

The on-disk `allocation` block carries **only player-authored values**:

- `allocation.spent` — build points the player has assigned to each positive stat. Mutable in the gameplay sense (the player can redistribute), immutable as a value object on disk (rewrite the file when redistributing).
- `allocation.unspent_pool` — build points not yet allocated. Starter files all set this to `0`; future "I leveled up but haven't picked where to put it" UX will increment this.
- `allocation.shadows` — starting shadow values. Distinct from in-session shadow drift, which lives on `GameSession`.

**Bonuses are never serialised.** When `CharacterDefinitionLoader.Load` runs, it hands the parsed `CharacterDefinition` to `CharacterAssembler`, which resolves item ids and anatomy tier ids against `IItemRepository` / `IAnatomyRepository` and recomputes the `CharacterProfile`'s effective stats fresh every time. This means: changing an item's stat modifier in `data/items/starter-items.json` propagates to every character that equips that item, with no migration of character files needed.

## Property ordering & whitespace

The file format is byte-stable. The writer guarantees:

- Top-level property order: `schema_version`, `character_id`, `name`, `gender_identity`, `bio`, `level`, `items`, `anatomy`, `allocation`.
- Inside `allocation`: `spent`, `unspent_pool`, `shadows`.
- Inside `spent`: `charm`, `rizz`, `honesty`, `chaos`, `wit`, `self_awareness` (matches `StatType` enum).
- Inside `shadows`: `madness`, `despair`, `denial`, `fixation`, `dread`, `overthinking` (matches `ShadowStatType` enum).
- 2-space indent, single trailing newline (LF), UTF-8 without BOM.
- `character_id` formatted with hyphens, lowercase (`Guid.ToString("D")`).
- `Write(Parse(file)) == file` byte-equal for every starter file. The round-trip test (`CharacterDefinitionWriterTests.Write_ParsedStarterFile_RoundTripsByteEqual`) pins this contract; anyone editing the writer is expected to keep it green.

## Sharing a character

The file is the sharing artifact.

1. Hand someone a `.json` (chat attachment, drag-and-drop, network share — doesn't matter).
2. They drop it into their characters directory (or, eventually, the equivalent UI).
3. Their `DirectoryCharacterStore.LoadAsync(id)` will pick it up the next time it's enumerated.

There is intentionally no built-in network transport in v1. The remote-store interface (`IRemoteCharacterStore`, issue #817) ships separately in this same sprint and is consumed by Eigencore later (issue #819, gated). Today: copy the file.

## Forward compatibility

- **Adding a property in v2:** new top-level property, schema version bump to `2`, `CharacterDefinition` POCO grows a property, writer emits it after existing properties (preserving v1's order so v1-aware readers can still locate everything they need; v1 readers will reject v2 files outright, which is the intended semantic). Migration code converts v1 to v2 by populating the new property with a default.
- **`character_id` collisions on import:** if a user drops a file with a `character_id` that already exists in their store, v1 implementations should **reject** the import (raise an error, refuse to overwrite). Last-write-wins or a UI dialog is intentionally out of scope for v1; revisit when an import UI exists. The `ICharacterStore.SaveAsync` contract (issue #816) follows this rule.
- **`character_id` reuse across stores:** two stores can hold a copy of the same `character_id`. That's fine — they're describing the same character. Asset-store mirrors (issue #817+) explicitly assume this pattern.

## Schema reference

For the binding shape, see [`data/characters/character-schema.json`](../../data/characters/character-schema.json). The fields, their types, their bounds, and `additionalProperties: false` are all enforced there. Tests in `tests/Pinder.Core.Tests/CharacterSchemaValidationTests.cs` validate every starter file against this schema directly (not via the parser).

## Related tickets

- #814 — POCO + v1 schema.
- #815 — this writer + this doc.
- #816 — `ICharacterStore` async + `DirectoryCharacterStore` (consumes this format).
- #817 — `IRemoteCharacterStore` interface for future remote stores.
- #818 — asset-attribute vocabulary spec (the wire contract between Pinder and the future asset backend).
- #819 — *gated* — Eigencore wrapper. Do not start.
