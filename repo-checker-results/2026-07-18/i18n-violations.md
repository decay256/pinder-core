> Scope: full multi-repo audit at current commits: pinder-core `e96a75f4`, pinder-web `1a7acb38`.

Inspected React frontend i18n catalog usage and ESLint guardrail output, FastAPI public error payloads, GameApi controllers/services returning `ErrorResponse` or JSON errors, pinder-core engine display helpers, and the pinder-core session-runner CLI transcript surfaces. The frontend i18n-only ESLint filter reports 92 current `i18n/no-literal-string` violations: 1 in `CharacterCard.tsx`, 81 in `CreationBench.tsx`, and 10 in `ItemForm.tsx`.

Counts: U1 0, U2 0, U3 9. Suppressions: 2 approved would-be U1 patterns noted below. Outputs: this file is intended to be byte-identical to `A:\Data\Obsidian\Eigen\Pinder Design\Audit\2026-07-18\i18n-violations.md`.

### Finding 1: Character card edit CTA bypasses the frontend i18n catalog
**File**: `pinder-web/frontend/src/components/CharacterCard.tsx:235`
**Issue**: The production character selection card imports `t` and already localizes sibling CTAs, but the edit action still renders a literal JSX text node: `<button ...> Edit </button>`. The existing guardrail flags this exact line as `i18n/no-literal-string`.
**Impact**: The `/play` character grid can show an untranslated English CTA in the normal authenticated player flow, and the otherwise typed `StringKey` catalog cannot verify or change it with the rest of the UI copy.
**Urgency**: U3 - topic default; single visible CTA with low behavioral risk.
**Fixer-Agent Action Plan**: Add a `character_card.edit_cta` string to the appropriate i18n YAML, regenerate `frontend/src/i18n/generated/en.ts`, replace `Edit` with `t('character_card.edit_cta')`, and rerun the i18n-only ESLint filter for `CharacterCard.tsx`.

### Finding 2: Creation Bench hardcodes the authenticated character authoring UI
**File**: `pinder-web/frontend/src/pages/CreationBench.tsx:658`
**Issue**: `CreationBench.tsx` has no `t` import and renders dozens of literal UI strings, including `{editSlug ? 'Creation Bench: Edit Character' : 'Creation Bench: Create Character'}`, `"Configure character traits, stats, look, and anatomy."`, `"Smart Randomize"`, `"Back to Select"`, `"Character Name"`, and `"Save Character"`. The i18n-only ESLint pass reports 81 violations in this file.
**Impact**: The full character create/edit workflow remains English-only and outside the documented catalog despite being mounted under `/creation-bench` inside `AuthGate` and reachable from `/play`.
**Urgency**: U3 - topic default; broad UI hygiene issue on an authenticated authoring surface, not a correctness failure.
**Fixer-Agent Action Plan**: Add a `creation_bench.*` namespace under the frontend-local i18n YAML, import `t` and replace static headings, buttons, placeholders, empty states, and tab labels with typed keys, then rerun `npx eslint . --format json` filtered to `i18n/no-literal-string` and confirm this file reports zero violations.

### Finding 3: ItemForm visual association labels are unlocalized and unannotated
**File**: `pinder-web/frontend/src/pages/ItemForm.tsx:201`
**Issue**: `ItemForm.tsx` imports `t`, and nearby schema labels use `I18N-OK`, but the visual association `TextField` labels are raw strings with no catalog key or exception annotation: `label="visual_asset_path"`, `label="visual_asset_sha256"`, `label="visual_asset_type"`, through `label="visual_material_type"`. The i18n-only ESLint pass reports 10 violations in this block.
**Impact**: The admin item editor mixes localized admin UI with literal schema labels, so future locale work cannot distinguish intentional schema identifiers from missed translation work.
**Urgency**: U3 - topic default; admin/editor-only surface, but it violates the repo's own "use t() or I18N-OK" convention.
**Fixer-Agent Action Plan**: Either add `admin.items.visual_associations.*` keys and pass localized labels, or annotate each intentionally verbatim schema identifier with `I18N-OK` immediately above the prop; rerun the i18n-only ESLint filter for `ItemForm.tsx`.

### Finding 4: FastAPI proxy errors return literal English details instead of stable localized error keys
**File**: `pinder-web/src/pinder-backend/session_services.py:184`
**Issue**: The FastAPI GameApi proxy raises public `HTTPException` payloads with constructed English details, e.g. `detail=f"GameApi returned {exc.response.status_code}"` and `detail=f"GameApi unreachable: {exc}"`, which the exception handler serializes as `{"error": ...}`.
**Impact**: Any frontend path that falls back to the backend `error` string displays proxy-layer English that is not in the frontend/core i18n catalog and cannot be translated or consistently mapped by clients.
**Urgency**: U3 - topic default; user-facing API copy is hardcoded, while raw exception leakage risk is covered by approved exceptions and not raised here.
**Fixer-Agent Action Plan**: Replace these literals with stable error codes plus optional interpolation data, add frontend `errors.*` catalog entries for display copy, and update tests to assert codes rather than exact English text.

