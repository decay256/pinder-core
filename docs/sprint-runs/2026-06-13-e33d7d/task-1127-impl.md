You are a backend engineer subagent implementing ONE GitHub ticket (#1127) end-to-end in an isolated git worktree, then opening a PR. Follow the project's DoD discipline exactly.

## Workspace setup (isolated worktree — non-negotiable)

Run EXACTLY this, do NOT touch /root/projects/pinder-web/pinder-core directly:

```bash
unset GITHUB_TOKEN
cd /root/projects/pinder-web/pinder-core
git fetch origin
git worktree add /tmp/work-1127 origin/main
cd /tmp/work-1127
git submodule update --init 2>/dev/null || true
git checkout -b fix/1127-apiversion-handshake-contract
```

All edits, builds, tests, commits happen inside /tmp/work-1127. Use /usr/bin/dotnet (8.0.128) directly. Do NOT prepend /root/.openclaw/agents-extra/pinder/bin/ to PATH — that remoting shim is BROKEN.

## Role spec

Read and follow /root/projects/eigentakt/agents/backend-engineer.md (DoD + Research Log discipline). Your PR body MUST include `## DoD Evidence` and `## Research Log` sections.

## Lessons (pinder-core LESSONS_LEARNED.md)

Read /tmp/work-1127/LESSONS_LEARNED.md. Key ones:
- SUBMODULE-SYNC-AFTER-REBASE: after any rebase, `git submodule update --init` before building.
- BUILD-PIPELINE-DISCIPLINE: DoD evidence must include the `dotnet build` output, not just tests.
- WIRE-CONTRACT-REGRESSION-TESTS: wire handshakes have a "looks right but is wrong" trap — pin the exact error code string + body field names + accept/reject policy in tests so a rename or naive ==-check regression breaks the test.
- FILE-SIZE-LIMIT-AND-DRY: keep files ≤400 soft / 600 hard lines.

## AGENTS.md (project rules)

Honor the project AGENTS.md:
- CI = LOCAL ONLY. Verify with `dotnet build` + `dotnet test` locally. Never gate on GitHub Actions.
- Scope = pinder-core ONLY (the engine). DO NOT touch the Unity client (GitLab Diego_Quarantine/p-game is READ-ONLY) or the pinder-web frontend. Server-side request rejection belongs to pinder-web — that is a CROSS-REPO follow-up, NOT implemented here.
- This is a contract/DTO ticket. Keep changes scoped to exactly what #1127 specifies; do not drive-by refactor.

## Scope — #1127 apiVersion handshake contract (pinder-core slice ONLY)

Land in pinder-core: the contract constant, the DTO field, the bump policy, and the structured mismatch-error type (code + body) DEFINITION — plus unit + regression tests. Validation WIRING is pinder-web's (cross-repo follow-up).

1. **Add `apiVersion` to the request contract.** Inspect existing request/response DTOs under `src/Pinder.LlmAdapters` (and any `*Contract*`/`*Dto*` types). EXTEND the existing request DTO with an `apiVersion` field rather than adding a parallel handshake envelope (avoid a jagged interface). Find the right type first; do not invent a new one if a request DTO already exists.
2. **Single constants location for `ApiContractVersion`** + a doc-comment stating the bump policy. **Bump policy = monotonic integer** (increment on any breaking wire change) per the refiner assumption. The value MUST equal #1128's stamped doc version — #1128 has not run yet, so YOU pick the value now and it becomes the source of truth; pick `1` unless an existing version constant already exists in the codebase (search first). Document the chosen number clearly in the Research Log so #1128's doc can stamp the identical number.
3. **Structured mismatch-error type** (code + body shape) defined ONCE and reused. Define the error `code` string and body field names explicitly. This type is referenced by pinder-web later, so make it a real, testable type — not just a description.

## Acceptance (assert in tests — xUnit)
- pinder-core defines `apiVersion` on the request contract + the mismatch error code/body type, with the version constant and a documented monotonic-integer bump policy.
- Unit test: the mismatch-error type serializes to the documented body shape (deterministic).
- **Regression test (WIRE-CONTRACT-REGRESSION-TESTS):** a test that a request with a MISSING apiVersion and one with a WRONG apiVersion both map to the documented mismatch error code/body (deterministic rejection contract), and a MATCHING version maps to "accepted" — using values that would pass a naive ==-only check but fail the documented policy (e.g. an unsupported-but-parseable version). Plus a test pinning the exact error `code` string and body field names (a rename must break the test).
- `dotnet build Pinder.Core.sln` succeeds (capture output).
- `dotnet test Pinder.Core.sln` green. Baseline on main = 4442+ passed / 0 failed / 27 skipped (test counts have grown across the sprint; confirm 0 failures, your new tests ADD to the pass count). If ANY test fails, run that scope 3× and report deterministic-vs-flake, and whether the same failure exists on origin/main. Do NOT label a regression as a flake.

## Cross-repo follow-ups (LIST in PR body — do NOT implement)
- pinder-web (`Pinder.GameApi`, game-api 5101): server-side validation that rejects unsupported versions and emits the structured error on the wire. File/pair a pinder-web ticket — note it in the PR.
- Unity client (GitLab, read-only): must send `apiVersion`. Documented in #1128's integration doc; owned by Martin. Note it, do not act.

## PR
- Push branch, open PR against decay256/pinder-core main.
- PR body MUST contain `Closes #1127` on its own line, plus `## DoD Evidence` (build + test output) and `## Research Log` (the DTO you extended, the constant location + chosen version number + bump policy, the mismatch-error type shape, the cross-repo follow-ups listed).
- Do NOT merge. Do NOT push to main. Report the PR URL + commit SHA when done.
- Append to /tmp/work-1127/agent.log a `started` JSONL line at entry and a `completed` line with PR URL + SHA at exit.

Report back: PR URL, commit SHA, build result, full test result (pass/fail/skip counts, with rerun analysis if any failures), list of files changed with line counts, the chosen ApiContractVersion number, the mismatch-error code string + body field names you defined, and the cross-repo follow-ups you listed.
