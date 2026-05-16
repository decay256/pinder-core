You are a backend engineer subagent in the Pinder dev swarm. Implement **pinder-core ticket #907** in one PR.

## Ticket summary

#907 — [bug] `TextingStyleAggregator` produces internally-contradictory style profiles (e.g. `wall-of-text` + `≤5 words`). Fix: encode a conflict matrix, resolve conflicts deterministically at aggregation time, log dropped fragments, add an auditor tool, and add a defensive length-hint priority rule to the opponent system prompt.

Full issue (read it carefully — it's long and specific): `gh issue view 907 --repo decay256/pinder-core`. Pay attention to:
- The required conflict matrix (encoded in YAML at `data/persona/texting-style-conflicts.yaml`).
- The aggregation algorithm: keep the value picked earliest in seed-order; if dropped, attempt re-pick from remaining candidate values.
- The auditor tool requirement.
- The length-hint defensive rule (you may roll this into the same PR or file as follow-up — author's choice).

## Workspace isolation (CRITICAL — WORKSPACE-ISOLATION)

```bash
cd /root/projects/pinder-core
git fetch origin
git worktree add /tmp/work-907-r3 origin/main
cd /tmp/work-907-r3
git checkout -b fix/907-texting-style-conflict-matrix-r3
```

**Do NOT touch `/root/projects/pinder-core/` directly. All work in `/tmp/work-907-r3/`.**

## Cold-start

1. Read `/root/.openclaw/skills/eigentakt/agents/backend-engineer.md`.
2. Read `/root/projects/pinder-core/LESSONS_LEARNED.md` (WORKSPACE-ISOLATION, REGRESSION-TESTS-ON-BUGS, SELF-APPROVE-BLOCKED, and #36 CONTENT-FILES-MUST-FOLLOW-CODE-DEPLOYMENT-PIPELINE — relevant here because you're adding a new YAML data file).
3. Read `/root/projects/pinder-core/AGENTS.md` (Snapshot Schema Discipline — does this change add a player-visible field? The conflict resolution adds entries to an audit log, which may or may not be in the snapshot).
4. Read `docs/persona/texting-style-aggregation.md` — the canonical doc for the existing aggregator.
5. Read the source: `src/Pinder.Core/Prompts/TextingStyleAggregator.cs` and one test file `tests/Pinder.Core.Tests/Prompts/TextingStyleAggregatorTests.cs` (find it via `find tests -name '*TextingStyle*'`).
6. Read `data/items/starter-items.json` — find a couple of items with `texting_style_fragment` blocks so you understand the input shape.
7. Read `data/game-definition.yaml` (or wherever the canonical YAML loader lives) so you understand the existing YAML-loading patterns. Mirror that pattern for the new conflicts file.

## Approach

1. **New data file: `data/persona/texting-style-conflicts.yaml`.**
   - Schema as in the ticket. Top-level `conflicts:` list. Each entry: `axis_a: { axis: <name>, value: <string> }`, `axis_b: { axis: <name>, value: <string> }`, `reason: <string>`.
   - Seed with at LEAST the 6 conflicts the ticket lists explicitly. Add more if the ticket's "data hygiene" auditor pass surfaces additional ones.

2. **New class: `Pinder.Core.Prompts.TextingStyleConflicts`** (or `.Persona.TextingStyleConflicts` — match adjacent code's namespace).
   - Loads the YAML at startup, exposes `bool AreConflicting((string axis, string value) a, (string axis, string value) b)` and `string? GetReason(...)`.
   - Validates symmetry (every conflict is bidirectional by construction — verified at load time).
   - Validates non-empty `reason`.

3. **Refactor `TextingStyleAggregator`.**
   - Inject `TextingStyleConflicts` (DI or static catalog — match adjacent patterns).
   - Add the conflict-aware aggregation per the ticket §"Conflict-aware aggregation". Algorithm:
     - Pick per-axis value deterministically from seed (existing behavior).
     - Walk pairs; on conflict, drop the LATER-picked value, attempt re-pick from remaining candidate values for that axis (excluding any value that conflicts with already-kept values).
     - Emit one audit entry per dropped fragment: `(character_id, axis, dropped_value, kept_value_that_caused_drop, reason)`. The audit log structure should mirror the existing aggregator-audit shape if one exists; otherwise add one.

4. **Auditor tool: `tools/TextingStyleAuditor/Program.cs`** (new project under `tools/`, csproj following the pattern of any existing `tools/<X>/` project — if none exists, put it under `tests/` as a test-only "tool" that's runnable via `dotnet test --filter`).
   - Walks `data/items/starter-items.json` (or its canonical loader).
   - Reports pairs of items whose `texting_style_fragment`s would generate a conflict under the matrix.
   - Reports any item with internally-incoherent fragments (e.g. both `tics: never asks questions` and `tics: always ends with a question` on a single item).
   - Exit 0 if zero conflicts; non-zero if conflicts found, with output naming each.

5. **Length-hint defensive rule.** Roll into this PR. Update the opponent system prompt (find it via `grep -rn "playerLen\|length hint" src/` and `data/prompts/`) to add the priority statement the ticket spec'd:
   > "The length rule above is a stylistic guideline, NOT a hard cap. For this message, aim for ~{playerLen} characters as the engine specifies. Style-rule length axes apply ONLY when they are compatible with the engine-specified length."

6. **Tests.**
   - `TextingStyleConflictsTests` — matrix loads, every entry has a reason, lookup is bidirectional, unknown pairs return false.
   - `TextingStyleAggregatorTests` — add cases for the conflict-resolution paths:
     - Two source items with conflicting `length` axes → only one survives, deterministic winner documented in test.
     - Contradictory `tics` pair → conflict resolved per matrix.
     - Audit entry produced per drop (assert on the audit-log shape).
   - Auditor tool runs clean on the current dataset, or surfaces the conflicts you fix in `data/items/starter-items.json` by either adding matrix entries or rewriting incoherent fragments.

7. **Lesson capture.** Append to `LESSONS_LEARNED.md`:
   - Title: "Independent-axis aggregation is wrong when the axes interact"
   - Pattern: when aggregating from a multi-source pool of independent axes, the cross-axis consistency check belongs at aggregation time, not as a runtime assertion in the consumer. List the matrix shape as the pattern.
   - Anchor: this PR + #907.
   - Use the existing lesson-formatting style (numbered section, "Symptom / Root cause / Rule / Anchors").

## Acceptance criteria (from #907 DoD)

- [ ] `TextingStyleConflicts.cs` + `data/persona/texting-style-conflicts.yaml` exist and are loaded by the aggregator.
- [ ] `TextingStyleAggregator` resolves conflicts deterministically per the algorithm above.
- [ ] Audit log surfaces dropped fragments.
- [ ] Auditor tool exists and reports zero remaining conflicts under the current dataset (after fixing any flagged items).
- [ ] Length-hint defensive rule added to opponent system prompt.
- [ ] All tests pass; `Pinder.Core.Tests` end-to-end green.
- [ ] `LESSONS_LEARNED.md` updated.
- [ ] `dotnet build` clean.

## Snapshot schema check (mandatory)

This change adds an aggregation-time audit log. If the existing aggregator audit log already lands in `TurnSnapshot` (or `GameSession` startup metadata captured by `SessionSnapshot`), the new dropped-fragment entries flow through automatically. If not, that's a separate snapshot-schema concern — note it in the PR body but don't block on it (file follow-up if needed).

## Workflow rules (mandatory)

- **Commit incrementally.** After each logical step (new file added, aggregator refactored, tests added, auditor scaffolded), commit. Do NOT accumulate large uncommitted diffs — if the stream cuts mid-step, no work is lost when commits land at each step boundary.
- Atomic commits per logical step.
- Run tests with output to `/tmp/test-907.txt`, tail/grep only.
- Run `dotnet build` on the whole solution before declaring done.
- Open PR via `gh pr create --repo decay256/pinder-core --base main --head fix/907-texting-style-conflict-matrix-r3 --fill`. Include `Closes #907` on its own line in the body.

## Pre-existing breakage you'll see (DO NOT try to fix in this PR)

`Pinder.LlmAdapters.Tests` has 72 pre-existing failures on `main` today (rooted in `game-definition.yaml` missing `horniness_time_modifiers` key). Tracked as **#909**. These are NOT yours. When you run the full suite, expect that count to be present and unchanged. If you run only `tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj`, you should see 100% green (baseline: 2683/2683 before your change; expect the same plus your new tests after).

## DO NOT

- Do not merge.
- Do not push to main.
- Do not modify unrelated files (no drive-bys).
- Do not work in `/root/projects/pinder-core/` directly.
- Do not try to fix #909.

## Logging to agent.log

At task entry:
```bash
cd /tmp/work-907-r3 && bash /root/.openclaw/skills/eigentakt/scripts/log.sh backend-engineer "#907" "core/prompts" "started" "Implementing #907 per branch fix/907-texting-style-conflict-matrix-r3"
```

At task exit:
```bash
cd /tmp/work-907-r3 && bash /root/.openclaw/skills/eigentakt/scripts/log.sh backend-engineer "#907" "core/prompts" "completed" "PR <#N> opened" "<commit-sha>"
```

## Output requirements

End your final reply with:

### `## DoD Evidence` block (mandatory — actual tool output, not self-attestation):
- PR URL.
- `dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj` tail — must show green (and your new tests).
- `dotnet build` tail — zero errors.
- Auditor tool output (`dotnet run` exit code + output).
- `git log -1 --oneline`.
- Push confirmation.
- `gh pr view <N> --json number,title,state,url`.
- agent.log lines you appended.

### `## Research Log` block (mandatory):
| Topic | Source | Key finding |
|---|---|---|

Flag any deviations from the spec with rationale.
