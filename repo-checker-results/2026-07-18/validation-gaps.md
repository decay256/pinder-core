# validation-gaps audit

Scope: full repositories. Primary `pinder-core` at `e96a75f4`; dependent `pinder-web` at `1a7acb38`.

Approved exception suppressions: none for validation-gaps. The listed raw exception and raw response leakage patterns were not raised as validation findings.

### Finding 1: Item catalog loader silently accepts malformed item entries
**File**: `pinder-core/src/Pinder.Core/Data/JsonItemRepository.cs:32`
**Issue**: The loader skips non-object entries with `if (!(element is JsonObject obj)) continue;`, then falls back through `itemId = obj.GetString("item_id")`, `displayName = obj.GetString("display_name")`, and `itemType = obj.GetString("item_type", "accessory")`. `ParseStatModifiers` only adds a modifier when the JSON value is numeric and `TryParseStatType` succeeds, so unknown stat keys and wrong-typed modifier values are dropped without error.
**Impact**: A malformed `starter_items.json` entry can become an empty-id/default-type item or lose its modifiers while startup continues. That can silently corrupt inventory balance and make content mistakes look like valid data.
**Urgency**: U1 - topic default; this is a production content loader accepting malformed schema and payload values at startup.
**Fixer-Agent Action Plan**: Replace permissive getters on required item fields with strict validation that rejects missing, empty, wrong-typed, and duplicate identifiers. Reject non-object array entries, unknown `stat_modifiers` keys, and wrong-typed modifier values with file/key context. Add loader tests for non-object entries, missing `id`/`item_id`, empty display names, unknown stat keys, and legacy `item_id` acceptance.

### Finding 2: Anatomy catalog loader skips and defaults malformed parameters and bands
**File**: `pinder-core/src/Pinder.Core/Data/JsonAnatomyRepository.cs:50`
**Issue**: The repository silently skips non-object array entries with `if (!(element is JsonObject obj)) continue;`. Required parameter fields are read as `id = obj.GetString("id")` and `name = obj.GetString("name")`, so missing or wrong-typed values become empty strings. Band parsing defaults malformed bounds through `lower = obj.GetFloat("lower", 0f)` and `upper = obj.GetFloat("upper", 1f)`, then only throws for missing `summary_text`.
**Impact**: Broken anatomy content can load with empty parameter IDs/names, skipped bands, or default `[0, 1]` ranges. Game mechanics and narrative text can then operate on malformed definitions without any startup failure.
**Urgency**: U1 - topic default; malformed catalog content is accepted silently instead of failing the load.
**Fixer-Agent Action Plan**: Validate top-level array entries as objects, require non-empty `id` and `name`, reject duplicate IDs, require numeric `lower`/`upper`, verify `lower < upper`, and reject unknown or wrong-typed stat modifiers. Add repository tests for skipped non-objects, empty IDs, missing band bounds, reversed ranges, and legacy-free valid catalogs.

### Finding 3: Timing profile loader defaults missing or wrong-typed fields
**File**: `pinder-core/src/Pinder.Core/Data/JsonTimingRepository.cs:26`
**Issue**: The constructor drops malformed top-level entries with `if (!(element is JsonObject obj)) continue;`. `ParseProfile` requires `id` and partially checks `baseDelayMinutes`, but `varianceMultiplier = obj.GetFloat("varianceMultiplier")`, `drySpellProbability = obj.GetFloat("drySpellProbability")`, and `readReceipt = obj.GetString("readReceipt", "neutral")` silently default missing or wrong-typed values.
**Impact**: Response timing configuration can degrade to zero variance/probability or a neutral read receipt without an error, changing pacing behavior while appearing to be a successful content load.
**Urgency**: U1 - topic default; timing payload validation gaps alter runtime behavior from malformed content.
**Fixer-Agent Action Plan**: Require object entries and strict numeric/string types for every timing field. Validate ranges such as non-negative delay, non-negative variance, and probability between 0 and 1. Add tests for wrong-typed floats, missing optional-looking fields, non-object entries, and explicit valid defaults if any are intentionally supported.

### Finding 4: Eigencore asset query response without `items` becomes an empty page
**File**: `pinder-core/src/Pinder.RemoteAssets/EigencoreCharacterStoreRead.cs:343`
**Issue**: `ParseQueryResponse` initializes `var items = new List<CharacterAssetRecord>();`, only parses records inside `if (root.TryGetProperty("items", out var itemsEl))`, and then returns `new CharacterAssetPage(items, nextCursor)`. A response body with no `items` property is accepted as a valid empty result instead of a malformed cross-service payload.
**Impact**: If Eigencore changes or breaks the response schema, Pinder can report that no remote character assets exist, hiding the contract failure and potentially causing operators or users to misdiagnose missing content.
**Urgency**: U1 - topic default; this is a cross-service boundary accepting malformed payloads silently.
**Fixer-Agent Action Plan**: Require an `items` property for query responses, require it to be an array, and fail with a typed store exception when absent or wrong-typed. Add tests for missing `items`, null `items`, non-array `items`, valid empty arrays, and malformed item entries.

