# LESSONS_LEARNED — pinder-core

This file is the project's institutional memory: hazard patterns that bit us
once, the rule that prevents the recurrence, and project-specific notes that
extend the canonical hazard catalogue.

## How to use this file

- **Canonical patterns** are referenced by NAME (e.g. `WORKSPACE-ISOLATION`,
  `SUBMODULE-SYNC-AFTER-REBASE`). The full body lives in
  `~/.openclaw/skills/swarm-drain/references/canonical-lessons.md`. Subagents
  and reviewers should look the body up there if they need it.
- **Project-specific lessons** are written here in full, in the same shape as
  canonical lessons (Symptom / Root cause / Fix / Rule). Add new ones at the
  bottom; never edit a numbered lesson once it's landed unless you're
  correcting an objectively wrong claim. Per `LESSONS-MUST-BE-WRITTEN-IMMEDIATELY`,
  write the lesson when the hazard hits, not at end-of-run.

## Active canonical patterns (from `references/canonical-lessons.md`)

These are the patterns that apply to pinder-core today. Treat each NAME as a
load-bearing identifier — subagent prompts reference them and reviewers
enforce them.

- `WORKSPACE-ISOLATION` — every parallel/subagent gets its own `git worktree`,
  never the main checkout.
- `SUBMODULE-SYNC-AFTER-REBASE` — after any rebase, run
  `git submodule update --init <submodule>` BEFORE building. (pinder-core has
  no submodules of its own; this matters when pinder-core is consumed as a
  submodule of pinder-web.)
- `SUBMODULE-COMMIT-RESET` — when committing inside a submodule, push the
  submodule first, THEN `git add <submodule>` in the parent. Never run
  `git submodule update` between the submodule-commit and the parent-add.
- `WORKTREE-FORCE-FOR-SUBMODULES` — `git worktree remove --force` for
  submodule worktrees.
- `SELF-APPROVE-BLOCKED` — `gh pr review --approve` fails when the gh-cli
  token's identity matches the PR author. Reviewers use `--comment` with an
  explicit `**Verdict: APPROVE/CHANGES_REQUESTED**` line in the body, and the
  orchestrator merges via `gh pr merge --squash --delete-branch`.
- `APPROVED-WORK-IS-IMMUTABLE` — subagents extend, never rewrite. Scope-bleed
  is fatal; a "while I was here, I also fixed…" diff gets reverted on sight.
- `LESSONS-MUST-BE-WRITTEN-IMMEDIATELY` — when a hazard hits, write the
  lesson here NOW, not at end-of-run. Push the commit on the same branch
  that triggered it.
- `REGRESSION-TESTS-ON-BUGS` — every bug ticket must include a
  regression-test acceptance criterion. If a ticket lacks one, add via
  `gh issue comment` before the implementer picks it up.
- `SECURITY-REVIEW-ON-AUTH-AND-EXPOSURE` — auth, redirect, token, secret,
  public endpoint, or exposed-DTO PRs get a dedicated security-reviewer
  subagent. Security verdict outranks code-review on conflict.
- `DOCS-FOLLOW-CODE` — any PR that changes a documented surface (engine
  contract, public DTO, persisted shape, host-integration path) ships a
  follow-up docs PR before the next ticket starts.
- `AGENT-LOG-EVERYTHING` — every implementer/reviewer/fix/security/docs
  subagent appends to `agent.log` (JSONL, schema in
  `~/.openclaw/workspace/skills/swarm-dev/agents/LOGGING.md`). Orchestrator
  logs `merged`, `closed`, `consolidated`. End-of-run summaries read from
  `agent.log`, not chat scrollback.
- `BUILD-PIPELINE-DISCIPLINE` — DoD evidence MUST include the output of the
  exact build/test command CI runs, not a hand-picked subset. For
  pinder-core, that's `dotnet build Pinder.Core.sln` and the relevant
  `dotnet test` filtered to the affected project.
- `ORCHESTRATOR-THIN-WAIST` — the orchestrator is a thin coordinator, not a
  content sink. Never read >1000 tokens into the orchestrator transcript
  when a subagent could read it and return a summary. Compact or fork at
  every ticket boundary if the orchestrator's session jsonl on disk grows
  past 2 MB.
- `REPORT-UPSTREAM-CONTINUOUSLY` — send a progress message after every
  major event in the meta-loop (kickoff, ticket-start, pr-opened, verdict,
  merged, docs-status, lesson-captured, follow-up-filed, final scoreboard).
  One message per event. Even successful events ship messages; silence
  reads as "hung" to the parent.
