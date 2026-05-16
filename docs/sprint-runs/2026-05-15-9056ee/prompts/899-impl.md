You are a backend engineer subagent in the Pinder dev swarm. Implement **pinder-core ticket #899** in one PR.

## Ticket summary

#899 — Horniness text overlay should run LAST, after BOTH trap and shadow corruption, not before shadow. The §15 interest-delta halving (which is already deferred to last) stays where it is. Only the TEXT-REWRITE half of the horniness layer moves.

Full issue: `gh issue view 899 --repo decay256/pinder-core`.

Current order in `GameSession.cs` ~L1450-1620:
1. Trap overlay
2. **Horniness overlay (text rewrite)** ← today
3. Shadow corruption (text rewrite)
4. Horniness §15 interest-delta halving (last; unchanged)

Target order:
1. Trap overlay
2. Shadow corruption (text rewrite)
3. **Horniness overlay (text rewrite)** ← move here
4. Horniness §15 interest-delta halving (last; unchanged)

## Workspace isolation

```bash
cd /root/projects/pinder-core
git fetch origin
git worktree add /tmp/work-899 origin/main
cd /tmp/work-899
git checkout -b fix/899-horniness-text-overlay-last
```

**Work in `/tmp/work-899/` only.**

## Cold-start

1. Read `/root/.openclaw/skills/eigentakt/agents/backend-engineer.md`.
2. Read `/root/projects/pinder-core/AGENTS.md` (Snapshot Schema Discipline).
3. Read the ticket fully (link above).
4. Read `src/Pinder.Core/Conversation/GameSession.cs` around lines 1450–1620. Identify exactly where the current `ApplyHorninessOverlayAsync` / `ApplyShadowCorruptionAsync` calls live. The ticket says line ~1626 has an explicit comment block describing the OLD invariant — find it.
5. `grep -rn "horniness.*before.*shadow\|horniness.*then.*shadow\|horniness overlay.*shadow" docs/` — locate every place the old ordering is documented. Likely: `delivery-instructions.yaml`, `rules-v3*.md`, and any sprint-run kickoff docs.

## Approach (don't redesign — the ticket is precise)

1. **Swap call order in `GameSession.cs`.** Move the `ApplyHorninessOverlayAsync` call to AFTER `ApplyShadowCorruptionAsync`. The §15 interest-delta halving is a SEPARATE code path that stays where it is; verify by reading the code that `ApplyHorninessOverlayAsync` is the text-rewrite part and the halving is elsewhere.
2. **Update the comment block at ~L1626** to describe the new invariant + reference #899. Use the existing comment style.
3. **Search-and-update doc references** to the old ordering. Update each to match the new invariant. Be conservative — only touch ordering language, not the rest of the doc.
4. **Add a numbered entry to `LESSONS_LEARNED.md`** explaining the flip + WHY (horniness should have final-say over delivered text per ticket rationale). Existing lessons go up to §36 or so; pick the next number. Use the existing lesson-formatting style (Symptom / Root cause / Rule / Anchors).
5. **Update ordering-sensitive tests.** `grep -rn "Horniness.*Shadow\|Shadow.*Horniness\|textDiffs" tests/Pinder.Core.Tests/Conversation/` and flip any test that asserts the old ordering. Specifically look for tests that check `textDiffs` ordering.
6. **Snapshot/golden tests.** Look under `session-runner/` for `.snap.json` files or snapshot baselines that captured the old ordering. If found, re-bake with new ordering — but READ THE COMMITTED VALUES FIRST and only flip the ones that show shadow→horniness in the new order (vs horniness→shadow in the old). Don't blindly mass-update.
7. **§15 invariant test.** Verify the audit-log invariant (`delta_from_roll + horniness_penalty == delta_total`) is preserved. If a unit test asserts that, run it to confirm no regression.
8. **Simulator confirmation.** If a sim runner exists and is easy to invoke (`session-runner/`), run one canned turn that exercises trap + shadow + horniness all firing, and verify `textDiffs` shows Horniness LAST. If too involved, defer to manual staging check (note in DoD).

## Acceptance criteria

- [ ] Order flipped in `GameSession.cs`.
- [ ] Comment block at ~L1626 rewritten.
- [ ] Doc references updated.
- [ ] `LESSONS_LEARNED.md` entry added.
- [ ] Tests updated; full Pinder.Core.Tests green.
- [ ] §15 halving still last (unchanged path verified).

## Snapshot schema discipline (AGENTS.md)

The ticket says snapshot/golden tests may need re-baking. If `TurnSnapshot` captures `text_diffs` ordering, then changing the production order will flow into snapshots. Verify and re-bake INTENTIONALLY (document each re-baked snapshot's old SHA + new SHA in the PR body so the review can audit each).

## Workflow rules

- **Commit incrementally** after each logical step.
- Run `dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj --no-restore` after the GameSession.cs change AND after any test update. Capture to `/tmp/test-899.txt`.
- Run `dotnet build` clean on the solution.
- Open PR: `gh pr create --repo decay256/pinder-core --base main --head fix/899-horniness-text-overlay-last --fill`. Include `Closes #899` on its own line.

## Pre-existing breakage you'll see (DO NOT try to fix)

`Pinder.LlmAdapters.Tests` has 72 pre-existing failures on `main` (tracked as #909). NOT yours. Run only `tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj` for the relevant signal.

## DO NOT

- Do not merge.
- Do not push to main.
- Do not modify the §15 interest-delta-halving code path (different concern, see #399/#743).
- Do not modify unrelated files.

## Logging

```bash
cd /tmp/work-899 && bash /root/.openclaw/skills/eigentakt/scripts/log.sh backend-engineer "#899" "core/conversation" "started" "Implementing #899 per branch fix/899-horniness-text-overlay-last"
```

At exit:
```bash
cd /tmp/work-899 && bash /root/.openclaw/skills/eigentakt/scripts/log.sh backend-engineer "#899" "core/conversation" "completed" "PR <#N> opened" "<commit-sha>"
```

## Output

`## DoD Evidence` block:
- PR URL.
- `dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj` tail (green).
- `dotnet build` tail (zero errors).
- `git log --oneline origin/main..HEAD` (atomic commits visible).
- Push confirmation.
- `gh pr view <N> --json number,title,state,url`.
- agent.log lines.
- Sim run output OR rationale for deferring.

`## Research Log` block:
| Topic | Source | Key finding |
|---|---|---|

Flag deviations.
