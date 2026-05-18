You are a code reviewer subagent in the Pinder dev swarm. Review PR **#660** in pinder-web (fix for #945).

## Workspace
```bash
rm -rf /tmp/review-945
git clone --branch fix/945-offered-option-dc-modifier \
  https://github.com/decay256/pinder-web /tmp/review-945
cd /tmp/review-945
git submodule update --init pinder-core
```

## Cold-start
1. Read `/root/projects/eigentakt/agents/code-reviewer.md`.
2. Read `/root/projects/pinder-web/AGENTS.md`.
3. `gh pr view 660 --repo decay256/pinder-web --json number,title,body,additions,deletions,files,commits`.
4. `gh issue view 945 --repo decay256/pinder-core --json number,title,body,comments`.

## What you're reviewing

PR #660 claims to fix #945 (`offered_options[].dc = null` and `.modifier = null` on every option). The implementer's diagnostic is:

> The fix is a **rename `stat_modifier` → `modifier`** in the wire DTO. The DC field was already computed correctly in `BuildTurnState`. The staging reviewer interpreted the "absent" `modifier` key (it was named `stat_modifier`) as null.

This means the ticket's "dc=null" symptom is **different from** the "modifier=null" symptom — the implementer is claiming only the modifier-rename is needed, and DC is already non-null.

## Verification (CRITICAL — verify the claim)

### 1. The dc=null claim
The ticket body explicitly shows:
```json
{ "stat": "Wit", "dc": null, "modifier": null, "intended_text": "..." }
```

Both `dc` and `modifier` are `null` per the staging report. If the fix is rename-only, **DC must have been non-null already** in the pre-fix wire. Check:

```bash
cd /tmp/review-945
grep -n "Dc\|StatModifier\|Modifier" src/Pinder.GameApi/Models/TurnDtos.cs
```

Find the DialogueOptionDto definition. Look at the property names and `[JsonPropertyName]` attributes BEFORE the fix (compare to `origin/main`):
```bash
git diff origin/main -- src/Pinder.GameApi/Models/TurnDtos.cs
```

Find:
- What was the `Dc` property's wire key BEFORE the PR? (likely `dc`)
- Was it actually populated by `BuildTurnState` on the controller's path?
- Was the staging session running an OLDER build where Dc wasn't yet populated?

Look at git blame on the `BuildTurnState` method to see when `defStats.GetDefenceDC()` was added:
```bash
git log --oneline -- src/Pinder.GameApi/Controllers/SessionsController.cs | head -10
git blame src/Pinder.GameApi/Controllers/SessionsController.cs | grep -i "DefenceDC\|defStats" | head -10
```

