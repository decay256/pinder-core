# Lessons — sprint 2026-05-17-f57876 (cleanup drain)

_Final. Captured during the run._

_Scope: all four lessons below are about eigentakt / orchestrator behaviour, not pinder-{core,web} game/architecture. They are intentionally NOT promoted to the project LESSONS_LEARNED.md files — see `~/.openclaw/agents-extra/pinder/AGENTS.md` for where this kind of lesson lives in this org._

## L1 — Verify the implementer's "stop and report on >1 file" decisions against the co-located-mirror exception

**Observation:** during web#624 (exhaustiveness guard + ASCII minus reconciliation), the implementer correctly identified that the `collapsedHeader.test.ts` file needed 4-line updates to match the source change (assertions hardcoded the exact U+2212 character literal). The implementer hit a tension between two rules:

1. The "stage only the one file" / "stop and report on >1 file" discipline (EIGENTAKT-IMPLEMENTER-EXPLICIT-PATHSPECS family).
2. The implicit "co-located mirror edits to a test file are part of the source change" convention.

The implementer chose the conservative path (stop and report). This was correct behaviour, but it cost a ~2-minute extra orchestrator round-trip to confirm and respawn a finalize step.

**Failure mode:** the discipline rules don't carve out the co-located-mirror case. The implementer can't tell from the rules alone whether a test file modified by the source edit is a drive-by (forbidden) or a mirror edit (in-scope and expected).

**Fix:** future implementer task templates for changes to component .ts/.tsx files should explicitly authorize updates to the matching co-located `.test.ts` / `.test.tsx` file IF AND ONLY IF (a) the test assertions hardcode literal strings that the source change reconciles, AND (b) the test edits are mechanical mirrors of the source edit (no logic changes, no added/removed tests). Same commit, called out under a `## Mirror test updates` heading in the PR's Research Log.

**Class:** prompt-template ambiguity. Easy fix; biggest payoff per word added.

**Lesson id:** EIGENTAKT-COLOCATED-MIRROR-TESTS-IN-SCOPE

## L2 — Verify ticket prescriptions against the actual codebase before spawning

**Observation:** three tickets in this sprint had prescriptions that didn't match the codebase as-is:

- **core#915** prescribed `sealed record` for `OpponentDefenseSnapshot` / `OpponentDefenseEntry`. The codebase is `LangVersion=8.0` (records require ≥9.0) and has an explicit `ProjectStructureTests.DtoTypes_AreSealed` `[Theory]` enforcing `sealed class` for DTOs. The ticket was incompatible with codebase policy.
- **web#614** prescribed ``t(`modifier.${key}`, undefined, { fallback: capitalize(key) })``. The actual `t()` signature in `useText.ts` has no third options arg and `StringKey` is a compile-time-checked literal union — dynamic template-string indexing is a tsc error.
- **web#606** prescribed a DOM render test via `@testing-library/react`. The repo is jsdom-free by design and does not ship `@testing-library/react` (multiple test files explicitly call this out).

In all three cases, the implementer either correctly stopped to report (core#915 → wontfix close) or the orchestrator pre-empted the friction by authoring an alternative approach in the spawn prompt (web#614 typed-StringKey dispatch table; web#606 exported-class-name constant assertion). Net cost: zero — these were all caught by the implementer pre-analysis or the orchestrator pre-spawn read-through.

**Failure mode if uncaught:** the implementer would have either pushed broken code or wasted cycles fighting the ticket prescription before realizing it was infeasible.

**Fix:** when reading a ticket body before spawning the implementer, the orchestrator MUST sanity-check the prescription's three load-bearing premises (always):
1. Does the file path it names actually exist? (cheap `ls` check)
2. Does the API/signature it prescribes match what's actually there? (cheap `grep` check)
3. Does the codebase's policy/test infra accept the prescribed approach? (e.g. LangVersion, jsdom-free convention)

When the prescription is wrong, the orchestrator either re-authors the approach in the spawn prompt (with explicit "ticket spec is infeasible because X, here's the orchestrator-approved alternative") OR closes the ticket as wontfix with the evidence.

**Class:** orchestrator pre-spawn diligence. The pattern is "skim the body, look it up, then write the prompt" — not "paste the body into the prompt."

**Lesson id:** EIGENTAKT-VERIFY-TICKET-AGAINST-CODEBASE

## L3 — Required-ification follow-through is in scope

**Observation:** during web#631 (drop deprecated `SteeringRoll.modifier`/`.dc` aliases), the planned 6-file diff expanded to 10 files because promoting the new wire fields from optional (`?:`) to required exposed 4 callers (3 TS stub constructors + 1 C# assertion test) that had been silently relying on the legacy fields or omitting the new ones.

The implementer made the "fix the callers" call inline (rather than backing out the required-ification or stopping to ask), and it was the right call. The spawn prompt had explicitly authorized this: *"if tsc complains about ... missing required fields, that's a real surface — investigate, find the caller, decide whether to update it (in scope) or revert the required-ification."*

**Failure mode if the prompt hadn't authorized this:** the implementer might have either reverted the required-ification (losing the type-system invariant we want post-cleanup) or stopped to ask, padding the wall time with another round-trip.

**Fix:** generalize this authorization pattern. When a cleanup PR tightens a type (drops `?:`, narrows a union, makes a method abstract, etc.), the spawn prompt should always include a pre-authorized "fix callers in scope" clause. The exception is when the caller fix is large enough to be its own ticket — but for stub/test/placeholder callers (which is the typical case for deprecation cleanup), inline is correct.

**Class:** prompt-template pattern. Apply to all "drop deprecated X" / "tighten Y to required" tickets.

**Lesson id:** EIGENTAKT-CLEANUP-CALLER-FIXES-INLINE

## L4 — Sediment in canonical clones must be sniffed at Phase 0, even when the kickoff §Sediment check declared clean

**Observation:** the kickoff's §Sediment check claimed all three local clones were clean. They weren't: `/root/projects/pinder-web` had 17 files of uncommitted WIP (a mix of staged adds + unstaged modifications) representing intermediate sprint-d1d40c state that had been superseded on origin/main by different commits. The canonical clone's local main was also 5 commits behind origin (the d1d40c final-merge train).

The orchestrator caught this at Phase 0 because the `git status` check was performed verbatim regardless of the kickoff's claim. Recovered with a safety stash + `git reset --hard origin/main` + `git submodule update --init pinder-core`. Net cost: ~3 minutes.

**Failure mode if uncaught:** the first implementer worktree would have inherited the contaminated state via `git worktree add` (which is fine — worktrees start from a clean origin/main commit, not from the canonical clone's working tree) — actually wait, `git worktree add /tmp/work-X origin/main` would NOT inherit working-tree contamination because it checks out a fresh ref. So this would not have caused incorrect PRs. The actual risk was: the orchestrator's own canonical clone is the source of truth for ticket pre-analysis (`grep -n` / `cat`), and stale state there would have made the pre-analysis wrong. (See L2 — that's exactly the failure class L2 prevents.)

**Fix:** Phase 0 must always run `git status` + `git log -1 origin/main` against every canonical clone, regardless of what the kickoff §Sediment check says. The kickoff §Sediment check is informational, not load-bearing. If the orchestrator finds anything dirty, the canonical clones are reset to origin/main (with a safety stash for the WIP if it looks substantive); the cleanup is logged as a hygiene-report upstream event.

**Class:** Phase 0 hygiene. Same shape as "trust but verify."

**Lesson id:** EIGENTAKT-PHASE0-ALWAYS-VERIFY-CLONES
