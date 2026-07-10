# Character Asset Attribute Vocabulary v1 (Pinder ↔ Eigencore contract)

> **Third-party app boundary.** Eigencore is treated as a third-party generic asset backend. Pinder adapts to its published interface; any Pinder-specific change to that interface needs to clear the "would another game built on eigencore also want this change?" bar before being filed against eigencore. Drift discovered on the Pinder side is fixed on the Pinder side (this spec + `Pinder.RemoteAssets`), not by pushing Pinder terminology back into eigencore's API.

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

### `asset_id`

- **Type:** string (UUIDv4 in `D` format — lowercase, hyphenated).
- **Required:** yes.
- **Set by:** client. Must equal the character UUID inside the payload (the `CharacterDefinition` identity field — see [`character-file-format.md`](character-file-format.md)).
- **Semantics:** stable identity. The asset backend treats this as the asset's primary key — two publishes with the same `asset_id` overwrite the same asset.

> **POCO naming note.** The C# POCO field on `CharacterAssetMetadata` stays `CharacterId` for Pinder-side clarity (the payload's primary identity is still a character UUID). `Pinder.RemoteAssets` renames at the HTTP boundary: `CharacterId` ↔ wire `asset_id` (path, JSON, `X-Asset-Metadata` header). The wire is eigencore's namespace; the POCO is Pinder's.

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
- **Reserved prefixes (server-enforced, loud-reject on violation):**
  - `auto-` — server-only. Reserved for server-side automation (content classifiers, abuse-reporting bots, etc.). Any client-supplied tag starting with `auto-` is **hard-rejected with HTTP 403 `permission_denied`**, regardless of caller, allow-list, or scope. No client can ever publish an `auto-*` tag.
  - `official-` — per-OAuth2-client allow-listed via `OAuth2Client.allowed_tag_prefixes` on the asset backend. A client whose OAuth2 client record includes `official-` in its allowed prefix list may publish `official-*` tags; every other client gets **HTTP 403 `permission_denied`** when it tries. Allow-list membership is configured server-side by the backend operator, not requestable over the wire.
  - Both prefixes use a hyphen rather than a colon to keep tags inside the `[a-z0-9-]+` charset.
  - **No silent mutation.** Earlier drafts of this spec described reserved-prefix violations as silently removed before storage. That is not what the deployed backend does and not what the contract should be — silently mutating client input is a footgun. The contract is loud-reject with 403 so the client learns immediately.
