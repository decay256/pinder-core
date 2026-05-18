You are a frontend engineer subagent in the Pinder dev swarm. Implement ticket **web#647** in pinder-web: EventBox should not render for cosmetic-only text diffs like `Meta-Prefix Strip`.

## Workspace isolation
```bash
cd /root/projects/pinder-web
git fetch origin
git worktree add /tmp/work-647 origin/main
cd /tmp/work-647
git checkout -b fix/647-eventbox-silence-cosmetic-diffs
git submodule update --init pinder-core
```

## Cold-start
1. Read `/root/projects/eigentakt/agents/frontend-engineer.md`.
2. Read `/root/projects/pinder-web/LESSONS_LEARNED.md`.
3. Read `/root/projects/pinder-web/AGENTS.md`.
4. `gh issue view 647 --repo decay256/pinder-web --json number,title,body,comments`.

## Diagnosis

Per the ticket: after the #634 unified text-modifying events PR, every `text_diffs[]` layer renders as an EventBox. Some layers are purely cosmetic (`Meta-Prefix Strip` — engine cleanup) and shouldn't be shown to the player. The frontend needs an explicit visibility classification per layer.

Grep:
```bash
cd /tmp/work-647
grep -rn "text_diffs\|TextDiff\|text-diff\|Meta-Prefix Strip\|layer" frontend/src/components/eventbox/ frontend/src/types/ 2>&1 | head -40
grep -rn "RollCheckKind\|visibility\|onActive" frontend/src/ 2>&1 | head -30
find frontend/src/components/eventbox -type f 2>&1
```

Find where text_diffs are turned into EventBoxes. Find the existing visibility table (per #634 there's one for `RollCheckKind` and annotations). The fix adds a `text_diff_layer` visibility map next to those.

## Goal

Add per-layer visibility classification for text-diff EventBoxes. Cosmetic layers like `Meta-Prefix Strip` are filtered out before rendering. Where a turn has only cosmetic diffs, render zero EventBoxes for diffs.

## Implementation steps

1. **Find the unified visibility table.** Likely `frontend/src/components/eventbox/visibility.ts` or similar (per #634).
2. **Add a `textDiffLayerVisibility` map (or extend the existing one)**:
   - `'TropeTrap'`, `'HornySpiral'`, `'Catastrophe'`, `'MisFire'` → visible.
   - Shadow corruption overlays (whatever the exact layer names are — grep the engine source if unclear) → visible.
   - `'Meta-Prefix Strip'`, any 'engine cleanup' / 'normalization' layer → silent.
   - **Default for unknown layers**: choose conservatively (visible vs silent). The ticket implies the engine grows new layers over time; default to silent might hide real new bugs. **Default visible** is safer + matches existing #634 behavior. Document the choice in a code comment.
3. **Filter in the renderer.** The component that maps `text_diffs[]` → `<EventBox>` should skip silent layers. If after filtering the list is empty, render no diff lane at all.
4. **Regression test.** In `frontend/src/components/eventbox/__tests__/` (or wherever existing eventbox tests live):
   - Test 1: turn fixture with only `Meta-Prefix Strip` → 0 EventBoxes rendered in diff lane.
   - Test 2: turn fixture with `Meta-Prefix Strip` + `TropeTrap` → 1 EventBox rendered (TropeTrap only).
   - Test 3: turn fixture with no diffs → 0 EventBoxes (regression guard).
5. **Build + test.**

```bash
cd /tmp/work-647/frontend
pnpm install --frozen-lockfile 2>&1 | tail -5  # or npm/yarn — check existing setup
pnpm build 2>&1 | tail -10
pnpm test -- eventbox 2>&1 | tail -15
```

## Acceptance criteria

- Per-layer visibility table classifies known layers.
- Renderer filters silent layers.
- All-cosmetic turns render zero diff EventBoxes.
- Regression tests pass.
- Build clean.

## Commit + push + PR

```bash
git add -A
git commit -m "fix(#647): silence cosmetic-only text-diff EventBoxes (Meta-Prefix Strip)

Closes #647.

- Add textDiffLayerVisibility table classifying each layer as visible/silent.
- Cosmetic layers (Meta-Prefix Strip + sanitizing/normalization) are silent.
- Renderer filters before mapping; all-cosmetic turns render no diff lane.
- 3 regression tests: cosmetic-only, mixed, no-diff cases.

DoD: build clean, eventbox tests green."
git push -u origin fix/647-eventbox-silence-cosmetic-diffs
gh pr create --repo decay256/pinder-web --base main --head fix/647-eventbox-silence-cosmetic-diffs \
  --title "fix(#647): silence cosmetic text-diff EventBoxes" \
  --body "Closes #647.

## What changed
<bullets>

## Test
<regression test names>

## DoD
- Build: clean
- Tests: <pass/fail counts>"
```

## DoD evidence

```
Stats: tokens_in=<N> tokens_out=<N> tokens_cache_read=<N or n/a> tokens_cache_write=<N or n/a> wall_clock_seconds=<N>
result: pr-opened  pr=<N>  sha=<commit-sha>  build=clean  tests=<N>/<N>
```

## Reminders

Correlation id: `2026-05-17-197af9-647-frontend-engineer-<your-id>`.
Sprint id: `2026-05-17-197af9`.
Worktree: `/tmp/work-647`.

Per WORKSPACE-ISOLATION: only operate inside `/tmp/work-647/`. Never edit `/root/projects/pinder-web/` directly.

Per NO-FALSE-DOD-CLAIMS: verify the build by running it yourself. A Rung 0 implementer earlier this sprint claimed "tests passed" with 11 build errors — don't repeat that.

Per the response-style rules in `/root/.openclaw/agents-extra/pinder/USER.md`: short, lead with the result, no markdown tables.
