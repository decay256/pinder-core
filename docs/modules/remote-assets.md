# Remote Assets

## Overview

`Pinder.RemoteAssets` is the HTTP client that talks to eigencore on Pinder's behalf. It is the **only** assembly in this repository that knows eigencore exists. The dependency arrow is strictly one-way: `Pinder.RemoteAssets → Pinder.Core`, never the reverse. `Pinder.Core`, the session runner, and every other assembly in this repo have zero knowledge of eigencore's types, endpoints, or schemas.

The architectural rule is explicit: **eigencore is a third-party app from Pinder's perspective** — equivalent to Stripe or Auth0. Pinder calls a documented API surface and gets opaque results. The wire vocabulary is defined in [`docs/specs/character-asset-vocabulary.md`](../specs/character-asset-vocabulary.md); any delta between Pinder's understanding and eigencore's deployed interface is fixed on Pinder's side (in this module + the spec) unless another game built on eigencore would also benefit. Drift found in May 2026 (`character_id`/`?tags=`/silent-strip vs eigencore's `asset_id`/`?tag=`/403-reject) was all fixed here as pinder-core#851; use that as the precedent.

## Key Components

| File | Description |
|------|-------------|
| `src/Pinder.RemoteAssets/Configuration.cs` | DI-friendly configuration bag: `BaseUrl`, `HttpMessageHandler` (injection seam for tests), `AuthTokenProvider` (Func&lt;CT, Task&lt;string&gt;&gt;), `CharacterPayloadParser` (delegate that converts payload bytes → `CharacterDefinition`, keeping the assembly free of a `Pinder.SessionSetup` dep), `MetadataSizeCapBytes` (default 4 096), `PayloadSizeCapBytes` (default 262 144), `DefaultRetryAfter`. |
| `src/Pinder.RemoteAssets/EigencoreCharacterStore.cs` | The `IRemoteCharacterStore` implementation. **Read path**: `LoadAsync`, `GetMetadataAsync`, `ExistsAsync` (all issue `GET /assets/{id}` and read the `X-Asset-Metadata` header). **Query path**: `QueryAsync` (issues `GET /assets?...`). **Write path**: `PublishAsync`, `SaveAsync` (thin delegate to `PublishAsync` with synthesised minimal metadata), `DeleteAsync`. `ListIdsAsync` deliberately throws `NotSupportedException` — the v1 wire has no list-all endpoint; discovery is via `QueryAsync`. One-retry budget on 429 for every operation. Network-level errors propagate as `HttpRequestException` (not wrapped). |
| `src/Pinder.RemoteAssets/CharacterAssetMetadataParser.cs` | Wire→POCO. Reads JSON metadata bytes (from `X-Asset-Metadata` response header on read endpoints, or from query response items). Decodes the header using `Convert.FromBase64String` — **RFC 4648 standard padded base64, NOT base64url**. Boundary-renames `asset_id` → `CharacterId`. Tolerates unknown attributes (forward-compat). |
| `src/Pinder.RemoteAssets/CharacterAssetMetadataSerializer.cs` | POCO→wire (outbound write path). Boundary-renames `CharacterId` → `asset_id`. Never emits `character_id`. Does not emit server-controlled fields (`owner_id`, `created_at`, `updated_at`, `payload_size`). Pinned by regression test. |
| `src/Pinder.RemoteAssets/Exceptions/RemoteAssetException.cs` | Abstract root of the typed exception hierarchy. Carries `StatusCode` (int, 0 when non-HTTP) and `ResponseBody` (verbatim body for triage). Network errors propagate as `HttpRequestException` and are NOT wrapped here. |
| `src/Pinder.RemoteAssets/Exceptions/RemoteAssetAuthException.cs` | 401 — invalid or expired credentials. |
| `src/Pinder.RemoteAssets/Exceptions/RemoteAssetForbiddenException.cs` | 403 — reserved-prefix tag violation (`auto-*` from any caller, `official-*` from a non-allowlisted caller) **or** not-owner on DELETE/overwrite. The wrapper does not enforce reserved-prefix rules itself; the server does. The wrapper surfaces the 403 cleanly and attempts best-effort extraction of the offending prefix from the response body. |
| `src/Pinder.RemoteAssets/Exceptions/RemoteAssetValidationException.cs` | 422 (generic) — malformed multipart or other validation failure. Carries an `Errors` string list parsed from the body. |
| `src/Pinder.RemoteAssets/Exceptions/RemoteAssetInvalidCursorException.cs` | 422 with `error=invalid_cursor` — the cursor passed to `QueryAsync` was malformed or expired. Distinct from generic validation so callers can restart pagination cleanly. |
| `src/Pinder.RemoteAssets/Exceptions/RemoteAssetTooLargeException.cs` | 422 with `code=metadata_too_large` or `code=payload_too_large`, **or** a pre-flight local check on `PublishAsync` before any HTTP call. Carries `Subject` (`"metadata"` or `"payload"`) and the cap that was exceeded. |
| `src/Pinder.RemoteAssets/Exceptions/RemoteAssetMalformedMetadataException.cs` | 200 with a missing or non-base64 `X-Asset-Metadata` header, or a valid header whose JSON is unparseable. Signals a server-side encoding bug, not a client mistake. |
| `src/Pinder.RemoteAssets/Exceptions/RemoteAssetRateLimitException.cs` | Second 429 (after one automatic retry with the server's `Retry-After` delay). Carries `RetryAfter`. |
| `src/Pinder.RemoteAssets/Exceptions/RemoteAssetServerException.cs` | 5xx, or any unexpected status code not otherwise mapped. Carries `StatusCode`. |

## Wire-Contract Summary

The full contract is in [`docs/specs/character-asset-vocabulary.md`](../specs/character-asset-vocabulary.md). What follows is the orientation a developer needs to use or extend this module.

**Endpoint set** (relative to `Configuration.BaseUrl`, which absorbs the `/api/v1` prefix):

| Operation | Method | Path |
|-----------|--------|------|
| Fetch character | `GET` | `/assets/{asset_id}` |
| Query characters | `GET` | `/assets` |
| Publish (upsert) | `POST` | `/assets` |
| Delete | `DELETE` | `/assets/{asset_id}` |

**Multipart shape on write (POST /assets):** exactly two parts. Part names are exact and case-sensitive:
- `metadata` — `application/json`, the serialised `CharacterAssetMetadata` (client-controlled fields only; ≤ 4 KiB). Never includes `character_id`.
- `payload` — `application/octet-stream`, the raw v1 `CharacterDefinition` JSON bytes (≤ 256 KiB by default). Both caps are pre-validated locally before the HTTP request is sent.

**The two most-likely-to-break wire details (both pinned by regression tests):**

1. **`X-Asset-Metadata` is RFC 4648 standard padded base64, NOT base64url.** Use `Convert.FromBase64String`, NOT `WebEncoders.Base64UrlDecode`. Standard base64 uses `+` and `/`; base64url uses `-` and `_`. An implementation using the wrong decoder will silently succeed on most payloads (characters that encode to only `A-Z`, `a-z`, `0-9`) and fail or corrupt on payloads that happen to produce `+` or `/` characters in the encoded form. The regression test uses byte sequence `0xFB 0xFF 0xBF` which encodes as `+/+/=` in standard base64 and `-_-_` in base64url, asserting that the `+/+/=` form round-trips cleanly and the `-_-_` form throws.

2. **Query parameter is `tag` (singular, repeatable), NOT `tags`.** The reference backend silently drops unknown query parameters rather than returning 422, so `?tags=foo` returns unfiltered results with no error signal. Regression test asserts `Assert.DoesNotContain("tags=", url)` on every built query URI.

**Boundary rename:** the wire uses `asset_id` everywhere; the POCO uses `CharacterId`. Direction matters:
- **Read** (`CharacterAssetMetadataParser`): wire `asset_id` → POCO `CharacterId`
- **Write** (`CharacterAssetMetadataSerializer`): POCO `CharacterId` → wire `asset_id`

## Error Mapping

| HTTP status + condition | Exception thrown |
|-------------------------|-----------------|
| `401` | `RemoteAssetAuthException` |
| `403` (reserved tag prefix or not-owner) | `RemoteAssetForbiddenException` |
| `422` — `error=invalid_cursor` on query | `RemoteAssetInvalidCursorException` |
| `422` — `code=metadata_too_large` | `RemoteAssetTooLargeException(subject="metadata")` |
| `422` — `code=payload_too_large` | `RemoteAssetTooLargeException(subject="payload")` |
| `422` — other | `RemoteAssetValidationException` |
| `429` — first occurrence | automatic retry after `Retry-After` (or `DefaultRetryAfter`) |
| `429` — second occurrence | `RemoteAssetRateLimitException` |
| `5xx` or unexpected | `RemoteAssetServerException` |
| `200` — missing or malformed `X-Asset-Metadata` | `RemoteAssetMalformedMetadataException` |
| Pre-flight cap exceeded (no HTTP call sent) | `RemoteAssetTooLargeException` |
| Network error (DNS, timeout, connection reset) | `HttpRequestException` (NOT wrapped) |

See [`docs/specs/character-asset-vocabulary.md`](../specs/character-asset-vocabulary.md) § Error handling for the server-side perspective.

## Architectural Invariants (Enforced by Tests and On-Merge Greps)

- **Zero `using Eigencore.*`** outside this assembly. A merge-time grep blocks any drift.
- **One-way dependency arrow.** `Pinder.Core` and `Pinder.SessionSetup` do not reference `Pinder.RemoteAssets`. The interface the engine calls (`IRemoteCharacterStore`) lives in `Pinder.Core`; the HTTP implementation lives here.
- **The wrapper does not enforce reserved-prefix rules.** It passes tags verbatim and surfaces 403 cleanly. The server is the authority.
- **`CharacterAssetMetadataSerializer` never emits `character_id`.** Pinned by regression test in `EigencoreCharacterStoreWriteTests`.
- **Query URI never contains `tags=`.** Pinned by regression test in `EigencoreCharacterStoreQueryTests`.

## What Is NOT in This Module

- **No caching layer.** Every call hits the wire; results are not stored locally.
- **No auto-pagination.** `QueryAsync` returns one page. The caller drives the cursor loop.
- **No live-staging integration tests.** Out of scope for #819; noted as a potential follow-up.
- **No streaming response handling.** The current contract caps payloads at 256 KiB; full body reads are fine at that scale.
- **No HEAD support.** `ExistsAsync` and `GetMetadataAsync` issue a full `GET` (v1 wire has no HEAD endpoint).

## Follow-Up Tickets Opened During the Sprint

- **#859 — HTTPS-scheme enforcement.** `Configuration` currently accepts any `Uri` for `BaseUrl`, including `http://`. Defence-in-depth: add a constructor guard that throws if the scheme is not `https` in non-test contexts, or at minimum emit a warning.
- **#860 — Response-size cap.** The current read/query paths do an unbounded `ReadAsByteArrayAsync`. Defence-in-depth: cap the response body read at a configurable limit to prevent a misbehaving server from causing an OOM.

## Source of Truth

The binding contract is [`docs/specs/character-asset-vocabulary.md`](../specs/character-asset-vocabulary.md). This module doc is the orientation; the spec is the contract. When the two disagree, the spec wins and this doc should be corrected.

## Change Log

| Date | Issue | Summary |
|------|-------|---------|
| 2026-05-13 | #819 (#856, #857, #858) | Initial creation — read path, query path, write path. Full `IRemoteCharacterStore` implementation against the eigencore character-assets API. |
