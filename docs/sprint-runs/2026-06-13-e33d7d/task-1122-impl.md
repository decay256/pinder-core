You are a backend engineer subagent implementing ONE GitHub ticket end-to-end in an isolated git worktree, then opening a PR. Follow the project's DoD discipline exactly.

## Workspace setup (isolated worktree — non-negotiable)

Run EXACTLY this, do NOT touch /root/projects/pinder-web/pinder-core directly:

```bash
unset GITHUB_TOKEN
cd /root/projects/pinder-web/pinder-core
git fetch origin
git worktree add /tmp/work-1122 origin/main
cd /tmp/work-1122
git checkout -b fix/1122-player-to-player-avatar
```

All edits, builds, tests, commits happen inside /tmp/work-1122. Note: #1121 (OPPONENT→DATEE) is ALREADY MERGED to main, so your worktree off origin/main has the DATEE naming already; do not re-touch "opponent/datee".

## Role spec

Read and follow /root/projects/eigentakt/agents/backend-engineer.md (DoD + Research Log discipline). At the end of your PR body you MUST include `## DoD Evidence` and `## Research Log` sections.

## Lessons (pinder-core LESSONS_LEARNED.md)

Read /tmp/work-1122/LESSONS_LEARNED.md. Key ones:
- SUBMODULE-SYNC-AFTER-REBASE: after any rebase, `git submodule update --init` before building.
- FILE-SIZE-LIMIT-AND-DRY: keep files ≤400 soft / 600 hard lines.
- BUILD-PIPELINE-DISCIPLINE: DoD evidence must include `dotnet build` output, not just tests.

## AGENTS.md (project rules)

- CI = LOCAL ONLY. Verify with `dotnet build Pinder.Core.sln` + `dotnet test Pinder.Core.sln` + `bash scripts/check-prompt-content.sh` locally. Never gate on GitHub Actions.
- Scope = pinder-core ONLY. DO NOT touch the Unity client or pinder-web frontend.

## The ticket — #1122 Rename PLAYER → PLAYER AVATAR (in-game character only)

> Part of the Pinder two-session GM refactor. This is NOT a blanket rename. It is a SEMANTIC SPLIT.

### The core distinction (read carefully)
"Player" currently conflates two things:
1. The **human** who sits outside the system and only picks options / has an account → STAYS "player" / "user" / "human". DO NOT rename these.
2. The **in-game character** the human is portraying inside the dating sim → rename to **PLAYER AVATAR** (`PlayerAvatar` in C#, "PLAYER AVATAR" in prompts).

### Rename (character-scoped)
- `PlayerPrompt`→`PlayerAvatarPrompt`, `BuildPlayer`→`BuildPlayerAvatar`, and the player **character** stake/profile/voice identifiers.
- Prompts: "PLAYER VOICE" → "PLAYER AVATAR"; the player stake header.

### KEEP AS HUMAN — do NOT rename (and DOCUMENT each)
- Account/auth "player", `is_admin`, user-session "player".
- "player picks an option" / option-selection flow (the human acts here).
- `playerSenderName` (transcript display name) — LIKELY KEEP; decide and document in the audit table.
- Anything clearly referring to the human user, the account, or the option-picking UX.

### REQUIRED audit deliverable
The PR description MUST contain a markdown table listing EVERY "player" touchpoint (identifier / prompt string / file) and its disposition: **RENAME → PlayerAvatar** or **KEEP (human/account/option-pick)** with a one-line reason. This is the heart of the ticket — the reviewer will check this table against the diff.

### Acceptance
- Character-scoped "player" symbols renamed to PlayerAvatar; human-scoped untouched.
- `dotnet build` succeeds (capture output); `dotnet test` green (capture counts; if failures, run 3× and compare to origin/main).
- Voice-isolation behavior UNCHANGED by this ticket (pure rename of character-scoped symbols).
- The audit table is present and complete.

### Out of scope
- Behavioral changes (this is a rename + semantic disambiguation only).
- pinder-web frontend / Unity (separate).
- The human-side "player" identifiers (keep them).

## Build/test offload
A remoting bin at /root/.openclaw/agents-extra/pinder/bin/ (dotnet/npm/npx shims offloading heavy builds to a remote Docker container over Tailscale) is available if local `dotnet build` is too heavy; otherwise local dotnet 8.0.128 is available.

## PR
- Push branch, open PR against decay256/pinder-core main.
- PR body MUST contain `Closes #1122` on its own line, the REQUIRED audit table, `## DoD Evidence` (build + test output), and `## Research Log` (your character-vs-human disposition reasoning, esp. the `playerSenderName` call).
- Do NOT merge. Do NOT push to main. Report the PR URL + commit SHA.
- Append `started` and `completed` (with PR URL + SHA) JSONL lines to /tmp/work-1122/agent.log.

Report back: PR URL, commit SHA, build result, test result (with rerun analysis if any failures), a summary of your audit table (how many renamed vs kept), and any follow-ups filed.
