You are a code reviewer subagent in the Pinder dev swarm. Review PR **#958** (fix for #943) in pinder-core.

## Workspace
```bash
rm -rf /tmp/review-943
git clone --branch fix/943-roll-tier-success-value \
  https://github.com/decay256/pinder-core /tmp/review-943
cd /tmp/review-943
```

## Cold-start
1. Read `/root/projects/eigentakt/agents/code-reviewer.md`.
2. Read `/root/projects/pinder-core/LESSONS_LEARNED.md` — focus on APPROVED-WORK-IS-IMMUTABLE, EIGENTAKT-DEAD-CODE-NEEDS-CROSS-REPO-GREP, BUILD-PIPELINE-DISCIPLINE.
3. Read `/root/projects/pinder-core/AGENTS.md`.
4. `gh pr view 958 --repo decay256/pinder-core --json number,title,body,additions,deletions,files,commits`.
5. `gh issue view 943 --repo decay256/pinder-core --json number,title,body,comments`.

## What you're reviewing

PR #958 fixes a P1 wire bug: `roll.tier` was absent on successful rolls because `FailureTier.None` (index 0) was serialized as the default-omitted value.

**THE IMPLEMENTER MADE A LOAD-BEARING DESIGN CHOICE — your job is to evaluate it carefully:**

The implementer **RENAMED `FailureTier.None` → `FailureTier.Success`** across 44 files in pinder-core (8 src files, 36 test files) instead of **ADDING** `Success` as a new enum value alongside the existing `None`.

The ticket explicitly offered both options:
> "`RollResult` / `RollCheckResult` carries `Tier` even when `IsSuccess=true` (e.g. a `Success` enum value, or repurpose the existing `OneShot` / `Tier-0` value)."

So the rename approach is technically permissible. BUT — your most important job is to check the **cross-repo blast radius**.

## The cross-repo blast radius check (THE LOAD-BEARING REVIEW STEP)

The pinder-web repo lives at `/root/projects/pinder-web/` on this host. Run these greps:

```bash
grep -rn 'FailureTier\.None\b' /root/projects/pinder-web/src/ /root/projects/pinder-web/frontend/src/ 2>/dev/null
grep -rn '"tier"\s*:\s*"None"' /root/projects/pinder-web/src/ /root/projects/pinder-web/frontend/src/ /root/projects/pinder-web/contracts/ 2>/dev/null
grep -rn "'None'\|\"None\"" /root/projects/pinder-web/frontend/src/ 2>/dev/null | grep -i tier
```

**Expected results (per the orchestrator's pre-check):**
- ~16 pinder-web .cs files reference `FailureTier.None` by symbol name.
- Multiple fixtures contain literal `"tier": "None"` strings (Issue518, ReplayPrivacyTests, TurnResultPayloadMapperTests).

**If those references exist:** this PR is a **BLOCKER**. Once pinder-web bumps its submodule to this PR's sha, the build will fail at compile-time on the `FailureTier.None` references, and any test that asserts the literal string `"None"` will fail at runtime.

**Two acceptable fix paths the implementer can take:**
1. **Preserve wire compat via `[EnumMember(Value="None")]`** on the renamed `Success` value. Then `enum.ToString()` still emits `"None"` and existing pinder-web string-fixtures keep passing. Also keep `FailureTier.None` as `[Obsolete] public const FailureTier None = FailureTier.Success;` style alias so symbol references in pinder-web still compile.
2. **ADD `Success` as a NEW enum value at index 0** and **keep `None` at a higher index** (or add a `[Obsolete]` alias). New code emits `Success` on success; pinder-web's `FailureTier.None` symbol references still compile against an existing (perhaps-deprecated) enum value.

If the implementer wants to keep the pure rename, then this PR must ship lockstep with a pinder-web PR that updates all 16+ references AND all literal-string fixtures, AND there must be a coordinated migration for any persisted audit records in pinder_staging DB. That's a much bigger lift than the ticket scoped for.

## Heuristic checklist (apply in order)

### 1. AC coverage from issue #943
- [ ] `RollResult` / `RollCheckResult` carries `Tier` even when `IsSuccess=true`.
- [ ] Wire DTO emits `tier` on every roll (verify this by examining the `RollResult` -> JSON path; if `JsonStringEnumConverter` is used and the enum value is now named `Success`, the wire string is now `"Success"` not `"None"`).
- [ ] Snapshot test asserts `tier` key present on success.
- [ ] Failed roll behavior preserved.

### 2. Cross-repo blast radius (per above) — THE LOAD-BEARING CHECK

Run the greps. Count references. If pinder-web references `FailureTier.None` by symbol name OR has fixtures with `"tier": "None"`, this PR is **BLOCKED** until either:
- The implementer changes approach to preserve wire/symbol compat, OR
- A coordinated pinder-web PR is opened alongside this one.

Note: a parallel pinder-web PR is the LARGER lift and contradicts the orchestrator's plan of bumping the submodule. Strongly prefer the preserve-compat path.

### 3. Wire serialization
What does the `tier` field literally serialize to on success now? `enum.ToString()` gives `"Success"`. If pinder-web's SPA does `tier === "None"` ANYWHERE, it now silently fails (TS) or returns a different render path (JS).

Check:
- `grep -rn '"None"\|tier.*None\|tier.*==.*None\|tier === "None"' /root/projects/pinder-web/frontend/src/`

### 4. Pinder-core internal damage
Run `dotnet build -c Release` and `dotnet test --no-build` in `/tmp/review-943` and confirm pinder-core itself compiles + tests pass (the implementer reported 0 errors / tests pass).

### 5. The new tests
Read `tests/Pinder.Core.Tests/Issue943_RollTierOnSuccessTests.cs`. Are the assertions:
- "successful rolls carry `FailureTier.Success`" — checks the symbol, doesn't catch wire issue.
- "successful rolls serialize the `Tier` property" — does it check the literal string value? If it asserts `"Success"`, the PR encodes the rename-breaks-wire. If it just asserts presence, the wire issue is silent.

### 6. Audit DB migration
Persisted `turn_records` in pinder_staging have `"tier": "None"` for past successful rolls (well, they didn't — that was the bug — but they have `"None"` for any roll where None was used elsewhere, and they have other tier names too). Does this PR migrate the old data? Probably not — the rename is forward-only. Document this in your verdict if you find it.

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

## Cross-repo blast radius
- pinder-web FailureTier.None symbol references: <count>
- pinder-web "tier":"None" fixture string references: <count>
- pinder-web frontend tier === "None" references: <count>
- Breaks pinder-web build at submodule bump: <yes | no>

## Self-verify (pinder-core only)
- Build: <result>
- Issue943 tests: <pass/fail>
- Full suite: <pass/fail/skip>
```

Then post the review via `gh pr review 958 --repo decay256/pinder-core --approve --body "..."` or `--request-changes --body "..."`. If self-approve is blocked: fall back to `--comment` with verdict stated.

## Logging
```bash
bash /root/projects/eigentakt/scripts/log.sh code-reviewer "#943" "PR-958-review" "started" "Cross-repo blast-radius focus; rename-vs-add evaluation"
```
After review:
```bash
bash /root/projects/eigentakt/scripts/log.sh code-reviewer "#943" "PR-958-review" "completed" "<verdict> posted"
```

## DO NOT
- Do not merge.
- Do not push commits.
- Do not approve if the cross-repo blast radius check identifies any pinder-web symbol or string reference that would break.
