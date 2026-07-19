> Scope: pinder-core files changed in `7962415f750de354b53d4f9b953eaa3e37b3575b..ae95e83cf40909337ccb0e9651b86bacae24972d` (104 files); pinder-web files changed in `c7a465bb67fb86ee6b5ab1105955b7c0717eddca..271b2230` (85 files). Topic 2 only: `doc-code-mismatches`. Inspected current HEAD; code is source of truth.

### Finding 1: Backend API Contract Omits Live Character Routes
**File**: `pinder-web/contracts/backend-api.yaml:309`
**Issue**: The contract documents only `GET /api/characters`, `GET /api/characters/{slug}`, and `POST /api/characters/{slug}/randomize` in the character section, but the changed FastAPI router exposes additional live routes: `@router.post("/api/characters/generate-random")`, `@router.post("/api/characters/{slug}/refresh-prompts")`, `@router.get("/api/models")`, `@router.put("/api/characters/{slug}")`, `@router.delete("/api/characters/{slug}")`, `@router.get("/api/items")`, and `@router.get("/api/anatomy")` in `pinder-web/src/pinder-backend/routes/characters.py`.
**Impact**: OpenAPI consumers and contract tests cannot discover or generate clients for changed public endpoints, even though the backend and frontend call them.
**Urgency**: U2 - escalated from U3 because this is the public API contract for changed, user-facing routes.
**Fixer-Agent Action Plan**: Add the missing path entries, request bodies, response schemas, auth/admin notes, and error responses to `contracts/backend-api.yaml`; add or extend `src/pinder-backend/tests/test_openapi.py` so these routes stay documented.

### Finding 2: Character Detail Contract Says Verbatim, Code Redacts Sensitive Fields
**File**: `pinder-web/contracts/backend-api.yaml:334`
**Issue**: The contract for `GET /api/characters/{slug}` says `200 returns the upstream body verbatim` and declares `CharacterSheetDto`, but `pinder-web/src/pinder-backend/routes/characters.py:100` says normal users do not receive sensitive narrative synthesis fields, and `_redact_sensitive_character_detail` removes `background_story`, `backstory_categories`, `psychological_stake`, and `psychiatric_diagnosis`.
**Impact**: Clients generated from the contract may depend on fields that are intentionally absent for non-admin users, causing broken UI assumptions or incorrect data-shape validation.
**Urgency**: U2 - escalated from U3 because this existing public endpoint has materially different response shapes by caller role.
**Fixer-Agent Action Plan**: Update the endpoint description and schema to describe admin vs non-admin projections, either with separate schemas or `oneOf`; remove the `verbatim` claim and cover the redaction behavior in OpenAPI tests.

### Finding 3: GameApi README Gives Stale Character File Defaults
**File**: `pinder-web/src/Pinder.GameApi/README.md:58`
**Issue**: The README says `CHARACTER_FILES_PATH` defaults to `<build-output>/Data/Characters` and later says character definitions are loaded from `Data/Characters/*.json` relative to `AppContext.BaseDirectory`. Current startup code resolves `PINDER_DATA_DIR`/`DATA_ROOT_PATH` first and then prefers `<dataRootPath>/characters` or `<dataRootPath>/Characters` before falling back to `<BaseDir>/Data/Characters` in `pinder-web/src/Pinder.GameApi/Program.cs:89`.
**Impact**: Operators following the README can inspect or mount the wrong directory and miss that the standard configured data root is the first lookup path.
**Urgency**: U3 - topic default.
**Fixer-Agent Action Plan**: Rewrite the README table and Character Files section to mirror `ResolveCharacterFilesPath`: explicit override first, configured data root `characters`/`Characters` second, runtime bundle last.

### Finding 4: Issue 415 Spec Still Documents Removed Prompt-File Fallback
**File**: `pinder-core/docs/specs/issue-415-spec.md:193`
**Issue**: The spec says missing `data/characters/{name}.json` should `fall back to CharacterLoader.Load(...)`, and line 288 says missing item/anatomy data should warn and fall back. Current `session-runner/Program.CharacterLoader.cs:29` says `#840 removed the prompt-file fallback`, throws `FileNotFoundException` for missing character JSON, and `EnsureReposLoaded` throws for missing item/anatomy/timing data.
**Impact**: A fixer or agent using the spec will reintroduce a deliberately removed stale prompt-file path instead of preserving the current fail-fast loader behavior.
**Urgency**: U3 - topic default.
**Fixer-Agent Action Plan**: Update Example 2, AC6, edge cases, and error-condition rows to state that `--player`/`--datee` resolve through `DirectoryCharacterStore` only and missing data is a hard setup error.

