You are a frontend engineer subagent adding component-gallery coverage for `RollEventBox` in pinder-web.

## Workspace setup (do this FIRST)

```bash
cd /root/projects/pinder-web
git fetch origin
git worktree add /tmp/work-612 origin/main
cd /tmp/work-612
git checkout -b chore/612-rolleventbox-gallery
```

Edits in `/tmp/work-612` only.

## Role spec

Read `~/.openclaw/skills/eigentakt/agents/frontend-engineer.md` for DoD-Evidence + Research-Log discipline.

## Lessons / canonical hazards

Apply: WORKSPACE-ISOLATION, NO-SCOPE-CREEP, DOCS-FOLLOW-CODE, REGRESSION-TESTS-ON-BUGS. Bodies in `~/.openclaw/skills/eigentakt/references/canonical-lessons.md`.

## AGENTS.md

Read `/root/projects/pinder-web/AGENTS.md`. 6-stat system (`Charm`, `Rizz`, `Honesty`, `Chaos`, `Wit`, `SelfAwareness`).

## Ticket #612 — RollEventBox component-gallery coverage

### Orchestrator-refined scope

The ticket says "if Storybook isn't wired, pick a lightweight alternative (component-gallery route)." Storybook is NOT wired. Use the existing **`TurnResultFixturePage`** pattern (`frontend/src/test-fixtures/TurnResultFixturePage.tsx`) and add a sibling `RollEventBoxGalleryPage` that renders a matrix of `RollEventBox` cases driven by a URL query param.

### In scope

**New file:** `frontend/src/test-fixtures/RollEventBoxGalleryPage.tsx`
- Mirrors the `TurnResultFixturePage` structure: `FIXTURES` map keyed by case-id, switch on `params.get('case') ?? 'index'`.
- When `case=index`: render an enumerated index of every case-id (one link per case) for visual review.
- When `case=<id>`: render the named case via `<RollEventBox {...props} />` inside a minimal layout.
- Add the route to `App.tsx` (or wherever `TurnResultFixturePage` is wired) under `/component-gallery/roll-event-box` (or analogous path — mirror what the fixture page does, even if it just adds another query-param branch).

**Matrix coverage (minimum 20 base cases):**
- 5 kinds: `option_roll`, `steering`, `horniness`, `shadow`, `shadow_growth`.
- 2 visibility modes: `box` (full collapsed EventBox), `line` (compact one-liner). Silent is invisible by definition — skip.
- 2 verdicts: `SUCCESS`, `MISS`.
- Total: 5 × 2 × 2 = 20 base cases.

**Plus variant cases (minimum 6 extra; 26 total):**
- 4 tier-suffix MISS variants on `option_roll` (Fumble, Misfire, TropeTrap, Catastrophe — pick the four representative tiers).
- 1 advantage/disadvantage rendering case (D20Pair surfaced).
- 1 zero-modifier (clean bag) vs 1 full-bag (5+ modifiers) — actually 2 extra cases here. Total = 4 + 1 + 2 = 7, so 27 cases.

Round to **27 fixtures** OR **20 if scope blows the implementer's wall-clock budget — in which case skip the variant cases and note that as a follow-up in the PR body.**

**Tests:**
- Add `RollEventBoxGalleryPage.test.tsx`: jsdom render test that loops over every `FIXTURES` key, mounts the gallery page with that case-id, and asserts (a) no thrown error, (b) at least one visible `data-testid` rendered. This is the test-half of the gallery — confirms every case at least renders.

**i18n:**
- If any new strings are needed for case labels (titles in the index page), put them in an existing yaml under a `component_gallery.*` key. Don't create a new yaml file.

### Out of scope
- Adding Storybook.
- Restyling `RollEventBox` itself.
- Backfilling fixtures for non-roll EventBox kinds (separate ticket if anyone wants it later — file a follow-up).

### Acceptance criteria
- New gallery page exists with ≥20 cases (target 27).
- New jsdom test loops every case and confirms render.
- `npm test` clean.
- `npm run build` clean.
- Index page lists every case with a link.

### PR
- Repo: `decay256/pinder-web`.
- Branch: `chore/612-rolleventbox-gallery`.
- Title: `chore(#612): RollEventBox component-gallery — N fixtures + jsdom render coverage`.
- Body: `Closes #612`, summary, case-count, list of files changed, `## DoD Evidence`, `## Research Log`.
- Open as non-draft.

### Authority
Sprint `2026-05-20-eventbox`. Self-unblock per SELF-UNBLOCK-BY-DEFAULT. If you hit a structural problem (e.g. `RollEventBox` props ambiguity for a given case), pick the most reasonable interpretation and document it in Research Log. Do not block on questions.

**Important — guard against stream-cut:** Commit and push EARLY and OFTEN. Commit after each milestone (gallery page skeleton, 5 fixtures, 10, all 20, tests added, final cleanup). If you stream-cut, the orchestrator can ship whatever you committed last.

Return when PR is open with green DoD.