- `REPO-HYGIENE-SWEEP` — start-of-run AND end-of-run sweep across local
  branches, remote branches, worktrees, stashes, open PRs, documentation
  drift, and retired-lesson references. Three lanes
  (`cleaned-trivially` / `self-unblockable` / `genuinely-ambiguous`) per
  `SELF-UNBLOCK-BY-DEFAULT`. Stale documentation is treated as a bug, not
  housekeeping.
- `SELF-UNBLOCK-BY-DEFAULT` — once the user authorizes a drain run, that
  authorization is a standing yes for the obvious in-scope envelope. Push
  through automation hiccups, retries, and reasonable hygiene defaults
  without bouncing decisions back upstream. Genuinely-irreducible questions
  go to `questions.md` and surface end-of-run; the run continues draining
  tickets that don't depend on the unanswered question.

## Retired canonical patterns

- `CONSOLIDATOR-TRIGGER` — retired with the move to sequential drain mode.
  File-overlap is now handled by ordinary rebase. Project lessons that
  reference this name should be rewritten or removed during the next
  hygiene sweep.

## Project-specific lessons

(None yet. Append new ones below this line as they accumulate. Use the
template from `~/.openclaw/skills/swarm-drain/templates/lesson-template.md`.)

### CONVERSATION-INDEXING-IS-CANONICAL

**Symptom:** Code in `session-runner/` or `src/Pinder.Core/` derives a turn
number from a `ConversationHistory` index using `i / 2` (and/or identifies
the role with `i % 2 == 0`). Today this looks correct; tomorrow a `[scene]`
seed entry, a skipped player turn (empty `DeliveredMessage`), or any other
non-strict-alternation case shifts the indexing and silently misattributes
turn-tagged data (text-diffs, audit fields, snapshots).

**Root cause:** The conversation log is no longer strictly
alternating-pairs. `[scene]` entries get seeded at the front (issue #333),
and player turns may be skipped entirely when the engine produces an empty
delivered message (issue #769). Pair-math against physical indices was
correct under the old invariant and is incorrect under the new one.

**Rule:** Use `Pinder.Core.Conversation.ConversationIndexing` for any
index→(turn, role) mapping. Never write fresh `% 2` / `/ 2` arithmetic
against `ConversationHistory` indices. If `ConversationIndexing` doesn't
expose what you need, extend the helper — don't reinvent the math at the
call site.

**Adjacent rule:** Reviewers grep new diffs for `% 2` and `/ 2` near the
strings `ConversationHistory`, `historyEntries`, `conversationLog` and push
back if they appear without a `ConversationIndexing` call alongside.

**Discovered in:** PR #767 brace-fix follow-up, surfaced in #769 and #774.
Codified during the first pinder-core swarm-drain run.

### GIT-WORKTREE-DOTGIT-IS-A-FILE

**Symptom:** A test that walks up from `AppContext.BaseDirectory` looking
for a repo-root marker `.git/` (as a directory) silently fails to find the
root when the build runs inside a `git worktree` (such as the per-ticket
worktrees this drain creates under `/tmp/work-*`). The test self-skips via
`Assert.Fail("SKIPPED:")` and the implementer mistakes it for a "new"
failure caused by their change.

**Root cause:** In a primary checkout, `<repo-root>/.git` is a directory.
In a worktree (`git worktree add ...`), `<worktree-root>/.git` is a regular
*file* containing `gitdir: <path-to-real-gitdir>`. Code that uses
`Directory.Exists(.git)` only to detect a repo root will return false in
worktrees and never reach the resource it was looking for.

**Fix:** Detect repo roots with `Directory.Exists(gitMarker) ||
File.Exists(gitMarker)` (or use a different stable marker such as the
project `.sln` file). Worktrees are the default execution environment for
swarm-drain runs, so this matters every time.

**Rule:** Any test or tool that walks up looking for a repo root MUST
accept `.git` as either a directory or a file. Do not introduce new
`.git`-as-directory-only checks. If a checkout-vs-worktree distinction is
actually meaningful (rare), make the distinction explicit instead of
letting it leak in via probing.

**Discovered in:** #814 implementation run (sprint character-assets-v1).
Six tests in `CharacterLoaderSpecTests` were spuriously failing in the
worktree until `FindPromptDir()` was taught to accept the file form.
