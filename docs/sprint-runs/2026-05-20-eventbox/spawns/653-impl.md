You are a frontend engineer subagent fixing a P2 visual-noise bug in pinder-web.

## Workspace setup (do this FIRST)

```bash
cd /root/projects/pinder-web
git fetch origin
git worktree add /tmp/work-653 origin/main
cd /tmp/work-653
git checkout -b fix/653-intended-equals-delivered
```

Edits in `/tmp/work-653` only.

## Role spec

Read `~/.openclaw/skills/eigentakt/agents/frontend-engineer.md` for DoD-Evidence + Research-Log discipline.

## Lessons / canonical hazards

Apply: WORKSPACE-ISOLATION, REGRESSION-TESTS-ON-BUGS, DOCS-FOLLOW-CODE, NO-SCOPE-CREEP, APPROVED-WORK-IS-IMMUTABLE. Bodies in `~/.openclaw/skills/eigentakt/references/canonical-lessons.md`.

## AGENTS.md

Read `/root/projects/pinder-web/AGENTS.md`. 6-stat system (`Charm`, `Rizz`, `Honesty`, `Chaos`, `Wit`, `SelfAwareness`).

## Ticket #653 — When intended === delivered, collapse to single "intended = delivered" row

### Background
On clean-success turns (no overlays, no traps active, no text-modifying events), the SPA renders both an "Intended" box and a "Delivered" box with identical text. That's visual noise. Collapse to a single indicator.

Per #647 (cosmetic-only diffs aren't "real" modifications), the collapse should also fire when the only text_diffs are cosmetic sanitisation layers — not just when `intended_text === delivered_message` byte-for-byte.

### In scope
- `frontend/src/components/eventbox/eventVisibility.ts` (or sibling): add a helper `isDeliveredIdenticalToIntended(result: TurnResult, intendedText: string | undefined): boolean`. Logic:
  1. If `intendedText` is null/undefined/empty → return `false` (we still want the placeholder + delivered split).
  2. If `intendedText === result.delivered_message` (byte-identical) → return `true`.
  3. If all entries in `result.text_diffs` (or whatever the field is called — grep to confirm) are cosmetic-only sanitisation layers per the #647 cosmetic-set definition → return `true`. Reuse any existing #647 cosmetic-classifier if one exists; otherwise add a small one with the documented cosmetic layer names.
- `frontend/src/components/TurnResultDisplay.tsx` (around lines 460–525): when the helper returns true, render ONE combined box (`data-testid="timeline-sent-equals"`) showing the (identical) text with a label like `Sent (intended = delivered)`. When false, keep the existing two-box layout exactly.
- i18n key for the new label: add under `turn_result.timeline_sent_equals` (whichever yaml currently hosts `turn_result.timeline_intended` / `turn_result.timeline_delivered`).

### Acceptance criteria
- Clean-success turn (intended === delivered, no diffs): renders one box only.
- Cosmetic-only diff turn (per #647 definition): renders one box only.
- Any turn with real overlay diffs: renders two boxes (existing behaviour unchanged).
- Add at least 3 jsdom/RTL tests in a sibling test file: byte-identical case, cosmetic-only case, real-overlay case.
- `npm test` clean.
- `npm run build` clean.

### Out of scope
- Restyling the surviving box (use the same visual treatment, just labelled differently).
- Touching the #647 cosmetic-set definition itself; only consume it.
- Opponent message rendering (untouched).

### PR
- Repo: `decay256/pinder-web`.
- Branch: `fix/653-intended-equals-delivered`.
- Title: `fix(#653): collapse intended/delivered into one box when text is identical (or cosmetic-only diffs)`.
- Body: `Closes #653`, one-paragraph summary, list of files changed, `## DoD Evidence`, `## Research Log`.
- Open as non-draft.

### Authority
Sprint `2026-05-20-eventbox`. Self-unblock per SELF-UNBLOCK-BY-DEFAULT.

Return when PR is open with green DoD.