### Finding 5: Issue 415 Spec Describes Prototype Character JSON, Not Current Schema
**File**: `pinder-core/docs/specs/issue-415-spec.md:142`
**Issue**: The required-fields section still lists top-level `build_points` and optional top-level `shadows`, while `CharacterDefinitionLoader.ParseDefinition` requires `schema_version`, `character_id`, and an `allocation` block, with `allocation.shadows` required. The same doc partly contradicts itself at line 276 by saying missing `shadows` is invalid for schema v2.
**Impact**: Schema edits based on this spec can produce character JSON that fails current loader validation, especially around `allocation.spent` and `allocation.shadows`.
**Urgency**: U3 - topic default.
**Fixer-Agent Action Plan**: Replace the example JSON and schema prose with the current v2 layout, including `schema_version`, `character_id`, `allocation.spent`, `allocation.unspent_pool`, and required `allocation.shadows`; remove the obsolete top-level `build_points`/`shadows` mapping.

### Finding 6: CharacterDefinitionLoader XML Comments Still Say v1
**File**: `pinder-core/src/Pinder.SessionSetup/CharacterDefinitionLoader.cs:16`
**Issue**: The class comment says `Loads a v1 character definition JSON file` and `v1 contract`, but the same class sets `SupportedSchemaVersion = CharacterDefinition.CurrentSchemaVersion` with the summary `v2 as of #1175`, and `ParseSchemaVersion` rejects anything except the current supported version.
**Impact**: Public API documentation for the loader contradicts the enforced schema version, which misleads callers and test authors around migration/back-compat behavior.
**Urgency**: U3 - topic default.
**Fixer-Agent Action Plan**: Update the XML comments to say v2/current schema, align the `ParseDefinition` exception docs, and remove the stale `No back-compat with the prototype format` wording if it no longer clarifies the current contract.

### Finding 7: Conversation Module Docs List Removed Read/Recover APIs
**File**: `pinder-core/docs/modules/conversation.md:16`
**Issue**: The module table and public interface sections document `ReadResult.cs`, `RecoverResult.cs`, `ReadAsync()`, and `RecoverAsync()`, but `src/Pinder.Core/Conversation` contains no `ReadResult.cs` or `RecoverResult.cs`, and `GameSession.Turns.cs` exposes `StartTurnAsync`, `ResolveTurnAsync`, `EnsureAllDicePoolsFilled`, `InjectNextDicePool`, and `Wait()` only.
**Impact**: Developers looking for supported standalone Read/Recover actions will chase APIs that do not exist and may design new features against a retired turn model.
**Urgency**: U3 - topic default.
**Fixer-Agent Action Plan**: Remove Read/Recover entries from the component table, API section, turn-flow notes, and change log; replace them with the current Start/Resolve/Wait plus dice-pool injection APIs if those are intended public surfaces.

### Finding 8: LLM Adapter Docs Describe Old Stateful Interface
**File**: `pinder-core/docs/modules/llm-adapters.md:73`
**Issue**: The docs show `IStatefulLlmAdapter` with `StartConversation(string)` and `HasActiveConversation`, and say it is implemented by `AnthropicLlmAdapter`. Current `IStatefulLlmAdapter` instead defines history-passing methods such as `GetDateeResponseAsync(DateeContext, IReadOnlyList<ConversationMessage>, CancellationToken)`, plus steering/horniness/improvement methods, and `PinderLlmAdapter` implements it; there is no `AnthropicLlmAdapter.cs` production file.
**Impact**: The module guide teaches the opposite ownership model from current code: adapter-held conversation state instead of engine-owned history passed per call.
**Urgency**: U3 - topic default.
**Fixer-Agent Action Plan**: Rewrite the stateful adapter section around the pure-stateless/history-passing interface, remove `StartConversation`/`HasActiveConversation`, and update implementation names to `PinderLlmAdapter` plus low-level transports.

### Finding 9: Prompt Map Omits Current Prompt Catalog Files
**File**: `pinder-core/docs/prompts.md:58`
**Issue**: The document calls itself the authoritative map of every prompt template, but its `Current SSOT` file layout lists only `templates.yaml`, `archetypes.yaml`, `structural.yaml`, `narrative.yaml`, `overlay-model-comparison.yaml`, and `stake.yaml`. Current `data/prompts/` also contains `backstory.yaml`, `backstory_consolidation.yaml`, `bio.yaml`, `diagnosis.yaml`, `dramatic_arc.yaml`, `outfit.yaml`, `personality_consolidation.yaml`, and `sim_agent.yaml`.
**Impact**: Prompt operators and admin-editor work may miss active prompt files that the catalog loads and the setup/synthesis code uses.
**Urgency**: U3 - topic default.
**Fixer-Agent Action Plan**: Update the file layout and prompt map to enumerate all current catalog YAML files and briefly identify which generator or adapter consumes each prompt group.
