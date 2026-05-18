You are a code reviewer subagent in the Pinder dev swarm. Review pinder-web PR **#666** (fix for #649 — expanded EventBox: consequence + roll breakdown + diff).

## Workspace isolation
```bash
rm -rf /tmp/review-649
git clone --branch fix/649-eventbox-expanded-consequence \
  https://github.com/decay256/pinder-web /tmp/review-649
cd /tmp/review-649
git submodule update --init pinder-core
```

## Cold-start
1. Read `/root/projects/eigentakt/agents/code-reviewer.md`.
2. Read `/root/projects/pinder-web/LESSONS_LEARNED.md`.
3. Read `/root/projects/pinder-web/AGENTS.md`.
4. `gh pr view 666 --repo decay256/pinder-web --json title,body,additions,deletions,files`.
5. `gh issue view 649 --repo decay256/pinder-web --json number,title,body`.

## What you're reviewing

PR #666 is +340/-14 across 7 files + submodule bump:

1. `pinder-core` submodule pointer bumped to `1a2a84e` (which contains PR #963 merging 17 consequence i18n keys to `pinder-core/data/i18n/en/consequences.yaml`).
2. `frontend/src/components/eventbox/consequenceText.ts` — pure `resolveConsequence({kind, tier, shadowName, stat, trapName})` helper, returns typed i18n string or null.
3. `frontend/src/components/eventbox/consequenceText.test.ts` — 17 unit tests.
4. `frontend/src/components/eventbox/RollEventBox.tsx` — restructured expanded body: consequence section → formula → diff.
5. `frontend/src/components/TurnResultDisplay.tsx` — wires `plainConsequence` through to 4 callers + trap_activated.
6. `frontend/src/components/eventbox/rollTypes.ts` — minor type additions for the new prop.
7. `frontend/src/components/eventbox/index.ts` — exports.
8. `frontend/src/data/displayNames.guardrail.test.ts` — allowlist additions for `consequenceText.ts/test.ts`.

## Heuristic checklist

### 1. AC coverage
- [ ] **4 ticket-body fixtures**, each pinned by a test:
  - Turn 1 shadow miss (Dread) → consequence text contains "Dread shadow took over".
  - Turn 1 horniness miss → "Horniness misfired catastrophically".
  - Turn 1 trap "pretentious" → "Pretentious trap".
  - Turn 3 roll pass (Honesty) → "Honesty roll landed" or equivalent.
- [ ] **Three-section render order**: consequence (section 1) → roll/formula breakdown (section 2) → text modification (section 3). Each section conditionally hidden when its data is missing.
- [ ] **All 6 shadow names** (Dread/Denial/Fixation/Madness/Despair/Overthinking) have corresponding i18n keys + tests.

### 2. Correctness
- [ ] `resolveConsequence` returns `null` when no key matches — caller hides the section. Verify no exceptions thrown on unknown kinds.
- [ ] Slot substitution: `{stat}` / `{trap_name}` / `{shadow_name}` placeholders resolved before render. Verify with one of the tests.
- [ ] Consequence text uses the **typed StringKey union** (the same enforcement that caught #651's missing key). If yes, the build TS-checks the keys; if no, missing keys would only surface at runtime.
- [ ] The `consequences.yaml` file in pinder-core is loaded into the SPA's i18n bundle. Check `frontend/scripts/build-i18n.mjs` or wherever i18n is compiled — it should pick up the new file automatically OR the implementer added an explicit reference.

### 3. Don't-break checks
- [ ] No regression in `RollEventBox` for events without a consequence i18n key — they still render formula + diff as before.
- [ ] `TurnResultDisplay` callers all pass `plainConsequence` — none silently lose the prop on the path between the component and the data fetch.
- [ ] Submodule bump: `pinder-core` sha matches PR #963's merge commit (`1a2a84e`).

### 4. Build + tests
```bash
cd /tmp/review-649/frontend
npm install --silent 2>&1 | tail -3
npm test -- consequenceText eventbox 2>&1 | tail -15
npm run build 2>&1 | tail -10
```
- [ ] Frontend tests: green (implementer claims 961/961).
- [ ] Build: only pre-existing #663 TS error.

### 5. PR hygiene
- [ ] `Closes #649` in PR body.
- [ ] Follow-up issue #964 (engine-side Path A) referenced.
- [ ] Commit message describes both repos.

## Verdict

`APPROVE` if all checks pass. `CHANGES_REQUESTED` with specific blockers otherwise.

```bash
gh pr review 666 --repo decay256/pinder-web --approve -b "<body>"
# OR
gh pr review 666 --repo decay256/pinder-web --request-changes -b "<body>"
```

## DoD evidence

```
Stats: tokens_in=<N> tokens_out=<N> tokens_cache_read=<N or n/a> tokens_cache_write=<N or n/a> wall_clock_seconds=<N>
verdict: APPROVE   (or CHANGES_REQUESTED — N blockers)
```

## Reminders

Correlation id: `2026-05-17-197af9-649-code-reviewer-<your-id>`.
Sprint id: `2026-05-17-197af9`.

Per the response-style rules in `/root/.openclaw/agents-extra/pinder/USER.md`: short, lead with the verdict, no markdown tables, max 5 bullets per section.
