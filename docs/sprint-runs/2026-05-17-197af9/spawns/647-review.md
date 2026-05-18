You are a code reviewer subagent in the Pinder dev swarm. Review pinder-web PR **#662** (fix for #647 — silence cosmetic-only text-diff EventBoxes).

## Workspace isolation
```bash
rm -rf /tmp/review-647
git clone --branch fix/647-eventbox-silence-cosmetic-diffs \
  https://github.com/decay256/pinder-web /tmp/review-647
cd /tmp/review-647
git submodule update --init pinder-core
```

## Cold-start
1. Read `/root/projects/eigentakt/agents/code-reviewer.md`.
2. Read `/root/projects/pinder-web/LESSONS_LEARNED.md`.
3. Read `/root/projects/pinder-web/AGENTS.md`.
4. `gh pr view 662 --repo decay256/pinder-web --json title,body,additions,deletions,files`.
5. `gh issue view 647 --repo decay256/pinder-web --json number,title,body,comments`.

## What you're reviewing

PR #662 advertises `+2477/-4` across 5 files. Three of those are the real fix; **one is a strayed `pnpm-lock.yaml` ~2540 lines** that should not be in the PR. The repo uses **npm** (existing `frontend/package-lock.json`), not pnpm.

Real fix files (post-lockfile-stripping):

1. `frontend/src/components/eventbox/eventVisibility.ts` — new `TEXT_DIFF_LAYER_VISIBILITY` map and `resolveDiffLayerVisibility` helper.
2. `frontend/src/components/TurnResultDisplay.tsx` — filter cosmetic diff layers before rendering.
3. `frontend/src/components/eventbox/eventVisibility.test.ts` — new regression tests.
4. `frontend/src/data/displayNames.guardrail.test.ts` — allowlist update for internal identifiers in the visibility map.

## Heuristic checklist

### 1. CRITICAL — strayed package manager artifact
- [ ] **BLOCKER if present:** `frontend/pnpm-lock.yaml` is in the PR. This repo uses npm (`frontend/package-lock.json` exists, no `pnpm-lock.yaml` on main). The implementer ran `pnpm install` instead of `npm install`. The lockfile must be **removed from the PR** before merge — request a `git rm frontend/pnpm-lock.yaml && git commit --amend && git push --force-with-lease`.

### 2. AC coverage
- [ ] `TEXT_DIFF_LAYER_VISIBILITY` map classifies known layers correctly: `Meta-Prefix Strip` → silent; `TropeTrap`, `HornySpiral`, `Catastrophe`, `MisFire` → visible.
- [ ] Default for unknown layers — verify the choice is documented and conservative (visible is safer; silent hides real bugs). Either is defensible; reviewer should flag if undocumented.
- [ ] `TurnResultDisplay.tsx` filter runs **before** the diff lane is rendered. If all diffs are silent, no diff lane element exists in the DOM at all.
- [ ] Regression tests cover: cosmetic-only turn (0 boxes), mixed turn (1 box), unknown layer (default applied), no-diff turn (0 boxes — regression guard for #634-baseline).

### 3. Don't-break checks
- [ ] `displayNames.guardrail.test.ts` allowlist additions are minimal — only the new map's internal identifiers, no broadening of what's allowed elsewhere.
- [ ] `TurnResultDisplay.tsx` doesn't regress non-text-diff EventBoxes (RollCheckKind, annotations, etc.). The filter should be scoped to text-diffs only.

### 4. Build + tests
```bash
cd /tmp/review-647/frontend
npm install --silent 2>&1 | tail -3
npm run build 2>&1 | tail -10
# Expect: TS error in src/hooks/useTurnSource.ts:382 (TS2345 about DialogueOption.modifier).
# This error PRE-EXISTS on main (verified by orchestrator) — filed as web#663.
# Do NOT fail the review for it. But DO confirm it's the SAME error and nothing new.
npm test -- eventbox 2>&1 | tail -15
# Expect: green (implementer claims 1002/1002 passing).
```

- [ ] Build error matches the pre-existing #663 signature only (no new errors introduced).
- [ ] Targeted eventbox tests green.
- [ ] If npm install ran cleanly (no peer dep warnings caused by the strayed pnpm-lock interfering), note it.

### 5. PR hygiene
- [ ] `Closes #647` in PR body.
- [ ] Commit message describes the fix.

## Verdict

`CHANGES_REQUESTED` if `frontend/pnpm-lock.yaml` is present (this is a hard blocker — wrong package manager artifact in a frontend PR is a footgun that will confuse future contributors).

`APPROVE` only if the lockfile is removed AND all AC + don't-break checks pass.

```bash
gh pr review 662 --repo decay256/pinder-web --request-changes -b "<body>"
# OR after lockfile removed and re-reviewed:
gh pr review 662 --repo decay256/pinder-web --approve -b "<body>"
```

## DoD evidence

```
Stats: tokens_in=<N> tokens_out=<N> tokens_cache_read=<N or n/a> tokens_cache_write=<N or n/a> wall_clock_seconds=<N>
verdict: APPROVE   (or CHANGES_REQUESTED — N blockers)
```

## Reminders

Correlation id: `2026-05-17-197af9-647-code-reviewer-<your-id>`.
Sprint id: `2026-05-17-197af9`.

Per the response-style rules in `/root/.openclaw/agents-extra/pinder/USER.md`: short, lead with the verdict, no markdown tables, max 5 bullets per section.