- **Semantics:** free-form discovery hints. The contract is "match-all" on the query side — see [§ Query semantics](#query-semantics).

### `created_at`

- **Type:** RFC3339 timestamp string in UTC (`2026-05-08T22:34:00Z` or `2026-05-08T22:34:00+00:00`).
- **Required:** yes.
- **Set by:** **server**, exactly once at first publish.
- **Semantics:** wall-clock creation time. Never overwritten on later updates.

### `updated_at`

- **Type:** RFC3339 timestamp string in UTC.
- **Required:** yes.
- **Set by:** **server**, on every overwrite (and at first publish, equal to `created_at`).
- **Semantics:** wall-clock last-modified time. The default sort order for queries is `updated_at desc`.

> **Timestamp serialization (informational).** The reference backend (Eigencore) uses Pydantic's default JSON datetime serialization, which emits an explicit `+00:00` UTC offset rather than the `Z` suffix. Both forms are RFC3339-valid. The Pinder wrapper MUST parse defensively and accept either (`Z`, `+00:00`, or any RFC3339 UTC offset that normalizes to zero). Do not depend on a specific UTC suffix when comparing strings; parse first, compare instants.

### `payload_size` (server-added, read-only)

- **Type:** integer (bytes).
- **Required:** present on every fetched / queried metadata record; absent on publish requests (clients MUST NOT supply it).
- **Set by:** **server**, on every publish.
- **Semantics:** the byte count of the stored opaque payload as the server received it. Useful for clients that want to surface storage cost or detect truncated payloads before parsing.
- **Forward-compat clause:** this is the first server-added read-only attribute beyond the timestamps. The general rule in [§ Forward compatibility](#forward-compatibility) (clients tolerate unknown attributes on read) covers it, but it is called out explicitly so wrapper implementations either expose it as a nullable property on `CharacterAssetMetadata` or document it as "tolerated and ignored" — silently dropping it is fine; lossy round-trip on re-publish is fine because clients never send it.

## Out of vocabulary for v1, by design

- `level_range`, `archetype`, `gender_identity` — derivable from the payload at load time. Adding them as indexable attributes would leak payload structure into the asset backend and double the work of every payload-shape change.
- Ratings, popularity, play counts, view counts — separate concern. If they're added, they live on a sibling attribute namespace owned by whatever subsystem produces them, not on the Pinder publishing path.
- Trust / verification flags — out of scope. The `official-` tag prefix is the v1 substitute.
- Free-text full-text search on payload — out of scope. Pinder is responsible for any payload-level search; the asset backend treats the payload as opaque bytes.

## Query semantics

The asset backend must support:

- **Exact match** on `asset_kind`, `owner_id`, `asset_id`, `is_public`.
- **All-of** match on `tags` (i.e. the asset's `tags` is a superset of the query's `tags`). No `OR` semantics in v1.
- **Range queries** on `created_at` and `updated_at` (server-defined date filter syntax — typically `created_at >= ...` / `<= ...`).
- **Combined AND** of any of the above.
- **Cursor pagination** with an opaque server-defined cursor format. The Pinder client treats the cursor as a string token; the server's encoding (offset, last-id, opaque hash) is implementation-defined. A malformed or expired cursor returns HTTP 422 with `error=invalid_cursor`; the wrapper SHOULD map that to a typed exception so callers can distinguish "bad cursor" from "bad query".
- **Default sort:** `updated_at desc`. The server MAY offer additional sort orders; clients SHOULD NOT depend on any sort order beyond the default.

The query surface in `CharacterAssetQuery` (see [`IRemoteCharacterStore`](../../src/Pinder.Core/Characters/IRemoteCharacterStore.cs) and [`CharacterAssetQuery`](../../src/Pinder.Core/Characters/CharacterAssetQuery.cs)) is the binding code-level shape; it is intentionally a strict subset of the wire surface above so that v1.x server-side additions don't immediately demand client-side changes.

Out of scope for query in v1:

- `OR` between filters.
- Sort orders other than the default and `updated_at desc`.
- Geographic / locale / language filters.

## Wire format

All examples are HTTP/1.1 over TLS. The asset backend MAY support additional protocols (gRPC, etc.) as long as the same attribute set + semantics are honoured.

### Route prefix

The asset endpoints live under `/api/v1/assets` on the reference backend. The wrapper's configured `BaseUrl` absorbs the `/api/v1` prefix — `Pinder.RemoteAssets` does not hard-code it, so a future `/api/v2` rev (or a backend that mounts under a different prefix) only changes config, not code. The example URLs below show only the relative path under that prefix for brevity.

### Publish

```
POST /assets
Content-Type: multipart/form-data; boundary=...

  Part 1: name="metadata", filename="metadata.json", Content-Type=application/json
    {
      "asset_kind": "character/v1",
      "asset_id": "59aa20f2-46d6-4adc-89c1-6ea17f815020",
      "is_public": true,
      "tags": ["starter", "official-pack"]
    }

  Part 2: name="payload", filename="payload.json", Content-Type=application/json
    <raw v1 CharacterDefinition JSON, byte-equal to the on-disk file>
```

Both parts are sent with a `filename` parameter so multipart parsers expose them as file parts rather than plain form values. Server fills in `owner_id`, `created_at`, `updated_at`, `payload_size` and returns the full metadata.

**Size caps.** The metadata part has a hard cap of **4 KiB** (`MAX_ASSET_METADATA_JSON_SIZE` on the reference backend; not env-overridable). The payload part has a default cap of **256 KiB** (`max_asset_payload_size`, env-overridable per backend deployment). Violations surface as HTTP 422 with `code=metadata_too_large` or `code=payload_too_large` respectively, so the wrapper can distinguish them from generic validation errors and surface a useful error to the caller without re-parsing.

**Reserved-prefix tag violations** (`auto-*` from any caller, `official-*` from a caller not on the allow-list) surface as HTTP 403 with `code=permission_denied`. See the `tags` attribute description.

### Fetch

```
GET /assets/{asset_id}
```

Returns payload bytes (same JSON as on disk) plus metadata.

**Metadata channel.** The reference backend ships the metadata envelope as an HTTP response header `X-Asset-Metadata` whose value is the metadata JSON encoded as **standard padded base64 per RFC 4648** — NOT base64url. The C# side decodes with `Convert.FromBase64String(...)`; the Python side decodes with `base64.b64decode(...)`. Do not use `WebEncoders.Base64UrlDecode(...)` or `base64.urlsafe_b64decode(...)`; padded `+` / `/` characters are part of the alphabet here and will round-trip through standard base64 only. The exact carrier mechanism (header vs. sidecar response vs. multipart response) is otherwise an implementation choice; whatever channel is used MUST round-trip the full attribute set.

### Query

```
GET /assets?asset_kind=character/v1&is_public=true&tag=official-pack&limit=50&cursor=...
```

Returns:

```json
{
  "items": [
    {
      "asset_kind": "character/v1",
      "asset_id": "...",
      "owner_id": "...",
      "is_public": true,
      "tags": ["..."],
      "created_at": "...",
      "updated_at": "...",
      "payload_size": 12345
    }
  ],
  "next_cursor": "opaque-string-or-null"
}
```

When the client wants to filter on multiple tags, **repeat the singular query parameter** (`?tag=a&tag=b`). Multiple values use AND semantics. The query parameter name is `tag` (singular, repeatable). Note that the plural form `tag` + trailing `s` is NOT recognized; the reference backend silently drops unknown query parameters rather than returning 422, so a client using the wrong parameter name receives **unfiltered results** with no error signal. Use the singular `tag` parameter; this is the contract.

## Forward compatibility

- **New optional attributes** can land in v1.x. Clients MUST tolerate unknown attributes on read (the `CharacterAssetMetadata` POCO ignores unknown fields). `payload_size` (above) is the first concrete example.
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