### Finding 5: FastAPI session request DTOs ignore unexpected JSON fields
**File**: `pinder-web/src/pinder-backend/routes/schemas.py:90`
**Issue**: `SessionCreateRequest` and `TurnSubmitRequest` use `model_config = ConfigDict(alias_generator=to_camel, populate_by_name=True)` without `extra="forbid"`. Neighboring request models such as `UpdateSessionPlacementStateRequest`, `OperationRetryRequest`, and `LevelUpRequest` explicitly forbid extras. The handlers then forward `body.model_dump(exclude_none=True)` in `sessions.py` and `endpoints/session_turns.py`, stripping unexpected members without telling the caller.
**Impact**: Public callers can send misspelled or historic session fields and receive normal processing while those fields are silently dropped before the GameApi boundary. That masks client/server contract drift and makes payload mistakes difficult to detect.
**Urgency**: U1 - topic default; public request DTOs accept malformed payload shape silently.
**Fixer-Agent Action Plan**: Add `extra="forbid"` to `SessionCreateRequest` and `TurnSubmitRequest`, preserving aliases that are intentionally supported. Add FastAPI tests that submit an unknown create-session field, an unknown turn-submit field, and a known camelCase/snake_case alias to verify rejection and compatibility.

### Finding 6: GameApi JSON binder allows unmapped request members
**File**: `pinder-web/src/Pinder.GameApi/Program.cs:659`
**Issue**: Controllers are registered with `AddJsonOptions` for snake_case naming, enum conversion, and null omission, but no strict unmapped-member handling is configured. Request models such as `CreateSessionRequest` and `SubmitTurnRequest` also do not capture or reject extension data, so direct GameApi calls with unknown JSON members bind successfully.
**Impact**: Internal callers or future services can drift from the GameApi contract without a 400 response. Unknown session/turn fields are accepted and ignored at the service boundary, creating the same silent payload-loss class even when the FastAPI proxy is bypassed.
**Urgency**: U2 - de-escalated one level because GameApi is behind the internal shared-secret boundary and some public extras are stripped earlier, but the service contract remains silently permissive.
**Fixer-Agent Action Plan**: Enable strict unmapped-member rejection globally or on request DTOs, then explicitly opt out only for documented backward-compatible payloads. Add controller tests for unknown members on create-session and submit-turn requests, plus one regression proving intentional legacy aliases still work.

### Finding 7: Character update silently drops unknown allocation stat and shadow keys
**File**: `pinder-web/src/Pinder.GameApi/Controllers/CharactersController.cs:419`
**Issue**: The character update path converts allocation dictionaries with `if (Enum.TryParse<StatType>(kv.Key, true, out var statType)) spentDict[statType] = kv.Value;` and has no `else` branch. Shadow keys are handled the same way later in the method. The dependent frontend sends snake_case allocation keys such as `self_awareness` from `frontend/src/pages/CreationBench.tsx`, which do not match the `StatType` enum name through `Enum.TryParse`.
**Impact**: Character saves can silently lose user-visible stat allocations or shadow values. A typo, historic key, or current snake_case frontend key can persist a character with missing mechanics while the API still returns success.
**Urgency**: U1 - topic default; a write endpoint accepts malformed or mismatched payload keys and silently changes saved character state.
**Fixer-Agent Action Plan**: Replace raw `Enum.TryParse` with the same canonical stat-key parser used by strict character definition loading, including snake_case aliases where intended. Reject any unknown allocation or shadow key with a validation response and validate value ranges. Add controller tests for `self_awareness`, a bogus stat key, a bogus shadow key, and a successful canonical save.

### Finding 8: Replay payload mappers fabricate default mechanics from malformed audit rows
**File**: `pinder-web/src/Pinder.GameApi/Models/ReplayTurnResultMapper.cs:33`
**Issue**: `FromAuditPayload` intentionally returns a "best-effort DTO with safe defaults" when the root JSON parses, and helper reads such as `GetInt(..., fallback)` default missing or wrong-typed mechanics. `MapRoll` and related sections default stat names, totals, dice, booleans, and tiers. The owner replay mapper has the same pattern in `ReplayTurnResultMapper.Owner.cs`, including default owner roll fields.
**Impact**: Malformed or historic `turn_records.payload` JSON can render as option `0`, zero interest deltas, false/zero roll data, or fabricated success/failure state instead of being quarantined or explicitly marked invalid. Replay and owner-history consumers may trust mechanics that were never present in the stored payload.
**Urgency**: U2 - de-escalated one level because this affects replay/history projection rather than live turn resolution, but it is still a silent historic-payload validation gap.
**Fixer-Agent Action Plan**: Introduce a versioned replay payload DTO with required-field validation and explicit migrations for known historic formats. For invalid rows, return a typed invalid-replay response or omit mechanics with an error marker instead of fabricated defaults. Add mapper tests for missing roll fields, wrong-typed option indexes, historic payloads that should migrate, and invalid payloads that should be flagged.

Counts: U1=6, U2=2, U3=0, total=8.
Output hash confirmation: SHA-256 of report content above this line (UTF-8) = 3e8aa35c577d40fb9d723ee6905d71174acf01c4b583f2b8e2cff0dca3e3eca6; mirror file content must match exactly.
