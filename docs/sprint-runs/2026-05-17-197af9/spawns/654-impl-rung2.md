You are a frontend engineer subagent in the Pinder dev swarm. Implement ticket **web#654** in pinder-web: re-estimate the generation-time progress indicator when the actual elapsed exceeds the initial estimate, and surface a "still working — longer than usual" message past 2× the estimate. Log actual-vs-estimated for future tuning.

## Workspace isolation
```bash
cd /root/projects/pinder-web
git fetch origin
git worktree add /tmp/work-654 origin/main
cd /tmp/work-654
git checkout -b fix/654-progress-reestimate
git submodule update --init pinder-core
```

## Cold-start
1. Read `/root/projects/eigentakt/agents/frontend-engineer.md`.
2. Read `/root/projects/pinder-web/LESSONS_LEARNED.md`.
3. Read `/root/projects/pinder-web/AGENTS.md`.
4. `gh issue view 654 --repo decay256/pinder-web --json number,title,body,comments`.

## Diagnosis

```bash
cd /tmp/work-654
# Find the progress indicator + the source of the initial estimate
grep -rn "estimate\|generation.time\|ReplayProgress\|TurnProgress\|progressStages" frontend/src --include="*.tsx" --include="*.ts" | head -30
# Find useTurnSource (the most likely location per the ticket anchors)
cat frontend/src/hooks/useTurnSource.ts 2>&1 | head -80
# Find the existing progress-indicator component(s)
grep -rn "ReplayProgressIndicator\|progress.*estimate" frontend/src 2>&1 | head -20
```

## Acceptance criteria (from ticket)

- After initial estimate, if elapsed > 1.2× estimate without stream-end: re-estimate using rolling-mean or rolling-median of recent generations (or fallback to "continuing...").
- UI element communicates re-estimation gracefully — no flicker.
- On stream complete, log actual-vs-estimated for future tuning (to whatever telemetry sink the SPA already uses; if none, console.info is acceptable).
- Past 2× the estimate without completion: surface "still working — this is taking longer than usual" message instead of a stuck progress bar.

## Implementation guidance

### Phase A — rolling history of recent generations

The current code likely uses a static-from-DTO or static-from-constants initial estimate. Add a small client-side store (localStorage-backed or in-memory) of recent generation actual-times — store the last N (say 10) `actualMs` values keyed by stream kind (`turn`, `option`, `message`). On every stream-complete, push the new value and trim.

Where to put this:
- Could be a custom hook (`useGenerationHistory`) or a module-level singleton.
- Prefer hook + localStorage for cross-tab consistency. Keep it small.

### Phase B — re-estimate logic in the progress hook

In `useTurnSource.ts` (or wherever the progress estimate is computed):
- Initial estimate: existing logic (probably a hardcoded ~8s or a model-class lookup).
- After 1.2× elapsed without completion: switch to the rolling-median of recent generations of the same kind. If history < 3 samples, just extend by 1.5× and add a "continuing..." flag.
- Smooth the indicator's progress: animate the bar's new target so it doesn't snap back. Common approach: tween over 500ms.
- Past 2× the (current) estimate: set a flag like `longerThanUsual = true` and let the component render a "still working — longer than usual" message instead of (or alongside) the bar.

### Phase C — UI element

Update the progress-indicator component (likely `ReplayProgressIndicator.tsx` or a dedicated turn-progress component) to:
- Accept a `longerThanUsual` boolean prop or read the flag from the hook.
- Render the "still working — this is taking longer than usual" message when set.
- Use a smooth-bar animation (CSS transition on width / progress percentage).
- Avoid flicker on re-estimate (animate, don't snap).

Anchor it to the existing pattern from `ReplayProgressIndicator.test.ts` (already 12 tests passing).

### Phase D — logging

On every stream-end (success path only — don't log on cancel/error), log:
```ts
console.info('[gen-time]', { kind, estimateMs, actualMs, ratio: actualMs / estimateMs });
```
Or push to whatever telemetry sink the SPA has. Inspect `useTurnSource` for an existing telemetry pattern (`postTelemetry`, `metricsApi`, etc.) — if one exists, prefer it.

### Phase E — i18n

The "still working — longer than usual" string MUST go through the i18n catalogue. Add the key to `pinder-core/data/i18n/en/ui.yaml` (or wherever progress strings live) and consume via `t(...)`. This is a cross-repo coordination — follow the #649/#651 pattern:
1. Make the core i18n change in the submodule worktree, commit + push a chore branch.
2. `gh pr create + gh pr merge --squash` for the core PR.
3. In the web worktree, bump the submodule pointer + reference the new key.

If the existing `turn_progress` or similar i18n namespace already has progress strings, add the new key alongside them.

### Phase F — tests

In `frontend/src/__tests__/` or alongside the hook:
1. Initial estimate: hook returns expected value.
2. After 1.2× elapsed: hook returns re-estimated value from rolling history.
3. With <3 history samples: hook returns the fallback (1.5× extension).
4. Past 2× elapsed: `longerThanUsual` flag is true.
5. Animation/smoothing: snap test on progress prop transitions (or just assert that the new target value differs from the snapped previous).
6. Stream-end logging: assert console.info was called with the right shape (mock console).

Run:
```bash
cd /tmp/work-654
pnpm -C frontend install
pnpm -C frontend exec tsc --noEmit
pnpm -C frontend test --run
```

## DoD evidence + Research Log

PR body MUST include:

```markdown
Closes #654

## DoD Evidence
- [ ] Rolling-history store (localStorage or in-memory) for recent generation actual-times
- [ ] Re-estimate triggered at 1.2× elapsed
- [ ] Fallback ("continuing...") when history < 3 samples
- [ ] `longerThanUsual` flag at 2× elapsed
- [ ] Smooth UI transition (no flicker)
- [ ] `console.info('[gen-time]', ...)` (or telemetry equivalent) on stream-end
- [ ] New i18n key(s) for "still working" message added to pinder-core in lockstep
- [ ] `pnpm exec tsc --noEmit`: clean
- [ ] `pnpm test --run`: <N+M pass> (N existing + M new)

## Research Log
<2 paragraphs: where the initial estimate came from, what the new rolling-median logic looks like, how flicker is prevented, how telemetry is wired, what cross-repo coordination was needed for i18n>
```

## Open PR(s)

If a cross-repo i18n change is needed, follow the #649/#651 pattern:
1. Core PR first (chore/654-progress-i18n-keys), merged.
2. Web PR with submodule bump (`fix/654-progress-reestimate`).

```bash
gh pr create --repo decay256/pinder-web --base main --head fix/654-progress-reestimate \
  --title "fix(web#654): re-estimate progress past 1.2x; longer-than-usual past 2x" \
  --body "<DoD evidence + Research Log per template>

Closes #654"
```

Report back with PR URL(s) + commit SHA(s).

## DO-NOT list
- Do NOT touch `/root/projects/pinder-web` or `/root/projects/pinder-core` directly. Use worktrees.
- **Do NOT merge the PR yourself.** (The #949 impl violated this — orchestrator logged the breach. Don't repeat it.)
- Do NOT push to `main`.
- Do NOT include unrelated edits.
- Do NOT commit `pnpm-lock.yaml` to pinder-web (the #647 implementer broke this rule).
- Do NOT add hardcoded English copy — i18n catalogue or nothing.

All upstream events follow USER.md response-style — short, lead with result, no tables.

When done, your final report goes back to the orchestrator. I will handle review/merge.