### Finding 5: FastAPI ownership and share endpoints duplicate hardcoded not-found/forbidden copy
**File**: `pinder-web/src/pinder-backend/session_services.py:280`
**Issue**: `SessionOwnershipService.assert_owner` and share helpers raise literal public details such as `detail="Forbidden"`, `detail="Session not found"`, and `detail="Share link not found"` at lines 280, 284, 286, 912, 933, and 955.
**Impact**: Session/share errors bypass the existing `pinder-core/data/i18n/en/errors.yaml` and frontend `errors.yaml`, forcing every consumer to treat English strings as the API contract.
**Urgency**: U3 - topic default; repeated user-visible API hygiene issue.
**Fixer-Agent Action Plan**: Introduce canonical error codes such as `session_not_found`, `session_forbidden`, and `share_link_not_found`, return those codes from FastAPI, map them to localized frontend strings, and adjust route tests to assert the code contract.

### Finding 6: GameApi session turn endpoints hardcode player-visible failure messages
**File**: `pinder-web/src/Pinder.GameApi/Controllers/SessionsController.Actions.cs:106`
**Issue**: Public session action endpoints return literal `ErrorResponse` strings: `"Turn generation is still in progress."`, `"Turn generation timed out. Please retry."`, `"Turn generation provider error. Please retry."`, `"Turn generation provider is temporarily unavailable. Please retry."`, `"option_index is out of range."`, and `"Session has ended."`.
**Impact**: The SPA receives English prose from GameApi for core gameplay failures instead of stable error keys it can localize through the existing frontend/core catalogs.
**Urgency**: U3 - topic default; high-traffic gameplay surface, but behavior is otherwise explicit and visible.
**Fixer-Agent Action Plan**: Define GameApi error codes and display keys for session-turn failures, return code/data fields from `ErrorResponse`, localize the display text in `errors.yaml`, and update GameApi plus frontend tests to consume codes.

### Finding 7: GameApi character endpoints hardcode validation and not-found responses
**File**: `pinder-web/src/Pinder.GameApi/Controllers/CharactersController.cs:91`
**Issue**: Character endpoints return literal English errors such as `$"Character '{idOrSlug}' not found."`, `"An unexpected error occurred."`, `"Payload cannot be null."`, `"Name is required."`, and `"Gender identity is required."`.
**Impact**: Character sheet and creation/edit failures escape the localization pipeline, making raw English response text part of the client-facing API behavior.
**Urgency**: U3 - topic default; user-facing API copy is hardcoded, but the failures are visible rather than silent.
**Fixer-Agent Action Plan**: Add structured character error codes and localized frontend mappings, keep raw identifiers as interpolation data, and update controller tests to assert code/data shape instead of literal English.

### Finding 8: Core trap penalty descriptions are formatted as literal English before reaching UI DTOs
**File**: `pinder-core/src/Pinder.Core/Conversation/GameSessionHelpers.cs:84`
**Issue**: `FormatTrapPenalty` is explicitly documented as "for display" and returns hardcoded strings like `"stat penalty -{def.EffectValue}"`, `"roll at disadvantage"`, and `"datee DC +{def.EffectValue}"`; those values are assigned to `TrapDetail.PenaltyDescription` at line 161.
**Impact**: Trap details surfaced through game state/replay DTOs carry English display text from core instead of consequence/catalog keys, so clients cannot localize trap penalties consistently with the rest of turn-event copy.
**Urgency**: U3 - topic default; localized display gap in a core DTO, not a correctness issue.
**Fixer-Agent Action Plan**: Add trap penalty string keys to the core/frontend i18n catalogs, have core emit a key plus numeric slots or resolve through `IConsequenceCatalog`, and verify `TrapDetail`/frontend rendering still shows the same English in the default locale.

### Finding 9: Session-runner CLI transcript bypasses the engine i18n catalog
**File**: `pinder-core/session-runner/Program.Setup.cs:162`
**Issue**: The session-runner prints Markdown transcript text directly with literals such as `"# Playtest Session ..."`, `"**Date:**"`, `"**Player:**"`, `"## Session State"`, `"Active Traps: none"`, and additional hardcoded headings/tables in `Program.PrintSetup.cs` and `Program.Loop.Helpers.cs`.
**Impact**: CLI/playtest transcript output is English-only even though the engine has `I18nCatalog` for shared event/consequence text, so simulator output cannot participate in locale or copy changes.
**Urgency**: U3 - topic default; CLI/reporting surface only.
**Fixer-Agent Action Plan**: Add a session-runner/reporting text catalog or reuse `I18nCatalog` with a runner-specific namespace, replace transcript headings/status labels with catalog lookups, and snapshot-test the default English transcript shape.

Further U3 instances exist in: `pinder-web/frontend/src/pages/CreationBench.tsx`, `pinder-web/frontend/src/pages/ItemForm.tsx`, `pinder-web/src/pinder-backend/session_services.py`, `pinder-web/src/Pinder.GameApi/Controllers/SessionsController.Creation.cs`, `pinder-web/src/Pinder.GameApi/Controllers/SessionsController.Queries.cs`, `pinder-web/src/Pinder.GameApi/Controllers/MediaController.cs`, `pinder-core/session-runner/Program.PrintSetup.cs`, `pinder-core/session-runner/Program.Loop.Helpers.cs`.

Suppressed by exception: Unsafe Leakage of GameApi Raw Exception/HTML Content (resp.text) to public-facing HTTP responses - would be U1.
Suppressed by exception: Unsafe Leakage of Raw C# Exception Messages (ex.Message) in public GameApi endpoints - would be U1.
