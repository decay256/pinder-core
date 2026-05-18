You are a backend engineer subagent in the Pinder dev swarm. Implement ticket **#945** in pinder-web: OfferedOption wire DTO emits `dc=null` and `modifier=null` on every option.

## Workspace isolation
```bash
cd /root/projects/pinder-web
git fetch origin
git worktree add /tmp/work-945 origin/main
cd /tmp/work-945
git checkout -b fix/945-offered-option-dc-modifier
git submodule update --init pinder-core
```

## Cold-start
1. Read `/root/projects/eigentakt/agents/backend-engineer.md`.
2. Read `/root/projects/pinder-web/LESSONS_LEARNED.md` if it exists; else skip.
3. Read `/root/projects/pinder-web/AGENTS.md` AND `/root/projects/pinder-core/AGENTS.md`.
4. `gh issue view 945 --repo decay256/pinder-core --json number,title,body,comments`.

## Diagnosis (do this first)

The ticket says:
- Every `offered_options[].dc` and `.modifier` is `null` on the wire.
- The engine clearly knows DC/modifier (resolved roll has the right values).
- Anchors: `pinder-web/src/Pinder.GameApi/Models/TurnDtos.cs` (OfferedOption DTO) or wherever options are serialized from `TurnStart.cs`.

Grep widely:
```bash
grep -rn "OfferedOption\|offered_options\|offered-options" /root/projects/pinder-web/src/ /root/projects/pinder-web/pinder-core/src/ 2>&1 | head -40
grep -rn "class OfferedOption\|record OfferedOption\|OfferedOptionDto" /root/projects/pinder-web/src/ /root/projects/pinder-web/pinder-core/src/ 2>&1 | head -20
```

Trace:
- Where does pinder-core compute the per-option DC and modifier for the options stream?
- Where does the engine produce the wire DTO for each offered option (likely `pinder-web/src/Pinder.GameApi/Models/`)?
- Why is the mapping dropping `dc` and `modifier`? Common causes:
  - The mapper takes `option.RollFormula` but reads only `.Stat` and `.IntendedText`, skipping `.Dc` and `.Modifier`.
  - The pinder-core `Option` type has a separate `RollFormula` with DC/Modifier but the wire mapper hardcodes them to null.
  - The pinder-core stream emits DC/modifier only after the option is picked (in `OnOptionPicked`) and the prefetch / streaming preview emits a null-padded version.

Capture findings in the `Diagnostic findings` section.

## Goal

Make the wire `offered_options[*].dc` and `.modifier` carry the real values pinder-core computes pre-pick.

### Fix paths (pick whichever the code shape calls for):

**A. Mapper-only fix.** If the pinder-core `Option` already exposes `Dc` and `Modifier` (or a `RollFormula` with them) and the pinder-web mapper just doesn't read them: update the mapper to read+emit. No pinder-core change.

**B. Engine fix.** If the pinder-core option payload doesn't carry DC/Modifier pre-pick because the engine only computes them at resolve time: a deeper engine fix — emit the same `RollCheck.Dc` + `RollCheck.StatModifier` that the resolve path computes. This may need a small pinder-core PR.

**C. Streaming layout.** If the SSE option-stream chunks emit a partial schema and only the final option carries DC/modifier: fix the SSE chunk producer to include them on every chunk.

Pick whichever path the code shows is the minimal change. **Strongly prefer Mapper-only (A)** unless evidence shows the engine doesn't carry the data.

## Acceptance criteria

- Wire DTO emits non-null `dc` (int) and `modifier` (int) on every offered option mapped from a normal RollCheck.
- Snapshot test in `src/Pinder.GameApi.Tests/`: assert `offered_options[].dc != null && offered_options[].modifier != null` on a fixture that triggers options.
- A vitest case in `frontend/src/components/eventbox/ModifierBagRollFormula.test.tsx` (if such file exists) — assert ModifierBagRollFormula renders DC + total formula from a fixture with non-null DC. **If the frontend test infrastructure exists, add it; if not, skip and note.**

## Tests
- New test file or augmented existing test in `src/Pinder.GameApi.Tests/`:
  `Issue945_OfferedOptionDcModifierTests.cs` — asserts wire shape on a representative turn.
- If frontend test infra is available: add a vitest case under `frontend/src/components/eventbox/`.

## Co-located mirror tests rule

Per EIGENTAKT-COLOCATED-MIRROR-TESTS-IN-SCOPE, co-located mirror tests (e.g. `*.test.tsx` next to `*.tsx`) are part of the source change in scope.

## Build evidence

```bash
cd /tmp/work-945

# Build
dotnet build -c Release 2>&1 | tee /tmp/945-build.log | tail -10

# Test
dotnet test -c Release --no-build 2>&1 | tee /tmp/945-test.log | tail -30
# Issue945 tests MUST pass; baseline pre-existing failures (~56 stake_llm_failed) unchanged.

# Frontend (if changes there)
if [ -d frontend ] && [ -f frontend/package.json ]; then
  cd frontend
  npm test -- --run 2>&1 | tee /tmp/945-fe-test.log | tail -20
  cd ..
fi
```

## Commit + push

Explicit pathspecs:
```bash
git add <each file you touched explicitly>
git status   # verify
git commit -m "fix(#945): emit dc and modifier on offered options wire DTO

<one-line summary of which fix path you took>

Acceptance:
- offered_options[*].dc and .modifier now carry the engine-computed values
- Snapshot test asserts non-null DC and modifier on representative turn
- ModifierBagRollFormula can now render pre-pick formula correctly"
git push origin fix/945-offered-option-dc-modifier
```

## PR

```bash
gh pr create --repo decay256/pinder-web --base main --head fix/945-offered-option-dc-modifier \
  --title "fix(#945): emit dc and modifier on offered options wire DTO" \
  --body "Closes #945.

## Summary
<which fix path; specific files touched>

## DoD evidence
\`\`\`
$(tail -5 /tmp/945-build.log)
$(tail -5 /tmp/945-test.log)
\`\`\`

## Wire shape before/after
**Before**: \`{ \"stat\": \"Wit\", \"dc\": null, \"modifier\": null, ... }\`
**After**:  \`{ \"stat\": \"Wit\", \"dc\": 21, \"modifier\": 4, ... }\`"
```

## Workflow rules
- Do NOT merge.
- Do NOT touch pinder-core unless the diagnostic shows engine-side data is missing (path B). If you do need pinder-core, that's a separate PR; STOP and report.
- Pathspec discipline only — no `git add -A`.

## Logging
```bash
bash /root/projects/eigentakt/scripts/log.sh backend-engineer "#945" "offered-option-dc-modifier" "started" "Wire DC + modifier on offered options"
```
After PR:
```bash
bash /root/projects/eigentakt/scripts/log.sh backend-engineer "#945" "offered-option-dc-modifier" "completed" "PR #N opened" "<sha>"
```

## Output requirements

End with:
- `## Diagnostic findings` — which DTO mapper site dropped DC/modifier, code citations.
- `## Implementation summary` — fix path taken (A/B/C), files touched, why.
- `## DoD Evidence` — PR URL, build tail, test tail, JSON before/after.
- `## Research Log` — what you read, what you grep'd.
- `## Filed follow-ups` — none expected.

All upstream events follow the response-style rules in USER.md — short, lead with the result, no tables.