**If DC was already being populated before the PR and only `stat_modifier`→`modifier` rename is the fix:** the ticket reporter was looking at the AUDIT NDJSON (which deliberately has a slim shape per #466 privacy contract — no dc, no modifier), NOT the GET `/sessions/{id}/turn` response. That's a confusion of two different wire surfaces. The PR fix is correct but doesn't actually address the audit-NDJSON case (which is by design slim).

**If DC was NOT being populated before:** the rename alone isn't enough — DC needs to also be added to the audit NDJSON, OR the privacy contract from #466 needs to be reconsidered. That's a blocker.

### 2. The two wire surfaces
The pinder-web `OfferedOption` exists in two wire shapes:
- **Audit NDJSON** (write-only, persisted to `pinder_staging.turn_records`): slim shape per #466.
- **Live GET `/sessions/{id}/turn`** (read for SPA): full shape with DC + modifier.

Check both:
```bash
grep -rn "offered_options\|OfferedOptionDto\|DialogueOptionDto" src/Pinder.GameApi/ 2>&1 | head -30
```

Trace where each shape is emitted. The fix should target the surface the SPA reads — the live GET response, which `ModifierBagRollFormula` consumes.

### 3. Frontend consumer
```bash
grep -rn "modifier\|stat_modifier" frontend/src/components/eventbox/ModifierBagRollFormula.tsx frontend/src/types.ts 2>&1 | head -20
```

Verify the frontend now reads `modifier` (not `stat_modifier`). Confirm the rename is consistent across ALL TS consumers.

### 4. Audit NDJSON impact
**Critical**: does renaming `stat_modifier` → `modifier` in `DialogueOptionDto` accidentally leak modifier into the audit NDJSON (which #466 deliberately keeps slim)?

```bash
grep -rn "TurnAuditWriter\|audit.*Option\|JsonIgnore" src/Pinder.GameApi/Services/TurnAuditWriter.cs src/Pinder.GameApi/Audit/ 2>&1 | head -20
```

If `DialogueOptionDto` is reused for audit serialization, the rename may leak data. If audit uses a DIFFERENT type (slim), no leak. Verify.

### 5. Tests
Read `src/Pinder.GameApi.Tests/Services/Issue945_OfferedOptionDcModifierTests.cs`. Are the assertions:
- "DC is non-null on each offered option" ✓
- "modifier is non-null on each offered option" ✓
- "wire key is `modifier` not `stat_modifier`" ✓

Run:
```bash
dotnet build -c Release 2>&1 | tail -5
dotnet test -c Release --no-build --filter "FullyQualifiedName~Issue945" --no-build 2>&1 | tail -10
```

### 6. Frontend tests
```bash
cd frontend
npm test -- --run -- ModifierBagRollFormula 2>&1 | tail -20
```

### 7. Fixture updates
```bash
git diff origin/main -- 'frontend/src/**/*.test.ts' 'frontend/src/**/*.test.tsx' 'src/Pinder.GameApi.Tests/' | head -100
```
Confirm any fixture updates are mechanical (`stat_modifier` → `modifier`), not semantic regressions.

### 8. Out-of-scope
```bash
git diff origin/main..HEAD --stat
```
Expected files:
- `src/Pinder.GameApi/Models/TurnDtos.cs`
- `frontend/src/types.ts`
- A few TS consumers
- New test file + fixture updates

Flag any unrelated.

## Verdict criteria

- **APPROVE if:** the implementer's diagnostic is correct (rename addresses the live GET response; staging reporter conflated audit-NDJSON with live response; DC was already non-null pre-PR), build clean, tests pass, no audit-NDJSON leak.
- **CHANGES_REQUESTED if:** DC was also null pre-PR (rename alone insufficient), audit-NDJSON now leaks modifier (privacy contract regression), or any test/build failure.

## Output requirements

End with EXACTLY:

```
## Review verdict
verdict: APPROVE | CHANGES_REQUESTED
blockers: <count>
non_blocking: <count>

## Blockers
- (one per line, or "none")

## Non-blocking suggestions
- (one per line, or "none")

## Diagnostic verification
- DC pre-PR populated in live GET: <yes | no | confirmed via grep + blame>
- stat_modifier → modifier rename consistent across TS+CS: <yes | no>
- Audit NDJSON shape unchanged (privacy #466 preserved): <yes | no | n/a>
- ModifierBagRollFormula consumes `modifier` correctly: <yes | no>

## Self-verify
- Build: <PASS/FAIL>
- Issue945 tests: <pass count / total>
- ModifierBagRollFormula vitest: <pass count / total>
- Pre-existing baseline failures: <count, vs ~56 expected>
```

Then post via `gh pr review 660 --repo decay256/pinder-web --approve --body "..."` or `--request-changes --body "..."`. If self-approve blocked, fall back to `--comment`.

## Logging
```bash
bash /root/projects/eigentakt/scripts/log.sh code-reviewer "#945" "PR-660-review" "started" "Verify rename addresses live-GET (not audit NDJSON); confirm DC was already non-null pre-PR"
```
After review:
```bash
bash /root/projects/eigentakt/scripts/log.sh code-reviewer "#945" "PR-660-review" "completed" "<verdict> posted"
```

## DO NOT
- Do not merge.
- Do not push commits.

All upstream events follow the response-style rules in USER.md — short, lead with the result, no tables.
