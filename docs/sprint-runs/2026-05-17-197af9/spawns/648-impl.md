You are a frontend engineer subagent in the Pinder dev swarm. Implement ticket **web#648** in pinder-web: folded EventBox header is uninformative — should carry outcome verb + margin + tier.

## Workspace isolation
```bash
cd /root/projects/pinder-web
git fetch origin
git worktree add /tmp/work-648 origin/main
cd /tmp/work-648
git checkout -b fix/648-eventbox-header-outcome
git submodule update --init pinder-core
```

## Cold-start
1. Read `/root/projects/eigentakt/agents/frontend-engineer.md`.
2. Read `/root/projects/pinder-web/LESSONS_LEARNED.md`.
3. Read `/root/projects/pinder-web/AGENTS.md`.
4. `gh issue view 648 --repo decay256/pinder-web --json number,title,body,comments`.

## Diagnosis

The folded EventBox header (`deriveCollapsedHeader` helper from #598/#623) currently emits just `"{Kind} Check"` (e.g. "Horniness Check") for roll/shadow/steering/horniness events. Should emit something like "Horniness Miss by 8: Catastrophic" — the verb + margin + tier.

The ticket includes a 7-row example table (turn1/2/3 of a known staging session) that defines the desired output per event kind:

- **Roll (success/miss):** `{Stat} {Pass|Miss} by {Margin}: {TierName|clean}`
- **Shadow check:** `{ShadowType} {Miss} by {Margin}: {Tier}` (note: ShadowType replaces the generic "Shadow Check" — e.g. "Dread Miss by 13: Catastrophic").
- **Horniness check:** `Horniness Miss by {Margin}: {Tier}`
- **Steering check:** `Steering Miss by {Margin}: pivot failed`
- **Trap activated:** `Trap: {TrapName}` (no margin/tier — different structure).

```bash
cd /tmp/work-648
find frontend/src/components/eventbox -type f -name "*.ts" -o -name "*.tsx" 2>&1 | head -20
grep -n "deriveCollapsedHeader\|collapsed_header\|foldedHeader\|getFoldedHeader" frontend/src/ 2>&1 | head -20
grep -rn "Horniness Check\|Wit Check\|Shadow Check" frontend/src/components/eventbox/ 2>&1 | head -10
```

## Goal

Rich folded header per event kind, computed by `deriveCollapsedHeader` (or whatever the current helper is). Each kind has a `{primary, suffix?}` shape:

- `primary` = outcome verb + magnitude (`"Wit Miss by 6"`, `"Honesty Pass by 1"`).
- `suffix` = named tier / subtype (`"TropeTrap"`, `"Catastrophic"`, `"clean"`, the trap name).

For the trap case which has no margin/tier, just `{primary: "Trap", suffix: TrapName}` works.

The header rendering layer concatenates `${primary}: ${suffix}` when both are present, or just `primary` otherwise.

## Implementation

1. **Audit current `deriveCollapsedHeader`.** Read the existing helper. It already returns SOMETHING — figure out what. The fix may be to extend its return shape, or to replace the per-kind switch with a richer one.
2. **Per-kind logic:**
   - **Roll:** read `payload.tier` ("Pass" vs "Miss"), `payload.margin`, `payload.tier_name` (or "clean" if undefined). The verb is "Pass" or "Miss"; magnitude is `Math.abs(margin)`; suffix is tier_name or "clean".
   - **Shadow:** read `payload.shadow_type` (Dread/Denial/etc. — per #655 this is exposed; if not, use the generic "Shadow" word for now and the #655 fix layers on top). Verb + margin + tier same as roll.
   - **Horniness / Steering:** same as roll but with the kind name; tier per-kind ("Catastrophic" for horniness Miss by ≥10, "Misfire" for Miss < 10, "pivot failed" for steering Miss, "clean" for steering Pass).
   - **Trap activated:** simple `{primary: "Trap", suffix: trapName}`.
3. **Where the helper consumer renders** — update the JSX to consume the new `{primary, suffix}` shape (concatenate, or render in two separate slots if there's a styling reason).
4. **Tests.** Use the 7-row table from the ticket as fixtures. For each row: build a synthetic event payload, call `deriveCollapsedHeader`, assert it returns the expected `{primary, suffix}` matching the table.
5. **Don't break #647.** The visibility table from #647 should still filter cosmetic layers — the header logic only runs on non-cosmetic ones.

## Build + test

```bash
cd /tmp/work-648
cd frontend
npm install --silent 2>&1 | tail -3
npm test -- eventbox 2>&1 | tail -15
# Expect: green, 200+ tests including 7 new header tests.
npm run build 2>&1 | tail -10
# Expect: same pre-existing useTurnSource.ts:382 #663 error as before, no new errors.
```

## Commit + push + PR

```bash
git add -A
git commit -m "fix(#648): folded EventBox header carries outcome + margin + tier

Closes #648.

- deriveCollapsedHeader now returns { primary, suffix? } with verb-by-margin and tier name.
- Per-kind logic: Roll, Shadow, Horniness, Steering, Trap activated.
- Renderer concatenates 'primary: suffix' when both present, primary otherwise.
- 7 new fixture-based tests (one per ticket-table row).

DoD: build clean (pre-existing #663 unchanged), eventbox tests green."
git push -u origin fix/648-eventbox-header-outcome
gh pr create --repo decay256/pinder-web --base main --head fix/648-eventbox-header-outcome \
  --title "fix(#648): rich folded EventBox header (verb + margin + tier)" \
  --body "Closes #648.

## What changed
<bullets>

## Tests
7 new fixture tests (one per ticket-table row).

## DoD
- Build: clean (#663 pre-existing unchanged)
- Tests: <pass/fail counts>"
```

## DoD evidence

```
Stats: tokens_in=<N> tokens_out=<N> tokens_cache_read=<N or n/a> tokens_cache_write=<N or n/a> wall_clock_seconds=<N>
result: pr-opened  pr=<N>  sha=<commit-sha>  build=clean  tests=<N>/<N>
```

## Reminders

Correlation id: `2026-05-17-197af9-648-frontend-engineer-<your-id>`.
Sprint id: `2026-05-17-197af9`.
Worktree: `/tmp/work-648`.

Per WORKSPACE-ISOLATION: only operate inside `/tmp/work-648/`. Use **npm**, not pnpm.

Per NO-FALSE-DOD-CLAIMS: run the build yourself and inspect the tail before claiming DoD.

Per NEVER-EXIT-MID-DRAIN: commit + push + open PR before your final message.

Per the response-style rules in `/root/.openclaw/agents-extra/pinder/USER.md`: short, lead with the result, no markdown tables.
