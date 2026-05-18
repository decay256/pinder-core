You are a code reviewer subagent in the Pinder dev swarm. Review PR **#659** in pinder-web (companion to #943 / pinder-core PR #958 merged at `97982646`).

## Workspace
```bash
rm -rf /tmp/review-943-web
git clone --branch fix/943-roll-tier-wire-on-success \
  https://github.com/decay256/pinder-web /tmp/review-943-web
cd /tmp/review-943-web
git submodule update --init pinder-core
git log --oneline -3
git submodule status pinder-core   # MUST be 97982646
```

## Cold-start
1. Read `/root/projects/eigentakt/agents/code-reviewer.md`.
2. Read `/root/projects/pinder-web/LESSONS_LEARNED.md` if it exists.
3. Read `/root/projects/pinder-web/AGENTS.md`.
4. `gh pr view 659 --repo decay256/pinder-web --json number,title,body,additions,deletions,files,commits`.
5. `gh issue view 943 --repo decay256/pinder-core --json number,title,body,comments`.

## Context

This PR closes out #943 (web half). The core PR #958 merged at `97982646` introduced a compat shim:
- `FailureTier.Success = 0` with `[EnumMember(Value="None")]`
- `FailureTier.None = 0` `[Obsolete]` alias (same int value)
- `.ToString()` on either returns `"Success"` (first-declared member name — non-obvious trap)

The previous wire bug: `TurnAuditWriter` emitted `null` for `tier` on successful rolls because the gate was `roll.Tier == FailureTier.None ? null : roll.Tier.ToString()`.

This PR claims to:
1. Bump pinder-core submodule to `97982646`
2. Replace the 3 null-on-Success gates in `TurnAuditWriter.cs` (lines ~439, 466, 488) with `Tier = TierToWireString(roll.Tier)`
3. Add a `TierToWireString` helper using explicit switch mapping (avoids `.ToString()` trap)
4. Add regression test `Issue943_SuccessfulRollEmitsTierNone`

## Verification

### 1. Submodule pinned correctly
```bash
git submodule status pinder-core
# MUST show 97982646...
```

### 2. The 3 TurnAuditWriter sites
```bash
grep -n "TierToWireString\|FailureTier" src/Pinder.GameApi/Services/TurnAuditWriter.cs | head -30
```
Verify:
- 3 sites use `Tier = TierToWireString(...)` — no more `== FailureTier.None ? null` gates
- `TierToWireString` is a private static method using a `switch` (NOT `.ToString()`)
- The switch maps `FailureTier.Success` → `"None"` (preserves wire contract)
- Other tier values map to their C# names (Fumble, Misfire, TropeTrap, Catastrophe, Legendary)

### 3. The `.ToString()` trap — CRITICAL CHECK

Confirm `TierToWireString` does NOT use `.ToString()` anywhere. If it does:
```csharp
return tier.ToString();  // BAD — returns "Success" on int=0 not "None"
```
That's a blocker. Reviewer's prior recommendation: switch expression OR EnumMember reflection.

The acceptable fallback (`_ => tier.ToString()`) for unknown values is fine — it just shouldn't be the path for `FailureTier.Success`.

### 4. Build + tests
```bash
cd /tmp/review-943-web
dotnet build -c Release 2>&1 | tail -10
# 0 errors required
dotnet test -c Release --no-build 2>&1 | tee /tmp/web-review-test.log | tail -30
# Look for:
# - Issue943_SuccessfulRollEmitsTierNone passes
# - Pre-existing stake_llm_failed (~56 per baseline) NOT a blocker
# - No new failures introduced
```

### 5. Wire shape — sample JSON
Read the new test in `src/Pinder.GameApi.Tests/.../TurnAuditWriterTests.cs`:
```bash
grep -A 30 "Issue943_SuccessfulRollEmitsTierNone" src/Pinder.GameApi.Tests/**/TurnAuditWriterTests.cs
```
Verify the assertion is on `"tier": "None"` (NOT `"Success"`, NOT `null`).

### 6. Fixture audit — no broken fixtures
```bash
grep -rn '"tier"\s*:\s*null' src/Pinder.GameApi.Tests/ 2>&1
grep -rn '"tier"\s*:\s*"Success"' src/Pinder.GameApi.Tests/ src/Pinder.GameApi/ 2>&1
```
Both should return zero matches (or pre-existing fixtures unrelated to successful rolls — investigate any hit).

### 7. Frontend impact
```bash
grep -rn "tier === 'None'\|tier !== 'None'" /root/projects/pinder-web/frontend/src/ 2>&1 | wc -l
```
Should be ~57 references — and ALL still correct because wire still emits `"None"` for success.

### 8. No unrelated changes
```bash
git diff origin/main..HEAD --stat
```
Expected files:
- `pinder-core` (submodule)
- `src/Pinder.GameApi/Services/TurnAuditWriter.cs`
- One test file (Issue943 regression)
- Maybe a small fixture update

Flag anything else.

## Verdict criteria

- **APPROVE if:** submodule pinned correctly, 3 gates removed, `TierToWireString` exists and maps Success→"None" via switch (NOT `.ToString()`), build clean, regression test passes asserting `"tier": "None"` on success, no fixture regressions, no unrelated changes.
- **CHANGES_REQUESTED if:** any of the above fails, especially if `.ToString()` is in the success path of the new helper.

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

## Wire contract verification
- TurnAuditWriter sites still gated on null-on-Success: <count, must be 0>
- TierToWireString uses explicit switch (not .ToString()): <yes | no>
- Wire emits "tier": "None" on success: <verified | not verified>
- Frontend tier === 'None' refs preserved: <count, ~57>

## Self-verify
- Submodule pin: <97982646 confirmed | drift>
- Build: <PASS/FAIL>
- Issue943 test: <pass/fail>
- Pre-existing baseline failures: <count, vs ~56 expected>
```

Then post via `gh pr review 659 --repo decay256/pinder-web --approve --body "..."` or `--request-changes --body "..."`. If self-approve blocked, fall back to `--comment`.

## Logging
```bash
bash /root/projects/eigentakt/scripts/log.sh code-reviewer "#943-web" "PR-659-review" "started" "Cross-repo wire-contract verification: TierToWireString helper, no .ToString() trap, submodule pin 97982646"
```
After review:
```bash
bash /root/projects/eigentakt/scripts/log.sh code-reviewer "#943-web" "PR-659-review" "completed" "<verdict> posted"
```

## DO NOT
- Do not merge.
- Do not push commits.

All upstream events follow the response-style rules in USER.md — short, lead with the result, no tables.
