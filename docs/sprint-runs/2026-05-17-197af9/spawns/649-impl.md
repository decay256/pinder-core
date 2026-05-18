You are a frontend engineer subagent in the Pinder dev swarm. Implement ticket **web#649** in pinder-web: expanded EventBox should show plain-language consequence + roll breakdown + text-modification sections in that order.

## Workspace isolation
```bash
cd /root/projects/pinder-web
git fetch origin
git worktree add /tmp/work-649 origin/main
cd /tmp/work-649
git checkout -b fix/649-eventbox-expanded-consequence
git submodule update --init pinder-core
```

## Cold-start
1. Read `/root/projects/eigentakt/agents/frontend-engineer.md`.
2. Read `/root/projects/pinder-web/LESSONS_LEARNED.md`.
3. Read `/root/projects/pinder-web/AGENTS.md`.
4. `gh issue view 649 --repo decay256/pinder-web --json number,title,body,comments`.

## Scope decision (orchestrator's choice — implementer should NOT relitigate)

The ticket lists two paths for the consequence text source:
- (A) Wire-DTO field — engine surfaces `consequence` on `RollCheckResult` / `ShadowCheckResult` / etc.
- (B) SPA i18n catalogue — per-kind/per-tier `{tier} → consequence` lookup.

**This PR does only (B).** (A) is filed as a separate engine-side follow-up. The wire-DTO route is the right long-term answer but it's a multi-repo coordination cost and (B) gets the user-visible fix landed faster with zero engine changes.

## Diagnosis

```bash
cd /tmp/work-649
find frontend/src/components/eventbox -type f -name "*.tsx" 2>&1 | head -10
grep -rn "EventBoxExpanded\|expanded\|ModifierBagRollFormula" frontend/src/components/eventbox/ 2>&1 | head -20
# Find the i18n source
ls /tmp/work-649/pinder-core/data/i18n/en/ 2>&1
grep -n "FailureTier\|fail_tier\|tier_label\|consequence" /tmp/work-649/pinder-core/data/i18n/en/*.yaml 2>&1 | head -20
```

## Implementation

### Phase A — i18n catalogue

Add a new `consequences.yaml` (or extend existing) in `pinder-core/data/i18n/en/` with per-kind/per-tier consequence strings. Keys like:

```yaml
consequence:
  roll.pass.clean: "Your {stat} roll landed — the message reads sincere and lands well."
  roll.miss.tropetrap: "You walked into a trope trap — the message reads as a cliché instead of fresh."
  roll.miss.catastrophic: "Your {stat} roll bombed catastrophically — the message lands very poorly."
  shadow.miss.dread: "Your Dread shadow took over — the message comes out anxious and over-explaining."
  shadow.miss.denial: "Your Denial shadow took over — the message dodges what you really wanted to say."
  shadow.miss.fixation: "Your Fixation shadow took over — the message obsesses over a detail and misses the moment."
  shadow.miss.madness: "Your Madness shadow took over — the message reads chaotic and unhinged."
  shadow.miss.despair: "Your Despair shadow took over — the message reads hopeless and unflirty."
  shadow.miss.overthinking: "Your Overthinking shadow took over — the message reads as second-guessing on display."
  horniness.miss.catastrophic: "Horniness misfired catastrophically — the message reads thirsty in a cringey way."
  horniness.miss.misfire: "Horniness misfired — the message reads slightly more thirsty than intended."
  steering.miss.pivot_failed: "Steering missed — the pivot didn't land and the message stays on the rejected topic."
  trap.activated: "You walked into the {trap_name} trap — the message will follow that pattern instead of your intended one."
```

The keys mirror the existing `deriveCollapsedHeader` per-kind logic from #648. **Match the casing/labels** the #648 PR established.

If `pinder-core/data/i18n/en/ui.yaml` is the existing single source, add a new section instead of a new file. Use your judgement based on what's already there.

### Phase B — frontend renderer

Find the expanded EventBox component (likely `frontend/src/components/eventbox/EventBoxExpanded.tsx` or rendered inside `RollEventBox` after expansion). Restructure to render three sections in this order:

