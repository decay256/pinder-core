> Resolution: current #1338/#1339 sprint changed files.

## Finding 1

Resolved as an intentional compatibility boundary, with the migration path
covered by regression tests.

Character schema version 2 remains the structural on-disk format. The expanded
therapist diagnosis is synthesis completeness, not a new serialized shape: it
is still the same flat string map. Legacy two-field maps therefore remain
loadable so the admin synthesis flow can repair them. DATEE session creation
continues to reject incomplete synthesis with an actionable validation error
instead of silently inventing reaction behavior.

Diagnosis-stage regeneration is the existing migration mechanism. It generates
the complete canonical map, validates every required field, regenerates the
dependent biography, and applies the diagnosis and biography in one repository
patch. The Web regression
`SynthesizeAndPatchFromStageAsync_Diagnosis_ReplacesLegacyTwoFieldMapAtomically`
proves that a legacy two-field map is replaced by all eleven fields in one
patch. Invalid generated formulation exhausts the existing recovery policy and
never patches either field.

The integrated delivery order remains Core first, followed by the Web submodule
pointer, so Web cannot be delivered against the pre-contract Core revision.

Verification:

- `CharacterSynthesisServiceTests`: 14 passed, 0 failed.
- Focused Core contract, loader, writer, schema, and bundled-character tests:
  52 passed, 0 failed.
