You are a code reviewer subagent in the Pinder dev swarm. Review pinder-web PR **#665** (fix for #648 — rich folded EventBox header).

## Workspace isolation
```bash
rm -rf /tmp/review-648
git clone --branch fix/648-eventbox-header-outcome \
  https://github.com/decay256/pinder-web /tmp/review-648
cd /tmp/review-648
git submodule update --init pinder-core
```

## Cold-start
1. Read `/root/projects/eigentakt/agents/code-reviewer.md`.
2. Read `/root/projects/pinder-web/LESSONS_LEARNED.md`.
3. Read `/root/projects/pinder-web/AGENTS.md`.
4. `gh pr view 665 --repo decay256/pinder-web --json title,body,additions,deletions,files`.
5. `gh issue view 648 --repo decay256/pinder-web --json number,title,body`.

## Critical context — review with HIGH scrutiny

PR #665 is **+123 / -1052** across 4 files. The implementer changed `CollapsedHeaderProps` from `{ severity, subject, result }` (3 fields) to `{ primary, suffix? }` (2 fields), then deleted 469 lines from `collapsedHeader.ts` and 579 lines from its test. This is a massive simplification.

**Risk:** existing behaviours/AC may be silently dropped along with the deleted code. The reviewer's main job here is to verify **nothing was lost**.

## Heuristic checklist

### 1. Per-kind AC coverage (THE primary check)

Verify each row of the ticket's 7-row table produces the correct output. The ticket's expected values:

- Roll, turn 1, miss DC 21, total 15 → `"Wit Miss by 6: TropeTrap"`
- Roll, turn 3, success DC 28, total 29 → `"Honesty Pass by 1: clean"`
- Shadow, turn 1, miss DC 15, die 2, Dread → `"Dread Miss by 13: Catastrophic"`
- Horniness, turn 1, miss DC 18, die 3 → `"Horniness Miss by 15: Catastrophic"`
- Horniness, turn 2, miss DC 18, die 15 → `"Horniness Miss by 3: Misfire"`
- Steering, turn 1, miss DC 20, die 10+2=12 → `"Steering Miss by 8: pivot failed"`
- Trap activated, turn 1, "pretentious" → `"Trap: Pretentious"` (note: capitalization)

For each row:
- [ ] Test exists in `collapsedHeader.test.ts` (or wherever the new tests live).
- [ ] Test asserts the exact expected string OR the equivalent `{ primary, suffix }` shape that renders to the expected string.
- [ ] Run the tests and confirm green.

### 2. Don't-break checks (the deleted-code risk)

The original `collapsedHeader.ts` had ~600 lines covering multiple event kinds and edge cases. The new file is ~150 lines. **Verify what was removed and whether it was replaced.**

- [ ] Run `git diff origin/main -- frontend/src/components/eventbox/collapsedHeader.ts | grep '^-' | grep -v '^---'` (or similar) to list the removed lines.
- [ ] For each removed function / branch, identify: (a) is it dead code now? (b) was it replaced by an equivalent in the new shape? (c) was it accidentally dropped?
- [ ] Check all callers of the OLD `CollapsedHeaderProps` shape: search for `.severity` / `.subject` / `.result` references. They should all be gone (or refactored to use `.primary` / `.suffix`).
- [ ] Check storybook / dev-only tools that may have rendered the old shape — those would break silently if not updated.

### 3. Renderer correctness

- [ ] `RollEventBox.tsx` correctly renders `${primary}: ${suffix}` when suffix is present, just `primary` otherwise.
- [ ] Visual styling (any color/severity-driven CSS) — if the old `severity` field drove color, does the new code preserve that information (perhaps via a separate prop)?
- [ ] No regressions in `RollEventBox` for layouts that previously used `severity` / `subject` / `result` as separate visual slots.

### 4. Build + tests
```bash
cd /tmp/review-648/frontend
npm install --silent 2>&1 | tail -3
npm test -- eventbox 2>&1 | tail -15
npm run build 2>&1 | tail -10
```
- [ ] Tests green (implementer claims 149/149).
- [ ] Build: only the pre-existing #663 TS error, no new errors.

### 5. PR hygiene
- [ ] `Closes #648` in PR body.
- [ ] Commit message describes the per-kind logic.

## Verdict

`APPROVE` only if all 7 AC rows are pinned by tests AND deleted code was demonstrably either dead or replaced. `CHANGES_REQUESTED` if any AC row is unverified, or if deleted code includes behaviour that wasn't replaced.

```bash
gh pr review 665 --repo decay256/pinder-web --approve -b "<body>"
# OR
gh pr review 665 --repo decay256/pinder-web --request-changes -b "<body>"
```

## DoD evidence

```
Stats: tokens_in=<N> tokens_out=<N> tokens_cache_read=<N or n/a> tokens_cache_write=<N or n/a> wall_clock_seconds=<N>
verdict: APPROVE   (or CHANGES_REQUESTED — N blockers)
```

## Reminders

Correlation id: `2026-05-17-197af9-648-code-reviewer-<your-id>`.
Sprint id: `2026-05-17-197af9`.

Per the response-style rules in `/root/.openclaw/agents-extra/pinder/USER.md`: short, lead with the verdict, no markdown tables, max 5 bullets per section.
