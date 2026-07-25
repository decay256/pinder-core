> Scope: current #1338/#1339 working-tree changes only: 29 modified/untracked files in `pinder-core` (including the six bundled character JSON files; audit reports excluded) plus `pinder-web/src/Pinder.GameApi.Tests/Services/CharacterSynthesisServiceTests.cs` (30 files total).

### Finding 1: Nine mandatory diagnosis fields were added without a character schema migration
**File**: `data/characters/character-schema.json:87`
**Issue**: The v2 schema still declares `"$id": ".../character-v2.json"` and `"schema_version": { "const": 2 }`, but #1338 changes `psychiatric_diagnosis.required` from the previously valid two-field shape (`derived_feeling`, `defense_reaction`) to eleven mandatory fields. The six bundled v2 files were updated in place, but no v2-to-v3 migration, compatibility reader, or repository upgrade path was added for already-persisted v2 characters. `CharacterDefinitionLoader.ParseOptionalPsychiatricDiagnosis` still accepts the old flat map and `CharacterDefinition.CurrentSchemaVersion` remains 2, so an old character loads successfully and is only rejected later when `TherapistDiagnosisContract.ValidateRequiredFields` runs during DATEE session creation or bio regeneration.
**Impact**: Every user-created or externally persisted v2 character synthesized before #1338 can pass repository loading and API reads yet fail with HTTP 422 when selected as the DATEE; bio-only regeneration also fails until the user knows to regenerate from the diagnosis stage. Because old and new incompatible shapes share schema version 2, callers and stored snapshots containing only character slugs cannot determine whether an upgrade is required from the version field.
**Urgency**: U1 - topic default; this is an unversioned persisted-contract break that makes previously valid production characters unusable on a normal session-creation path.
**Fixer-Agent Action Plan**: Introduce an explicit character-schema migration boundary. Either bump the on-disk schema to v3 and implement a v2 reader/upgrader, or keep the new fields optional in v2 and represent synthesis completeness separately until diagnosis regeneration atomically persists the eleven-field form. Add a regression fixture containing the pre-#1338 two-field v2 diagnosis and cover repository load, session creation, diagnosis/bio regeneration, writer round-trip, and API persistence. After committing Core `0.2.18`, advance the `pinder-web` submodule gitlink so the dependent API deploy cannot remain pinned to the pre-migration Core commit.

## Verified No Additional Findings

All six bundled characters contain exactly the eleven runtime-required diagnosis fields. The scoped parity tests keep the schema, prompt object, and `TherapistDiagnosisContract.RequiredFields` in identical order; writer and GameApi synthesis-patch paths retain the expanded flat map; focused Core tests passed 29/29 and focused GameApi regeneration/persistence tests passed 14/14. Session snapshots persist character slugs rather than a diagnosis schema/version, so they provide no independent migration mechanism but introduce no second scoped persistence defect.

`Directory.Build.props` correctly bumps Core to `0.2.18`. The parent `pinder-web` gitlink is still `218b9c830fd360c995f003c99269f92518c41f5b` and currently reports only a dirty submodule; this is expected before the Core commit, but integrated delivery requires the parent pointer to be advanced afterward.

## Counts

U1: 1
U2: 0
U3: 0
