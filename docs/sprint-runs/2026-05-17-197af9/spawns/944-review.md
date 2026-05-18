You are a code reviewer subagent in the Pinder dev swarm. Review PR **#657** in `decay256/pinder-web` (fix for #944).

## Workspace
```bash
rm -rf /tmp/review-944
git clone --branch rung-2-sonnet-4-6/944 \
  https://github.com/decay256/pinder-web /tmp/review-944
cd /tmp/review-944
```

## Cold-start
1. Read `/root/projects/eigentakt/agents/code-reviewer.md`.
2. Read `/root/projects/pinder-web/LESSONS_LEARNED.md`.
3. Read `/root/projects/pinder-web/AGENTS.md`.
4. `gh pr view 657 --repo decay256/pinder-web --json number,title,body,additions,deletions,files`.
5. `gh issue view 944 --repo decay256/pinder-core --json number,title,body`.

## What you're reviewing

PR #657 is a P1 wire-DTO serialization fix (~75 lines added, 0 removed across 3 files):

1. `src/Pinder.GameApi/Models/TurnResultPayloadMapper.cs` — fix the read site (was reading `trap_activated` from `RollRecord` where it doesn't exist; now reads from `TurnAuditRecord` top-level).
2. `src/Pinder.GameApi/Models/TurnDtos.cs` — add a property to the wire DTO and apply `[JsonIgnoreCondition.Never]` (or equivalent attribute) so the field always serializes, including `null`.
3. `src/Pinder.GameApi.Tests/Models/TurnResultPayloadMapperTests.cs` — 3 new test cases.

## Heuristic checklist

### 1. AC coverage
- [ ] `trap_activated` key is present in JSON output regardless of whether the engine returned a trap name or `null`.
- [ ] Snapshot test: a turn with no trap → JSON contains `"trap_activated": null` (literal string check + `JsonValueKind.Null` parse check).
- [ ] Snapshot test: a turn with a trap → JSON contains `"trap_activated": "pretentious"` (or equivalent).

### 2. The serializer-config change
The PR applies an attribute to override the global `WhenWritingNull` for this one property. Verify:
- The override is **scoped to this property only** (e.g. `[JsonIgnore(Condition = JsonIgnoreCondition.Never)]` or `[JsonInclude]`), NOT a global config flip.
- The override doesn't change behavior for OTHER nullable fields in the same DTO that legitimately should be omitted when null.
- The DTO compiles and serializes correctly (Issue942 + Issue306 etc. tests still pass on the full suite).

### 3. The mapper read-site fix
Verify the corrected read pulls from the same location pinder-core writes the field. Cross-check by reading `pinder-core/src/Pinder.Core/Conversation/TurnResult.cs` (or wherever `TrapActivated` is set on the engine result) — the wire mapper's read must match the engine's write.

### 4. Test quality
- The 3 new tests must use realistic `TurnResult` / `TurnAuditRecord` fixtures, not just `null`-on-everything stubs.
- One test asserts the "trap activated" case (string in, string out).
- One test asserts the "no trap" case (null in, JSON null out).
- The "no trap" assertion must check BOTH literal substring presence (`"trap_activated":null` or `"trap_activated": null`) AND that parsing the JSON back gives `JsonValueKind.Null`. Just doing `JsonNode.Parse(...)?.TryGetPropertyValue("trap_activated", out var v)` is acceptable as long as it covers presence.

### 5. Companion concern — `TurnAuditWriter`
The implementer noted in their Research Log: "the TurnAuditWriter writer bug (also dropping trap_activated:null on write) is a separate concern." Verify this is correct — historical audit records written before this PR will lack the field, and the mapper fix needs to handle that gracefully (read returns `null` from the audit blob even when the field is absent). If the mapper instead throws or crashes on absent key, that's a BLOCKER. If the mapper degrades to null cleanly, the writer fix can ride as a follow-up.

### 6. Self-verify
```bash
cd /tmp/review-944
dotnet build -c Release 2>&1 | tail -5
dotnet test src/Pinder.GameApi.Tests/Pinder.GameApi.Tests.csproj --filter "FullyQualifiedName~TrapActivated|FullyQualifiedName~Issue944" --nologo 2>&1 | tail -5
dotnet test src/Pinder.GameApi.Tests/Pinder.GameApi.Tests.csproj --nologo 2>&1 | tail -5
```

Build: 0 errors. New tests: all pass. Full suite: same pass/fail count as main (56 baseline failures untouched; net-new tests added). Any new regression is a BLOCKER.

### 7. Scope
Only `TurnResultPayloadMapper.cs`, `TurnDtos.cs`, and the test file in the diff. Anything else is a scope-creep yellow flag.

## Output requirements

End with EXACTLY this block:

```
## Review verdict
verdict: APPROVE | CHANGES_REQUESTED
blockers: <count>
non_blocking: <count>

## Blockers
- (one per line, or "none")

## Non-blocking suggestions
- (one per line, or "none")

## Self-verify
- Build: <result>
- New tests: <pass/fail>
- Full suite: <pass/fail/skip vs baseline>

## Read-site fix correctness
- pinder-core writes TrapActivated at: <citation>
- pinder-web mapper now reads from: <citation>
- Locations match: <yes | no>
```

Then post the review via `gh pr review 657 --repo decay256/pinder-web --approve --body "..."` or `--request-changes --body "..."`. If self-approve is blocked: fall back to `--comment` with the verdict stated clearly.

## Logging
```bash
bash /root/projects/eigentakt/scripts/log.sh code-reviewer "#944" "PR-657-review" "started" "P1 wire-DTO serialization fix review"
```
```bash
bash /root/projects/eigentakt/scripts/log.sh code-reviewer "#944" "PR-657-review" "completed" "<APPROVE|CHANGES_REQUESTED> posted"
```

## DO NOT
- Do not merge.
- Do not push commits.
- Do not approve if the mapper crashes on absent-key in old audit blobs.
