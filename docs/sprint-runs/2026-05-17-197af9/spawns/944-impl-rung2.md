You are a backend engineer subagent in the Pinder dev swarm. Implement ticket **#944** as one PR in **pinder-web**.

## What this ticket is

A wire-DTO serialization bug. The SPA's `turn_record` JSON should always include `trap_activated` (as `null` when no trap activated, as the trap name when one did). Currently, when no trap activates, the key is **absent entirely** from the JSON.

The mapper to fix lives in `pinder-web/src/Pinder.GameApi/Models/TurnResultPayloadMapper.cs`. A grep just showed it doesn't even reference `trap_activated` — so the field is being dropped at the mapper level, not the JSON-serializer level. The fix is to ensure the mapper writes a `trap_activated` property whose value is either the string trap name or `null`.

## Audit-first note

The ticket mentions #942 might have "fixed" turn 3 incidentally by making it evaluate traps. That's NOT the bug we're fixing here. The bug is the wire field's PRESENCE. Even if the engine now correctly returns no-trap on turn 3, the mapper must still emit `trap_activated: null`, not omit it. This PR is independent of #942 status.

## Workspace isolation
```bash
cd /root/projects/pinder-web
git fetch origin
git worktree add /tmp/work-rung-2-944 origin/main
cd /tmp/work-rung-2-944
git checkout -b rung-2-sonnet-4-6/944
```

## Cold-start
1. Read `/root/projects/eigentakt/agents/backend-engineer.md`.
2. Read `/root/projects/pinder-web/LESSONS_LEARNED.md` if it exists; else `/root/projects/pinder-core/LESSONS_LEARNED.md`.
3. Read `/root/projects/pinder-web/AGENTS.md`.
4. `gh issue view 944 --repo decay256/pinder-core --json number,title,body,comments`.
5. Read `src/Pinder.GameApi/Models/TurnResultPayloadMapper.cs` in full.
6. Find the upstream type that should carry `trap_activated`: `grep -rn "trap_activated\|TrapActivated\|trapActivated" /root/projects/pinder-web/src/ /tmp/work-rung-2-944/pinder-core/src/ 2>/dev/null | head -20`. The pinder-core `TurnResult` or `TurnRecord` type likely has a `string? TrapActivated` or similar property.

## Pathspec discipline
Explicit pathspecs only. Don't commit `agent.log`, build artifacts, `.eigentakt-bin/`.

## Implementation steps

1. Identify whether the field is missing because:
   - **(a)** The mapper doesn't read the field from the engine result at all (most likely from grep results), OR
   - **(b)** The mapper reads it but uses a serializer config that omits null/default values.

2. **If (a):** Add the field. Use the same property naming convention as siblings (`trap_activated` snake_case via `[JsonPropertyName]` attribute, or whatever convention the file uses). Map it from the engine's `TurnResult.TrapActivated` (or equivalent). When the engine returns no trap, the wire field must serialize as `null`, not be omitted.

3. **If (b):** Adjust the serializer config or apply `[JsonInclude]` / `[JsonIgnore(Condition = JsonIgnoreCondition.Never)]` to ensure null isn't dropped.

4. **Snapshot test.** Add a unit test to `src/Pinder.GameApi.Tests/Models/TurnResultPayloadMapperTests.cs` (create the file if it doesn't exist):
   - Test 1: a `TurnResult` with `TrapActivated = "pretentious"` serializes with `"trap_activated": "pretentious"`.
   - Test 2: a `TurnResult` with `TrapActivated = null` serializes with `"trap_activated": null` (NOT omitted). Assert the literal `"trap_activated"` substring is in the JSON output AND the parsed JSON value is JsonValueKind.Null.

5. Build + test:
   ```bash
   dotnet build -c Release 2>&1 | tail -5
   dotnet test src/Pinder.GameApi.Tests/Pinder.GameApi.Tests.csproj --no-build 2>&1 | tail -10
   ```
   Build must be 0 errors. New tests must pass. Pre-existing failures (~56 controller integration baseline) untouched.

## Commit + PR

One commit. PR via:
```bash
gh pr create --repo decay256/pinder-web --base main --head rung-2-sonnet-4-6/944 \
  --title "fix(#944): always serialize trap_activated wire field (null when absent)" \
  --fill
```

PR body must include `Closes #944`, DoD Evidence (build tail, test tail, sample JSON output before/after).

## DO NOT
- Do not merge.
- Do not push to main.
- Do not modify unrelated files (no submodule bump unless required for the test).
- Do not work in `/root/projects/pinder-web/` — only in `/tmp/work-rung-2-944/`.

## Logging
```bash
bash /root/projects/eigentakt/scripts/log.sh backend-engineer "#944" "trap-activated-wire-field" "started" "Investigating mapper drop"
```
After PR:
```bash
bash /root/projects/eigentakt/scripts/log.sh backend-engineer "#944" "trap-activated-wire-field" "completed" "PR #N opened" "<sha>"
```

## Output requirements

End with:
- `## Diagnostic findings` — was it (a) or (b)?
- `## DoD Evidence` — PR URL, JSON before/after, build tail, test tail, `git log -1`, `gh pr view`.
- `## Research Log` — what you read, what you tried.
- `## Filed follow-ups` — any new tickets.
