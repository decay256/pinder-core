You are a backend engineer subagent in the Pinder dev swarm. Implement the **pinder-web companion PR for #943** — bump the pinder-core submodule to `97982646` and fix the real wire bug in `TurnAuditWriter`.

## Workspace isolation

```bash
cd /root/projects/pinder-web
git fetch origin
git worktree list | grep work-943-web || git worktree add /tmp/work-943-web origin/main
cd /tmp/work-943-web
git checkout -b fix/943-roll-tier-wire-on-success
git status   # clean
```

## Cold-start
1. Read `/root/projects/eigentakt/agents/backend-engineer.md`.
2. Read `/root/projects/pinder-web/LESSONS_LEARNED.md` if it exists; else skip.
3. Read `/root/projects/pinder-web/AGENTS.md`.
4. `gh issue view 943 --repo decay256/pinder-core --json number,title,body,comments`.
5. `gh pr view 958 --repo decay256/pinder-core --json reviews,mergeCommit` — read the merged compat-shim approach to understand what pinder-core now exposes.

## Background — what pinder-core changed (merged at `97982646`)

- `FailureTier.Success = 0` is the new canonical success value with `[EnumMember(Value="None")]`.
- `FailureTier.None = 0` exists as `[Obsolete]` alias (same int value).
- `.ToString()` on either returns `"Success"` (first declared C# member name — this is a TRAP).
- pinder-core's own JsonSerializer emits `"Tier":0` (int) because Tier has no JsonStringEnumConverter.
- pinder-web's `TurnAuditWriter` currently does:
  ```csharp
  Tier = roll.Tier == FailureTier.None ? null : roll.Tier.ToString(),
  ```
  After submodule bump this STILL compiles (None alias still exists), so existing tests still pass and frontend still sees `null` on success — meaning the bug is unfixed unless this PR changes the logic.

## What this PR must do

### 1. Bump pinder-core submodule
```bash
cd /tmp/work-943-web
git submodule update --init pinder-core
cd pinder-core
git fetch origin
git checkout 97982646f0a6b582bb6a5595732d32706260b082
cd ..
git add pinder-core
git status  # confirm submodule update staged
```

### 2. Fix `TurnAuditWriter.cs` (production wire serializer)

File: `src/Pinder.GameApi/Audit/TurnAuditWriter.cs` (path may differ — locate via `grep -rn "FailureTier.None ? null" src/`).

The three sites (lines ~439, 466, 488 per reviewer) currently emit `null` for success. Change them to **always emit the wire string**, mapping the enum to the `[EnumMember]` attribute value.

**Critical**: do NOT use `.ToString()` — it returns `"Success"` (first-declared C# member), but the wire/frontend contract is `"None"`. Use one of:

**Option A (preferred — explicit string mapping):**
```csharp
private static string TierToWireString(FailureTier tier) => tier switch
{
    FailureTier.Success => "None",
    FailureTier.Fumble => "Fumble",
    FailureTier.Misfire => "Misfire",
    FailureTier.TropeTrap => "TropeTrap",
    FailureTier.Catastrophe => "Catastrophe",
    FailureTier.Legendary => "Legendary",
    _ => tier.ToString()
};

// Then at the three sites:
Tier = TierToWireString(roll.Tier),    // no longer null on success
```

**Option B (EnumMember reflection lookup):**
```csharp
private static string TierToWireString(FailureTier tier)
{
    var member = typeof(FailureTier).GetField(tier.ToString());
    var attr = member?.GetCustomAttribute<EnumMemberAttribute>();
    return attr?.Value ?? tier.ToString();
}
```

Option A is simpler and faster; prefer it unless code-style elsewhere in the file does reflection.

**The three call sites must change from:**
```csharp
Tier = roll.Tier == FailureTier.None ? null : roll.Tier.ToString(),
```
**to:**
```csharp
Tier = TierToWireString(roll.Tier),
```

This removes the null-on-success gate and emits `"None"` for success, `"Fumble"` for Fumble, etc.

### 3. Verify wire DTO `Tier` field is non-nullable in the output schema

Look at `TurnDtos.cs` or wherever the audit-event DTO is defined. If `Tier` is `string?`, fine. If it has `[JsonIgnore(Condition=WhenWritingNull)]` or similar, no change needed — but verify the wire shape on success is `"tier": "None"`, not absent.

### 4. Tests

Add regression test in `src/Pinder.GameApi.Tests/Audit/TurnAuditWriterTests.cs` (or wherever existing TurnAuditWriter tests live — `grep -rn "TurnAuditWriter" src/Pinder.GameApi.Tests/`):

```csharp
[Fact]
public void Issue943_SuccessfulRollEmitsTierNone()
{
    // Arrange: build a roll with IsSuccess=true (Tier=FailureTier.Success)
    var roll = new RollResult { /* setup */, Tier = FailureTier.Success, IsSuccess = true };

    // Act: serialize via TurnAuditWriter path

    // Assert: emitted JSON contains "tier": "None" (NOT null, NOT "Success")
}
```

Co-located mirror tests are in scope per EIGENTAKT-COLOCATED-MIRROR-TESTS-IN-SCOPE.

### 5. Frontend handling — no changes required

The wire still emits `"None"` for success (via the explicit mapping). The 57 frontend `tier === 'None'` references continue to work correctly because:
- `collapsedHeader.ts:67` `tier === 'None' ? 'positive' : 'negative'` — success now arrives as `"None"`, gets `'positive'` ✓
- `rollAdapters.ts:224` `dto.tier === 'None' ? 'SUCCESS' : 'MISS'` — success → `'SUCCESS'` ✓
- All others similarly preserved.

**No frontend code changes needed in this PR.** Document this in the PR body.

### 6. Snapshot/fixture audit

If any test fixture has `"tier": null` for a successful roll, update it to `"tier": "None"`. Run:
```bash
grep -rn '"tier"\s*:\s*null' src/Pinder.GameApi.Tests/ 2>&1
```
Update any fixture that represents a successful roll. Leave fixtures unrelated to success-tier alone.

## Build evidence (MANDATORY)

```bash
cd /tmp/work-943-web

# Submodule sanity
git submodule status pinder-core   # MUST show 97982646...

# Full build
dotnet build -c Release 2>&1 | tee /tmp/943-web-build.log | tail -10
# 0 errors required.

# Full test suite
dotnet test -c Release --no-build 2>&1 | tee /tmp/943-web-test.log | tail -30
# Compare to baseline. Issue943 new test MUST pass.
# Look for any pre-existing test that broke due to changed null-on-success behavior — these need fixture updates (acceptable as part of this PR).

# Frontend build (if pinder-web has a frontend build)
if [ -f frontend/package.json ]; then
  cd frontend
  npm run build 2>&1 | tee /tmp/943-web-fe-build.log | tail -10
  npm test -- --run 2>&1 | tee /tmp/943-web-fe-test.log | tail -20
  cd ..
fi
```

## Commit + push

Explicit pathspecs only:
```bash
git add pinder-core   # submodule bump
git add src/Pinder.GameApi/Audit/TurnAuditWriter.cs    # or wherever it lives
git add src/Pinder.GameApi.Tests/Audit/Issue943_*.cs   # new test
# Any fixture updates, explicitly listed
git status   # verify
git commit -m "fix(#943): emit tier on successful rolls in TurnAuditWriter

Companion to pinder-core PR #958 (merged at 97982646).

- Bump pinder-core submodule to 97982646
- TurnAuditWriter no longer emits null for FailureTier.Success — uses
  explicit TierToWireString mapping so the wire emits \"None\" for success
  (preserves frontend tier === 'None' semantics in 57 production+test sites)
- Avoids .ToString() trap (returns \"Success\" not \"None\" due to first-declared
  enum member ordering in pinder-core's compat shim)
- New regression test asserts \"tier\": \"None\" on successful roll wire emit
- No frontend changes required — wire string remains \"None\" for success"
git push origin fix/943-roll-tier-wire-on-success
```

## PR

```bash
gh pr create --repo decay256/pinder-web --base main --head fix/943-roll-tier-wire-on-success \
  --title "fix(#943): emit tier on successful rolls (web wire fix + submodule bump)" \
  --body "Closes #943 (web half; pinder-core PR #958 merged at 97982646).

## Summary
- Bump pinder-core submodule to 97982646 (FailureTier.Success + EnumMember(\"None\") compat shim).
- TurnAuditWriter now emits \`\"tier\": \"None\"\` on successful rolls instead of \`null\`.
- Explicit TierToWireString mapping avoids \`.ToString()\` returning \`\"Success\"\` (first-declared member trap).
- Frontend unchanged — wire string \`\"None\"\` for success preserves all 57 \`tier === 'None'\` checks.

## DoD evidence
\`\`\`
$(tail -5 /tmp/943-web-build.log)
$(tail -5 /tmp/943-web-test.log)
\`\`\`

## Risk
Fixture updates where applicable (listed in diff). Frontend cause-effect: zero — wire contract preserved via EnumMember + explicit map."
```

## Workflow rules
- Do NOT merge. Orchestrator merges after reviewer approval.
- Do NOT modify the C# enum in pinder-core submodule — read-only.
- If `dotnet test` surfaces pre-existing failures unrelated to this change, document them in the PR body as out-of-scope (per #953 pattern).

## Logging
```bash
bash /root/projects/eigentakt/scripts/log.sh backend-engineer "#943" "web-wire-on-success" "started" "Pinder-web companion: submodule bump + TurnAuditWriter null-on-Success fix + explicit tier-to-wire mapping"
```
After PR open:
```bash
bash /root/projects/eigentakt/scripts/log.sh backend-engineer "#943" "web-wire-on-success" "completed" "PR opened, awaiting orchestrator review" "<sha>"
```

## Output requirements

End with:
- `## Diagnostic findings` — exact TurnAuditWriter file path, line numbers of the 3 sites, the mapping helper location.
- `## Implementation summary` — submodule bump sha, files touched, fixture updates, new test name.
- `## DoD Evidence` — PR URL, build tail (0 errors), test tail (Issue943 pass, baseline preserved), sample JSON showing `"tier": "None"` on success.
- `## Research Log` — what you read, what you grep'd.
- `## Filed follow-ups` — none expected; flag if anything weird.

All upstream events follow the response-style rules in USER.md — short, lead with the result, no tables.
