# Character Asset Attribute Vocabulary v1 (Pinder ↔ Eigencore contract)

**Status:** v1 (`asset_kind = "character/v1"`).
**Scope:** the wire-level contract between Pinder and an external asset backend (Eigencore today; in principle any backend that wants to host Pinder character assets). This document is **doc only**; the binding interface in code is `Pinder.Core.Characters.IRemoteCharacterStore` plus the `CharacterAssetMetadata` POCO it traffics in (see issue #817).

This spec exists because Eigencore stores **opaque payload blobs + a small fixed set of indexable key/value attributes** per blob. Eigencore intentionally does not parse Pinder character JSON. That keeps Eigencore generic — it can host item packs, saves, or replays later — at the cost of forcing Pinder to redundantly project a curated subset of identity / discovery fields out of the payload into the metadata envelope.

## What Pinder commits to

For every character published to a remote asset store, Pinder writes the attribute set defined below. Names, types, and semantics are stable across Pinder versions until the vocabulary is bumped.

## What the asset backend commits to

- Indexing every required attribute below.
- Supporting the query semantics described in [§ Query semantics](#query-semantics).
- Round-tripping every attribute unchanged across publish → fetch → re-publish.
- Refusing writes that supply server-controlled attributes (timestamps, owner identity) — the server stamps those itself.

## Attribute table — v1

Each attribute lives in the metadata envelope, NOT inside the character payload. The payload is the byte-stable v1 character JSON (see [`character-file-format.md`](character-file-format.md)).

### `asset_kind`

- **Type:** string.
- **Required:** yes.
- **Set by:** client.
- **Allowed values:** `"character/v1"` for v1 character files. Future kinds (item packs, anatomy packs, saves, replays) reuse the metadata envelope with a different value.
- **Semantics:** discriminates what's inside the opaque payload. Bumped together with the on-disk schema; v2 character files use `"character/v2"`.
- **Pinned in code by:** `CharacterAssetMetadata.AssetKindCharacterV1`.

### `character_id`

- **Type:** string (UUIDv4 in `D` format — lowercase, hyphenated).
- **Required:** yes.
- **Set by:** client. Must equal `CharacterDefinition.character_id` inside the payload.
- **Semantics:** stable identity. The asset backend treats this as the asset's primary key — two publishes with the same `character_id` overwrite the same asset.

### `owner_id`

- **Type:** string.
- **Required:** yes.
- **Set by:** **server** (from the authenticated session — clients cannot lie about ownership).
- **Semantics:** the publishing user, in whatever id format the asset backend uses internally. Pinder treats it as opaque; it is fine for `owner_id` to be empty for service-owned / official assets.

### `is_public`

- **Type:** boolean.
- **Required:** yes.
- **Set by:** client.
- **Default:** `false`.
- **Semantics:** whether the asset is reachable in unauthenticated queries. The asset backend is also free to apply additional auth-based visibility — `is_public: true` is necessary, not sufficient.

### `tags`

- **Type:** array of strings.
- **Required:** no (absent or empty array both mean "no tags").
- **Set by:** client (with reserved-prefix exceptions, see below).
- **Constraints:**
  - At most 16 tags per asset.
  - Each tag is 1..32 characters, lowercase ASCII letters / digits / dashes (`[a-z0-9-]+`), no leading or trailing dash.
  - Tags are case-sensitive on the wire (always lowercase) and the asset backend MAY reject mixed-case input rather than silently lowercasing it.
- **Reserved prefixes (server-enforced):**
  - `official-` — applied only to assets owned by an account on the Pinder team allow-list. Clients that put `official-*` on their own assets get the tag stripped before storage.
  - `auto-` — applied only by server-side automation (content classifiers, abuse-reporting bots, etc.). Client-supplied `auto-*` tags are stripped.
  - Both prefixes use a hyphen rather than a colon to keep tags inside the `[a-z0-9-]+` charset.
- **Semantics:** free-form discovery hints. The contract is "match-all" on the query side — see [§ Query semantics](#query-semantics).

### `created_at`

- **Type:** RFC3339 timestamp string in UTC (`2026-05-08T22:34:00Z`).
- **Required:** yes.
- **Set by:** **server**, exactly once at first publish.
- **Semantics:** wall-clock creation time. Never overwritten on later updates.

### `updated_at`

- **Type:** RFC3339 timestamp string in UTC.
- **Required:** yes.
- **Set by:** **server**, on every overwrite (and at first publish, equal to `created_at`).
- **Semantics:** wall-clock last-modified time. The default sort order for queries is `updated_at desc`.

## Out of vocabulary for v1, by design

- `level_range`, `archetype`, `gender_identity` — derivable from the payload at load time. Adding them as indexable attributes would leak payload structure into the asset backend and double the work of every payload-shape change.
- Ratings, popularity, play counts, view counts — separate concern. If they're added, they live on a sibling attribute namespace owned by whatever subsystem produces them, not on the Pinder publishing path.
- Trust / verification flags — out of scope. The `official-` tag prefix is the v1 substitute.
- Free-text full-text search on payload — out of scope. Pinder is responsible for any payload-level search; the asset backend treats the payload as opaque bytes.

## Query semantics

The asset backend must support:

- **Exact match** on `asset_kind`, `owner_id`, `character_id`, `is_public`.
- **All-of** match on `tags` (i.e. the asset's `tags` is a superset of the query's `tags`). No `OR` semantics in v1.
- **Range queries** on `created_at` and `updated_at` (server-defined date filter syntax — typically `created_at >= ...` / `<= ...`).
- **Combined AND** of any of the above.
- **Cursor pagination** with an opaque server-defined cursor format. The Pinder client treats the cursor as a string token; the server's encoding (offset, last-id, opaque hash) is implementation-defined.
- **Default sort:** `updated_at desc`. The server MAY offer additional sort orders; clients SHOULD NOT depend on any sort order beyond the default.

The query surface in `CharacterAssetQuery` (see [`IRemoteCharacterStore`](../../src/Pinder.Core/Characters/IRemoteCharacterStore.cs) and [`CharacterAssetQuery`](../../src/Pinder.Core/Characters/CharacterAssetQuery.cs)) is the binding code-level shape; it is intentionally a strict subset of the wire surface above so that v1.x server-side additions don't immediately demand client-side changes.

Out of scope for query in v1:

- `OR` between filters.
- Sort orders other than the default and `updated_at desc`.
- Geographic / locale / language filters.

## Wire format

All examples are HTTP/1.1 over TLS. The asset backend MAY support additional protocols (gRPC, etc.) as long as the same attribute set + semantics are honoured.

### Publish

```
POST /assets
Content-Type: multipart/form-data; boundary=...

  Part 1: name="metadata", Content-Type=application/json
    {
      "asset_kind": "character/v1",
      "character_id": "59aa20f2-46d6-4adc-89c1-6ea17f815020",
      "is_public": true,
      "tags": ["starter", "official-pack"]
    }

  Part 2: name="payload", Content-Type=application/json
    <raw v1 CharacterDefinition JSON, byte-equal to the on-disk file>
```

Server fills in `owner_id`, `created_at`, `updated_at` and returns the full metadata. Client `tags` containing reserved prefixes are stripped before storage.

### Fetch

```
GET /assets/{character_id}
```

Returns payload bytes (same JSON as on disk) plus metadata. The exact mechanism — `X-Asset-Metadata` header with base64-encoded JSON, sidecar response, multipart response — is left to the implementation (#819 + the Eigencore-side ticket); whichever channel carries it MUST round-trip the full attribute set.

### Query

```
GET /assets?asset_kind=character/v1&is_public=true&tags=official-pack&limit=50&cursor=...
```

Returns:

```json
{
  "items": [
    {
      "asset_kind": "character/v1",
      "character_id": "...",
      "owner_id": "...",
      "is_public": true,
      "tags": ["..."],
      "created_at": "...",
      "updated_at": "..."
    }
  ],
  "next_cursor": "opaque-string-or-null"
}
```

When the client requests multiple `tags`, repeat the query parameter (`?tags=a&tags=b`). Multiple values use AND semantics.

## Forward compatibility

- **New optional attributes** can land in v1.x. Clients MUST tolerate unknown attributes on read (the `CharacterAssetMetadata` POCO ignores unknown fields).
- **Renaming or removing an attribute, or changing its type, requires bumping `asset_kind`.** Old clients query `asset_kind=character/v1` and continue to ignore v2 assets; new clients query both kinds during a transition window.
- **Reserved prefix list** is part of the contract, not an implementation detail. Adding a new reserved prefix is a v1.x feature; removing one is a v2 break.
- **Query semantics** can grow in v1.x (e.g. adding a sort-order parameter) but cannot change the meaning of existing parameters.
- This doc is versioned. New vocabulary versions go in a new top-level section; never edit the v1 table to retcon meaning.

## Cross-references

- Code-level metadata POCO + interface: [`src/Pinder.Core/Characters/CharacterAssetMetadata.cs`](../../src/Pinder.Core/Characters/CharacterAssetMetadata.cs), [`IRemoteCharacterStore.cs`](../../src/Pinder.Core/Characters/IRemoteCharacterStore.cs), [`CharacterAssetQuery.cs`](../../src/Pinder.Core/Characters/CharacterAssetQuery.cs), [`CharacterAssetPage.cs`](../../src/Pinder.Core/Characters/CharacterAssetPage.cs). All shipped in #817.
- On-disk file format: [`character-file-format.md`](character-file-format.md) (issue #815).
- Local store implementation: `Pinder.SessionSetup.DirectoryCharacterStore` (issue #816).
- Pending Eigencore wrapper implementation: issue #819 — **gated; do not start.**
- Parent epic: issue #264.