1. **Consequence** — one or two plain sentences. Resolve via `t(consequenceKey({kind, tier, stat, ...}))`. Stat names get substituted (`{stat}` → `Honesty`, etc.) using existing i18n interpolation. Fall back to nothing (hide the section) if no key matches.
2. **Roll breakdown** — already rendered via `ModifierBagRollFormula` (#592). Keep as-is. For shadow events, also show the player's shadow level + threshold (look for the existing rendering — if it's not there for shadow, add it).
3. **Text modification** — before/after spans (already rendered). Hide the section when no diff exists.

### Phase C — tests

In `frontend/src/components/eventbox/__tests__/` (or wherever the expanded EventBox is tested), add fixture tests for each of the 4 examples in the ticket body:

1. Turn 1 shadow miss (Dread) → consequence text contains "Dread shadow took over".
2. Turn 1 horniness miss → consequence text contains "Horniness misfired catastrophically".
3. Turn 1 trap "pretentious" → consequence text contains "Pretentious trap".
4. Turn 3 roll pass (Honesty) → consequence text contains "Honesty roll landed" (or "Honesty" + "lands well").

### Phase D — pinder-core PR for i18n

This requires a pinder-core PR (just the i18n yaml addition) followed by a submodule bump in the pinder-web PR — same pattern as the #651 i18n key fix that just merged.

**Order of operations:**

```bash
# 1. Make the change in the submodule
cd /tmp/work-649/pinder-core
git fetch origin
git checkout -b chore/web649-consequence-i18n main
# edit data/i18n/en/ui.yaml or new consequences.yaml
git add -A
git commit -m "chore(web#649): add consequence i18n keys for expanded EventBox

Companion to pinder-web #649. Per-kind/per-tier plain-language
consequence strings for the expanded EventBox renderer."
git push -u origin chore/web649-consequence-i18n
gh pr create --repo decay256/pinder-core --base main --head chore/web649-consequence-i18n \
  --title "chore(web#649): consequence i18n keys" \
  --body "Companion to pinder-web #649."
# Merge it
gh pr merge --repo decay256/pinder-core --squash --delete-branch <pr-number>
git checkout main && git pull origin main
NEW_SHA=$(git rev-parse HEAD)

# 2. Bump submodule in outer
cd /tmp/work-649
git add pinder-core
# (rest of outer commits below)
```

## Build + test

```bash
cd /tmp/work-649
cd frontend
npm install --silent 2>&1 | tail -3
npm test -- eventbox 2>&1 | tail -15
npm run build 2>&1 | tail -10
```

- Build: only the pre-existing #663 TS error, no new errors.
- Tests: green.

## Commit + push + PR

```bash
git add -A
git commit -m "fix(#649): expanded EventBox shows plain-language consequence + roll breakdown + diff

Closes #649.

- New per-kind/per-tier consequence i18n keys (companion pinder-core PR #N landed).
- EventBoxExpanded renders three sections in order: consequence, roll breakdown, text modification.
- Hide-by-default for missing consequence (graceful) and missing diff (already worked).
- 4 fixture tests cover ticket-body examples (shadow Dread, horniness catastrophic, trap, roll pass).

Engine-side Consequence wire field deferred to a follow-up — see issue body Path A.

DoD: build clean (pre-existing #663 unchanged), eventbox tests green."
git push -u origin fix/649-eventbox-expanded-consequence
gh pr create --repo decay256/pinder-web --base main --head fix/649-eventbox-expanded-consequence \
  --title "fix(#649): expanded EventBox shows consequence + breakdown + diff" \
  --body "Closes #649. Companion to pinder-core PR #N (i18n keys).

## What changed
<bullets>

## Tests
<names>

## Follow-up
- Engine-side Consequence wire field (Path A from ticket body): see filed follow-up.

## DoD
- Build: clean (#663 pre-existing unchanged)
- Tests: <pass/fail counts>"
```

Also file the engine-side follow-up via `gh issue create --repo decay256/pinder-core` titled `[chore][P2] [#649 follow-up] Surface 'consequence' on RollCheckResult / ShadowCheckResult / HornyCheckResult / TropeTrapResult wire DTOs` with body explaining that the SPA currently uses i18n catalogue fallback per #649 PR, and the engine-side field would let it surface dynamic consequences (e.g. trap-specific verbs).

## DoD evidence

```
Stats: tokens_in=<N> tokens_out=<N> tokens_cache_read=<N or n/a> tokens_cache_write=<N or n/a> wall_clock_seconds=<N>
result: pr-opened  pr=<N>  sha=<commit-sha>  build=clean  tests=<N>/<N>  companion_pinder_core_pr=<M>
```

## Reminders

Correlation id: `2026-05-17-197af9-649-frontend-engineer-<your-id>`.
Sprint id: `2026-05-17-197af9`.
Worktree: `/tmp/work-649`.

Per WORKSPACE-ISOLATION: only operate inside `/tmp/work-649/`. Use **npm**, not pnpm.
Per NO-FALSE-DOD-CLAIMS: run the build yourself and inspect the tail before claiming DoD.
Per NEVER-EXIT-MID-DRAIN: commit + push + open BOTH PRs before your final message. The cross-repo flow is: pinder-core PR → merge → bump submodule in outer → outer PR.

Per the response-style rules in `/root/.openclaw/agents-extra/pinder/USER.md`: short, lead with the result, no markdown tables.
