# Sprint 2026-05-21-06c60a — Lessons learned

## L1 — BASH-GLOBAL-LOCAL-ERROR (stable)

- **Symptom:** During the implementation of the database backfill script `scripts/backfill-session-outcomes.sh` (PR `pinder-web#683`), a subagent declared a variable with the `local` keyword at global scope (line 101), which was caught and blocked during code review.
- **Root cause:** The `local` keyword in Bash is only valid within function bodies. Using it at the script's top-level is a syntax error in many shells (or behaves unexpectedly).
- **Fix / mitigation actually applied:** Removed the `local` keyword at the global scope level, simply assigning the variable directly (`remaining=$(...)`).
- **Rule:** Never use the `local` keyword at the global/script scope in Bash scripts. Restrict `local` strictly to variables declared inside function definitions.

## L2 — CASE-SENSITIVE-JSON-CHECK (stable)

- **Symptom:** The data-drift check script `scripts/check-data-drift.sh` (PR `pinder-web#679`) originally skipped character data files, flagging them as `(not bundled)`, because it searched for a lowercase folder `characters/` whereas C# code expected `Characters/`.
- **Root cause:** Case-sensitive filesystems treat `characters/` and `Characters/` as completely separate paths. Mismatched case between C# assets/endpoints and database/data directories causes data checks to silently fail or skip crucial bundled assets.
- **Fix / mitigation actually applied:** Modified `scripts/check-data-drift.sh` to correctly map `characters/` to `Characters/` (and vice-versa where case differences existed) so that all bundled characters are verified on case-sensitive platforms.
- **Rule:** Always handle case mismatches between data folders and C# compilation/asset structures explicitly. Ensure all data-drift and content gates treat paths case-sensitively and include test coverage for both lowercase/uppercase folder mappings.
