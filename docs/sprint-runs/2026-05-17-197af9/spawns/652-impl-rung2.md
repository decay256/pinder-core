You are a frontend engineer subagent in the Pinder dev swarm. Implement ticket **web#652** in pinder-web: the main-roll formula should be a foldable section INSIDE the success/miss EventBox, not rendered AFTER the intended-message text.

## Workspace isolation
```bash
cd /root/projects/pinder-web
git fetch origin
git worktree add /tmp/work-652 origin/main
cd /tmp/work-652
git checkout -b fix/652-formula-folds-under-eventbox
git submodule update --init pinder-core
```

## Cold-start
1. Read `/root/projects/eigentakt/agents/frontend-engineer.md`.
2. Read `/root/projects/pinder-web/LESSONS_LEARNED.md`.
3. Read `/root/projects/pinder-web/AGENTS.md`.
4. `gh issue view 652 --repo decay256/pinder-web --json number,title,body,comments`.

## Diagnosis

The post-#628 layout currently renders:
1. The success/miss EventBox header (folded, from `RollEventBox`).
2. The intended message text.
3. The `ModifierBagRollFormula` (as a SIBLING below the message).

The ticket asks for:
1. The success/miss EventBox header (folded shows result; expanded shows the formula INSIDE it).
2. The intended message text (rendered above as plain text).

So the `ModifierBagRollFormula` needs to move from sibling-node to fold-content of the EventBox.

Related to the #649 work that already shipped: PR #666 added expanded-EventBox sections for consequence + roll breakdown + text-modification (the F3 layout). Check whether #649 already moved `ModifierBagRollFormula` into the expanded box — if so, this ticket may already be partly done and the remaining work is to remove the duplicate sibling render in `TurnResultDisplay`.

Run these probes BEFORE coding:
```bash
cd /tmp/work-652
grep -rn "ModifierBagRollFormula\|RollEventBox" frontend/src --include="*.tsx" --include="*.ts" | head -30
sed -n '1,50p' frontend/src/components/eventbox/RollEventBox.tsx
grep -n "ModifierBagRollFormula" frontend/src/components/TurnResultDisplay.tsx
# Find where the formula is currently rendered as a sibling vs inside the expanded RollEventBox
sed -n '1,80p' frontend/src/components/TurnResultDisplay.tsx
```

## Acceptance criteria (from ticket)

- The main-roll formula is a foldable section *inside* the success/miss EventBox, not a sibling node after the intended-message text.
- Expanded layout (per #649 F3): consequence ↦ formula ↦ text-modification (in that order).
- Folded box shows the result; expanded section shows the formula.
- Visual layout: collapsed = one EventBox header; expanded = consequence + formula + text-mod.

## Implementation guidance

**Likely path:** `TurnResultDisplay.tsx` currently renders something like:
```tsx
<RollEventBox ... />
<IntendedMessageText ... />
<ModifierBagRollFormula ... />  {/* <-- this is in the wrong place */}
```
Move the `ModifierBagRollFormula` render to be passed as a prop or child of `RollEventBox` (inside its expanded content), and delete the sibling render.

Check `RollEventBox.tsx`'s expansion API:
- Does it already accept a `formula` slot / children? If yes, just plumb it.
- If not, add a `formula?: ReactNode` (or `breakdown?: ReactNode`) prop and render it inside the expanded fold between consequence and text-mod.

Mind the #649 F3 ordering (consequence → formula → text-mod). If #649's PR #666 already established this structure, just hook the formula slot.

## Tests required

- Update / add a snapshot or layout test in `frontend/src/components/__tests__/` confirming:
  - Collapsed state: only the EventBox header is visible; the formula is NOT in the DOM (or is `hidden`).
  - Expanded state: formula appears INSIDE the EventBox, AFTER consequence, BEFORE text-mod.
- Regression: any existing test that asserted formula-as-sibling-of-EventBox must be updated.

Run the full frontend test suite:
```bash
cd /tmp/work-652
pnpm -C frontend test --run
pnpm -C frontend exec tsc --noEmit
```

## DoD evidence + Research Log

The PR body MUST include:

```markdown
Closes #652

## DoD Evidence
- [ ] ModifierBagRollFormula moved from sibling to expanded-EventBox slot
- [ ] Expanded layout order: consequence → formula → text-mod
- [ ] No duplicate formula render
- [ ] Existing sibling-formula assertions updated
- [ ] `pnpm -C frontend test --run`: <N/N pass>
- [ ] `pnpm -C frontend exec tsc --noEmit`: clean

## Research Log
<1-2 paragraphs: where the formula was sibling-rendered, how the RollEventBox expanded-content API looked, what changed>
```

## Open PR

```bash
gh pr create --repo decay256/pinder-web --base main --head fix/652-formula-folds-under-eventbox \
  --title "fix(web#652): fold main-roll formula INSIDE success/miss EventBox" \
  --body "<DoD evidence + Research Log per template>

Closes #652"
```

Report back with the PR URL + commit SHA.

## DO-NOT list
- Do NOT touch `/root/projects/pinder-web` directly. Use the worktree.
- Do NOT merge the PR yourself.
- Do NOT push to `main`.
- Do NOT include unrelated edits (icon swaps, formatting changes, prettier sweeps). The Rung 3 review of #649 caught this and forced a revert.
- This is likely pure frontend — no `pinder-core` changes expected. If you find you need a core change, STOP and ask the orchestrator (reply in your final message) before doing the cross-repo dance.

## Log entries
Run `cd /tmp/work-652 && /root/projects/eigentakt/bin/eigentakt-mark IMPL_STARTED` at task entry.
Run `IMPL_DONE` after the PR is opened.
