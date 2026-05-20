You are a backend engineer subagent in the pinder-core repo.

## Workspace setup (do this FIRST)

```bash
cd /root/projects/pinder-core
git fetch origin
git worktree add /tmp/work-964 origin/main
cd /tmp/work-964
git checkout -b chore/964-consequence-wire-field
```

All edits in `/tmp/work-964` only.

## Role spec

Read `~/.openclaw/skills/eigentakt/agents/backend-engineer.md` for DoD-Evidence + Research-Log discipline.

## Lessons / canonical hazards

Apply: WORKSPACE-ISOLATION, AGENT-LOG-EVERYTHING, DOCS-FOLLOW-CODE, NO-SCOPE-CREEP, REGRESSION-TESTS-ON-BUGS, APPROVED-WORK-IS-IMMUTABLE. Project lessons in `/root/projects/pinder-core/LESSONS_LEARNED.md`. Canonical bodies in `~/.openclaw/skills/eigentakt/references/canonical-lessons.md`.

## AGENTS.md

Read `/root/projects/pinder-core/AGENTS.md`. 6-stat system (`Charm`, `Rizz`, `Honesty`, `Chaos`, `Wit`, `SelfAwareness`).

## Ticket #964 — Surface `consequence` on RollCheckResult / ShadowCheckResult / HornyCheckResult wire DTOs

### Orchestrator-refined scope (read carefully — narrower than the issue body)

The original issue body asks for engine-side consequence text *populated* from a yaml catalogue. That's a larger feature (i18n loader, slot substitution, locale handling). **This ticket is the wire-schema half only.**

**In scope:**
1. Add a nullable `Consequence` property (`string?`, default `null`) with `[JsonPropertyName("consequence")]` on these three classes:
   - `src/Pinder.Core/Rolls/RollCheckResult.cs` (class `RollCheckResult`)
   - `src/Pinder.Core/Conversation/ShadowCheckResult.cs` (class `ShadowCheckResult`)
   - `src/Pinder.Core/Conversation/HorninessCheckResult.cs` (class `HorninessCheckResult`)
2. Property must be settable post-construction via a single `ApplyConsequence(string)` method per class (mirror the `ApplyFinalOverride` pattern in `RollCheckResult.cs` lines ~64–86). Idempotent guard — if already set, throw `InvalidOperationException("Consequence already applied")`.
3. Wire DTO serialises as `"consequence": null` when unset and `"consequence": "<string>"` when set.
4. Update the XML doc comment on each new property to point at #964 and note: "Population deferred to follow-up. SPA falls back to client-side i18n catalogue when null."

**Out of scope (file a follow-up issue and link it in the PR body):**
- The actual engine-side population (loading `data/i18n/en/consequences.yaml`, slot substitution, calling `ApplyConsequence` from `RollEngine`, `ShadowCheckEngine`, `HorninessCheckEngine`).
- `TropeTrapResult`: this class does not exist. `TropeTrap` is a `FailureTier`, captured on `RollCheckResult` when `Tier == FailureTier.TropeTrap`. The `Consequence` field on `RollCheckResult` covers the TropeTrap case. **Note this explicitly in the PR body's "Out of scope" section.**

### Acceptance criteria
- Three properties added, idempotent setters, XML docs in place.
- `dotnet build` clean.
- Existing tests still pass.
- Add ONE small unit test per class verifying: default is null, `ApplyConsequence("foo")` sets it, second call throws. (Three test methods total — colocate in the existing `RollCheckResultTests`, `ShadowCheckResultTests`, `HorninessCheckResultTests` test files if present; otherwise create minimal new test files in `tests/Pinder.Core.Tests/`.)
- Snake_case `consequence` key visible in JSON output (verify with one snapshot or roundtrip test if a snapshot harness already exists; otherwise skip and note).
- File a follow-up issue titled `[#964 follow-up] Engine-side population of RollCheckResult/ShadowCheckResult/HornyCheckResult.Consequence from i18n catalogue` and link its number in the PR body.

### PR
- Branch: `chore/964-consequence-wire-field`.
- Title: `chore(#964): nullable consequence wire field on RollCheckResult / ShadowCheckResult / HorninessCheckResult`.
- Body: `Closes #964` (mark the original issue closed — the wire half is the deliverable; the population follow-up is a separate issue), then one-paragraph summary, then "Out of scope" section naming the follow-up issue number and the missing `TropeTrapResult` class. End with `## DoD Evidence` (build + test output) and `## Research Log`.
- Open as non-draft.

### Authority
Authorized for sprint `2026-05-20-eventbox`. Self-unblock per SELF-UNBLOCK-BY-DEFAULT. Do not merge.

Return when PR is open with green DoD blocks and the follow-up issue is filed.
