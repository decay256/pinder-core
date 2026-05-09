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

### NEVER-SILENTLY-TRUNCATE-LLM-INPUTS

**Symptom:** Stake-generation samples for #826 were compared head-to-head
between prompt variants for weeks before anyone noticed the prompts were
being fed amputated character profiles. `LlmStakeGenerator.BuildUserMessage`
was slicing `assembledSystemPrompt` to the first 4000 chars
(`Math.Min(4000, ...Length)` + `Substring(0, ...)`), with no log line, no
metric, no marker on the truncated string, and no marker in the resulting
stake. The bug had been live since the stake generator was written. Every
eval, every sample comparison, every dataset built off stake outputs had a
silent ceiling baked in.

**Root cause:** Defensive truncation written once for cost/latency reasons
that (a) outlived its justification, (b) was never observable, and (c) was
physically detached from the prompt design — so when the prompt was
iterated, no one re-asked whether the cap still made sense.

**Rule:** Never silently truncate any string that is going to an LLM. If a
cap is genuinely required:

1. The cap value lives in a single named constant with a comment explaining
   *why* (cost, provider hard limit, snapshot column width — be specific).
2. Every truncation event emits a log line at WARN with the original
   length, the cap, and the call site.
3. A metric or counter increments so we can see truncation frequency in
   prod.
4. The truncated string carries a visible marker (`...[truncated]` or
   equivalent) so downstream consumers see that data was lost.
5. Tests assert that under normal operation the cap is never hit. If it is,
   that's a regression alert, not a steady state.

**Better than capping:** structured summarisation (build a tighter version
of the input upstream) or splitting the input across multiple LLM calls.
Capping is a last resort; if it's the right call, it must be loud.

**Code-review checklist:** any new `Substring(0, n)`, `[:n]` (Python),
`Truncate`, `MaxLength`, or tokeniser-based shortening that lands on a
prompt-bound or audit-bound string requires a comment block explaining
which of the five rules above applies. PRs that quietly add such a slice
without the comment block must be sent back.

**Discovered in:** #834 audit, prompted by #826 stake-prompt re-spec
discussion in the pinder Discord (2026-05-09).
