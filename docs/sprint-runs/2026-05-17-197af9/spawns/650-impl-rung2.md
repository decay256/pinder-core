You are a frontend engineer subagent in the Pinder dev swarm. Implement ticket **web#650** in pinder-web: weakness-window hit should trigger global `FoldableHintBanner`; the player should not have to expand events to find out.

## Workspace isolation
```bash
cd /root/projects/pinder-web
git fetch origin
git worktree add /tmp/work-650 origin/main
cd /tmp/work-650
git checkout -b fix/650-weakness-window-banner
git submodule update --init pinder-core
```

## Cold-start
1. Read `/root/projects/eigentakt/agents/frontend-engineer.md`.
2. Read `/root/projects/pinder-web/LESSONS_LEARNED.md`.
3. Read `/root/projects/pinder-web/AGENTS.md`.
4. `gh issue view 650 --repo decay256/pinder-web --json number,title,body,comments`.

## Diagnosis

Most of the scaffolding already exists:

- `frontend/src/components/FoldableHintBanner.tsx` (post-#593 / PR #622) — the banner component, supports kind `'tell' | 'weakness_window'`.
- `frontend/src/components/foldableHintBanner.helpers.ts` — kind types + styles.
- `frontend/src/components/OptionSelectionWidget.tsx` — currently renders the FoldableHintBanner (around lines 509–530) but ONLY during the player's decision moment, not when the weakness signal first lands.
- `frontend/src/pages/GameScreen.tsx:1049` — passes `weaknessDcReduction={turnState.weakness_dc_reduction ?? null}` down.
- `frontend/src/types.ts:462` — `weakness_dc_reduction?: number | null` on the turn state.

The signal already reaches the SPA — the banner gate logic in `OptionSelectionWidget` is the issue OR the banner needs to be hoisted up to a more global slot so it renders the moment the opponent response lands (not only when the player is selecting an option).

Run these probes:
```bash
cd /tmp/work-650
grep -rn "weakness_dc_reduction\|FoldableHintBanner\|weakness_window" frontend/src --include="*.tsx" --include="*.ts" | head -40
# Inspect the existing render gate
sed -n '440,560p' frontend/src/components/OptionSelectionWidget.tsx
# Where is the per-turn weakness signal first observable?
sed -n '1000,1100p' frontend/src/pages/GameScreen.tsx
# Find the turn-result rendering pipeline
grep -rn "TurnResultDisplay\|opponent_response" frontend/src/pages/GameScreen.tsx | head -10
```

## Acceptance criteria (from ticket)

- When a turn's opponent response generates a WEAKNESS signal that affects the next turn's DC, the `FoldableHintBanner` renders globally (above options or above the conversation) the moment the opponent response lands.
- Banner text names the stat and the DC reduction: e.g. "WEAKNESS: Honesty -3 — your next Honesty option's DC drops by 3."
- When the weakness fires (player picks the matching stat), the next turn's expanded EventBox should show the reduction in its roll formula.
- **Regression test:** feed a fixture with `weakness_dc_reduction: { stat: 'Honesty', amount: 3 }` → assert `FoldableHintBanner` is rendered.

## Implementation guidance

Two reasonable approaches — pick whichever matches existing conventions best after reading the code:

**Approach A (banner hoist, preferred):** Move the weakness-window `FoldableHintBanner` render from inside `OptionSelectionWidget` (which only mounts during option-selection) up to a higher component (e.g. `GameScreen` or `TurnResultDisplay`) so it renders the moment the opponent-response turn lands. Leave the existing `OptionSelectionWidget` tell-banner alone. Two banners can coexist — they're orthogonal.

**Approach B (gate fix):** If the banner is already hoisted but gated wrong (e.g. only renders if BOTH tell AND weakness present), fix the gate to render independently per kind.

Either way, the banner text must include both the stat name AND the DC reduction amount, in the format the ticket specifies. Reuse existing i18n keys if `pinder-core/data/i18n/en/ui.yaml` already has weakness-banner copy; if not, add a new key in a small pinder-core companion PR (same cross-repo pattern as #651/#649 — see below).

For the EventBox formula display, check `frontend/src/components/eventbox/ModifierBagRollFormula.tsx` — if weakness DC reduction isn't surfaced there already, add it as a labeled modifier line ("Weakness: -3").

## Cross-repo coordination (if needed)

If you need a new i18n key in `pinder-core/data/i18n/en/ui.yaml`:

```bash
# 1. Make the change in the submodule
cd /tmp/work-650/pinder-core
git fetch origin
git checkout -b chore/web650-weakness-banner-i18n main
# edit data/i18n/en/ui.yaml
git add -A
git commit -m "chore(web#650): add weakness-banner i18n key

Companion to pinder-web #650. Plain-language weakness-window
banner text with stat + DC reduction interpolation."
git push -u origin chore/web650-weakness-banner-i18n
gh pr create --repo decay256/pinder-core --base main --head chore/web650-weakness-banner-i18n \
  --title "chore(web#650): add weakness-banner i18n key" \
  --body "Companion to pinder-web #650. See that PR for context."
# Merge it (you have authority for this trivial i18n key add)
gh pr merge --squash --auto --repo decay256/pinder-core <PR-NUMBER>
# 2. Bump submodule in pinder-web worktree
cd /tmp/work-650
git submodule update --remote pinder-core
git add pinder-core
git commit -m "chore: bump pinder-core submodule for web#650 i18n key"
```

Skip the cross-repo dance if no new i18n key is required.

## Tests required

Add a unit test for the banner-render gate. Use the existing `FoldableHintBanner.test.ts` style + the Approach A render context. Verify the banner renders when `weakness_dc_reduction` is non-null on turn state. Regression-test naming: `it('renders weakness-window banner when weakness_dc_reduction is set', ...)`.

Run the full frontend test suite before declaring done:
```bash
cd /tmp/work-650
pnpm install --frozen-lockfile  # or check existing lockfile
pnpm -C frontend test --run
pnpm -C frontend exec tsc --noEmit
```

## DoD evidence + Research Log

The PR body MUST include:

```markdown
Closes #650

## DoD Evidence
- [ ] Banner renders globally on weakness signal (not gated behind OptionSelectionWidget mount)
- [ ] Banner text names stat + DC reduction amount
- [ ] EventBox formula surfaces the weakness DC reduction
- [ ] Regression test added
- [ ] `pnpm -C frontend test --run`: <N/N pass>
- [ ] `pnpm -C frontend exec tsc --noEmit`: clean

## Research Log
<1-2 paragraphs on what you found in OptionSelectionWidget, where the banner needed to move, what i18n key (if any) you added>
```

## Open PR

```bash
gh pr create --repo decay256/pinder-web --base main --head fix/650-weakness-window-banner \
  --title "fix(web#650): global FoldableHintBanner for weakness-window signal" \
  --body "<DoD evidence + Research Log per template>

Closes #650"
```

Report back with the PR URL + commit SHA.

## DO-NOT list
- Do NOT touch `/root/projects/pinder-web` directly. Use the worktree.
- Do NOT merge the PR yourself. The orchestrator handles merge after review.
- Do NOT push to `main`.
- Do NOT include unrelated edits in the diff (icon swaps, formatting changes, etc.). The Rung 3 review of #649 caught this and forced a force-push revert.

## Log entries
Run `cd /tmp/work-650 && /root/projects/eigentakt/bin/eigentakt-mark IMPL_STARTED` at task entry.
Run `IMPL_DONE` after the PR is opened.
