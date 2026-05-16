You are a frontend engineer subagent in the Pinder dev swarm. Implement **pinder-web ticket #595** in one PR.

## Ticket summary

#595 — [bug] Player-visible NAT 20 explainer text leaks internal issue reference `per #271`.

Player-visible string `frontend/src/components/natDieExplainer.ts:10` contains `, per #271` — internal GitHub ref leaking into game UI. Fix the copy AND move `nat1`/`nat20` to i18n keys AND add a regression test.

Full issue: `gh issue view 595 --repo decay256/pinder-web`.

## Workspace isolation (CRITICAL — WORKSPACE-ISOLATION)

```bash
cd /root/projects/pinder-web
git fetch origin
git worktree add /tmp/work-595-r1 origin/main
cd /tmp/work-595-r1
git submodule update --init pinder-core
git checkout -b fix/595-strip-issue-ref-nat-explainer-r1
```

**Do NOT touch `/root/projects/pinder-web/` directly. All work in `/tmp/work-595-r1/`.**

## Cold-start

1. Read `/root/.openclaw/skills/eigentakt/agents/frontend-engineer.md`.
2. Read `/root/projects/pinder-web/LESSONS_LEARNED.md` (especially WORKSPACE-ISOLATION, SUBMODULE-SYNC-AFTER-REBASE, SELF-APPROVE-BLOCKED, REGRESSION-TESTS-ON-BUGS).
3. Read `/root/projects/pinder-web/AGENTS.md`.
4. `gh issue view 595 --repo decay256/pinder-web` — pay attention to the test scaffolding the ticket already provides.

## Approach

The ticket body has the full fix. Don't redesign:

1. Edit `frontend/src/components/natDieExplainer.ts:10` — change `nat20` string:
   - From: `'🎲 NAT 20 — automatic crit success (and grants advantage on the next roll, per #271)'`
   - To: `'🎲 NAT 20 — automatic crit success. Grants advantage on the next roll.'`
2. Move both `nat1` and `nat20` strings to i18n. New keys: `turn_result.nat20_explainer`, `turn_result.nat1_explainer` (mirror existing turn_result.* keys' naming).
3. Update the i18n table at `frontend/src/i18n/en.ts` (or wherever the en locale lives — `grep -r 'turn_result\.' frontend/src/i18n` to find).
4. Update `natDieExplainer.ts` to consume the i18n keys via the project's existing i18n hook (find the pattern in adjacent components).
5. Add the regression test that the ticket specifies:
   ```ts
   test('no player-visible string contains "#NNN" issue references', () => {
     const allI18nKeys = Object.values(en);
     const offenders = allI18nKeys.filter(v => typeof v === 'string' && /\b#\d{2,}\b/.test(v));
     expect(offenders).toEqual([]);
   });
   ```
   The test must walk nested objects (the i18n table is usually nested) — adjust accordingly. Acceptable to use a recursive walk that flattens to leaf strings.

## Acceptance criteria

- [ ] `nat20` string no longer contains `#271` or any `#NNN` ref.
- [ ] Both `nat1` and `nat20` are i18n keys, not naked strings in code.
- [ ] Regression test added that fails if any `#NNN` ref re-appears in player-visible i18n strings.
- [ ] All existing tests still pass.
- [ ] `npm run build` (or `tsc -b && vite build`) succeeds cleanly.

## Workflow rules (mandatory)

- Atomic commits.
- Run tests, capture to `/tmp/test-595.txt`, read tail/grep only.
- Run deploy build (`npm run build` or equivalent — read `deploy.sh` first to confirm exact command).
- Open PR: `gh pr create --repo decay256/pinder-web --base main --head fix/595-strip-issue-ref-nat-explainer-r1 --fill`. Include `Closes #595` on its own line in the body.

## DO NOT

- Do not merge.
- Do not push to main.
- Do not bump the pinder-core submodule pointer.
- Do not modify unrelated files (no drive-bys; APPROVED-WORK-IS-IMMUTABLE applies in reverse — don't add scope).
- Do not work in `/root/projects/pinder-web/` directly.

## Logging to agent.log (lesson AGENT-LOG-EVERYTHING)

At task entry, after reading cold-start materials:
```bash
bash /root/.openclaw/skills/eigentakt/scripts/log.sh frontend-engineer "#595" "frontend/natDieExplainer" "started" "Implementing #595 per branch fix/595-strip-issue-ref-nat-explainer"
```

Note: `log.sh` lives in eigentakt skill dir, and appends to `<cwd>/agent.log`. Run from `/tmp/work-595-r1` so the log lands in the worktree, then I (orchestrator) will pick it up at merge time. If you're outside the project root, prepend with `cd /tmp/work-595-r1 && `.

At task exit, after PR opened:
```bash
cd /tmp/work-595-r1 && bash /root/.openclaw/skills/eigentakt/scripts/log.sh frontend-engineer "#595" "frontend/natDieExplainer" "completed" "PR <#N> opened" "<commit-sha>"
```

## Output requirements

End your final reply with:

### `## DoD Evidence` block (mandatory — actual tool output, not self-attestation):
- PR URL.
- Test tail showing pass (final summary line at minimum).
- `npm run build` (or equivalent) tail showing zero errors.
- `git log -1 --oneline`.
- Push confirmation.
- `gh pr view <N> --json number,title,state,url` output.
- Paste the two agent.log lines you appended.

### `## Research Log` block (mandatory):
| Topic | Source (URL or file path) | Key finding |
|---|---|---|

Flag any deviations from the spec with rationale.
