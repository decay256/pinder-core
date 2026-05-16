You are a backend engineer subagent in the Pinder dev swarm. Implement **pinder-core ticket #902** in one PR.

## Ticket summary

#902 — [bug] Meta-prefix strip only runs at parser stage. Every downstream LLM overlay can re-introduce `LABEL:` artifacts. Fix: extract a shared `MetaPrefixStripper`, apply it after every overlay-producing LLM call in `GameSession.ResolveTurnAsync`, widen regex to include hyphens (e.g. `WOULD-YOU-RATHER:`).

Full issue: `gh issue view 902 --repo decay256/pinder-core`. Read it carefully — it specifies Option A (per-overlay strip), the regex (`^(?:[A-Z][A-Z\s\-]+):\s*`), and required tests.

## Workspace isolation (CRITICAL — WORKSPACE-ISOLATION)

```bash
cd /root/projects/pinder-core
git fetch origin
git worktree add /tmp/work-902-r1 origin/main
cd /tmp/work-902-r1
git checkout -b fix/902-meta-prefix-stripper-r1
```

**Do NOT touch `/root/projects/pinder-core/` directly. All work in `/tmp/work-902-r1/`.**

## Cold-start

1. Read `/root/.openclaw/skills/eigentakt/agents/backend-engineer.md`.
2. Read `/root/projects/pinder-core/LESSONS_LEARNED.md` (WORKSPACE-ISOLATION, REGRESSION-TESTS-ON-BUGS, SELF-APPROVE-BLOCKED).
3. Read `/root/projects/pinder-core/AGENTS.md` — note the **Snapshot Schema Discipline** rule. This ticket adds a new `TextDiff` layer; verify whether `TurnSnapshot` already captures `text_diffs` or if a new field needs adding.
4. `gh issue view 902 --repo decay256/pinder-core`.
5. Locate the existing similar pattern — `CallbackStripper` — and mirror its structure. Find `Pinder.Core.Text.CallbackStripper` and read it.
6. Find the #862 fix: `git log --all --oneline --grep="#862" | head -5` and read the relevant diff. The existing regex lives in `DialogueOptionParsers` — read that too.
7. Locate `GameSession.ResolveTurnAsync` and identify each overlay-producing LLM boundary: `DeliverMessageAsync` (initial + failure-tier), `ApplyTrapOverlayAsync`, `ApplyHorninessOverlayAsync`, `ApplyShadowCorruptionAsync`.

## Approach

1. **Create `Pinder.Core.Text.MetaPrefixStripper`** mirroring `CallbackStripper`'s shape:
   - Regex: `^(?:[A-Z][A-Z\s\-]+):\s*` (note: includes hyphen, widened vs #862).
   - Public API: `Strip(string text, out TextDiff? diff)` returning new text and emitting a `TextDiff` with layer name `"Meta-Prefix Strip"` ONLY when content was actually removed.
   - Match `CallbackStripper`'s exact public-API shape and naming conventions so consumers compose identically.
2. **Refactor `DialogueOptionParsers`** to call `MetaPrefixStripper.Strip(...)` instead of its own inline regex. The pre-existing #862 tests must still pass.
3. **Wire into `GameSession.ResolveTurnAsync`** — apply the strip after each of the 5 overlay-producing LLM call sites listed in the ticket. Emit the `TextDiff` into the existing `text_diffs` list so the audit log records each firing.
4. **Tests:**
   - Unit: `MetaPrefixStripperTests` — verify regex strips `WOULD-YOU-RATHER:`, `CONTEXT:`, `RECOGNITION:`, `OPENER:`, `GENUINE QUESTION:`, `LABEL:`, AND does NOT strip non-label patterns (e.g. quoted strings, leading lowercase, non-colon-terminated tokens). Include a case mirroring the staging-observed string.
   - Integration: in `GameSessionTests` (or a new file mirroring its style), mock an `ILlmAdapter` whose `ApplyTrapOverlayAsync` / `ApplyHorninessOverlayAsync` / `ApplyShadowCorruptionAsync` return `"WOULD-YOU-RATHER: x"` — assert `TurnResult.DeliveredMessage` does NOT start with `WOULD-YOU-RATHER:` and that a `Meta-Prefix Strip` layer appears in `text_diffs`.
   - Refactor existing `DialogueOptionParsersTests` — still pass after the regex moves into `MetaPrefixStripper`.

## Acceptance criteria (from ticket DoD)

- [ ] `Pinder.Core.Text.MetaPrefixStripper` exists; regex widened to include hyphens.
- [ ] `GameSession.ResolveTurnAsync` applies the strip after every overlay-producing LLM call (delivery, failure overlay, trap, horniness, shadow).
- [ ] Each successful strip emits a `Meta-Prefix Strip` `TextDiff` layer with proper before/after spans (matches `CallbackStripper` pattern).
- [ ] `DialogueOptionParsers` routes through the shared `MetaPrefixStripper`.
- [ ] Tests added per ticket §Tests.
- [ ] `LESSONS_LEARNED.md` updated with the "sanitization invariants must run after EACH stage" rule. Use the existing lesson-formatting style (numbered section, "Symptom / Root cause / Rule / Anchors").
- [ ] `dotnet build` clean.
- [ ] `dotnet test` green.

## Snapshot schema check (mandatory)

Per `AGENTS.md` snapshot schema discipline: does this change add a player-visible field to `GameSession`? The strip itself adds entries to existing `text_diffs` — which `TurnSnapshot` should already capture if `text_diffs` was previously a snapshot field. **Verify:** `grep -r "text_diffs" session-runner/Snapshot/SessionSnapshot.cs` — if present, no schema change needed. If absent, the ticket is the chance to add it.

## Workflow rules (mandatory)

- Atomic commits per logical step (extract Stripper, wire DialogueOptionParsers, wire GameSession, tests, LESSONS_LEARNED).
- Run tests with output to `/tmp/test-902.txt`, read tail + grep-failures only. Do NOT pipe raw test output into reasoning.
- Run `dotnet build` on the whole solution before declaring done.
- Open PR via `gh pr create --repo decay256/pinder-core --base main --head fix/902-meta-prefix-stripper-r1 --fill`. Include `Closes #902` on its own line in the body.

## DO NOT

- Do not merge.
- Do not push to main.
- Do not modify unrelated files (no drive-bys).
- Do not work in `/root/projects/pinder-core/` directly.

## Logging to agent.log

At task entry:
```bash
cd /tmp/work-902-r1 && bash /root/.openclaw/skills/eigentakt/scripts/log.sh backend-engineer "#902" "core/text" "started" "Implementing #902 per branch fix/902-meta-prefix-stripper-r1"
```

At task exit:
```bash
cd /tmp/work-902-r1 && bash /root/.openclaw/skills/eigentakt/scripts/log.sh backend-engineer "#902" "core/text" "completed" "PR <#N> opened" "<commit-sha>"
```

## Output requirements

End your final reply with:

### `## DoD Evidence` block (mandatory — actual tool output):
- PR URL.
- `dotnet test` tail showing pass counts.
- `dotnet build` tail showing zero errors.
- `git log -1 --oneline`.
- Push confirmation.
- `gh pr view <N> --json number,title,state,url` output.
- The two agent.log lines you appended.

### `## Research Log` block (mandatory):
| Topic | Source | Key finding |
|---|---|---|

Flag any deviations from the spec with rationale.
