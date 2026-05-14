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

### LLM-INPUT-SILENT-TRUNCATION-IS-ALWAYS-A-BUG

*(also known as NEVER-SILENTLY-TRUNCATE-LLM-INPUTS — the canonical name as of #834.)*


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
discussion in the pinder Discord (2026-05-09). Fix landed in PR for #834 —
removed the 4000-char cap in `LlmStakeGenerator.BuildUserMessage` and the
500-char cap in `LlmPlayerAgent.BuildSystemMessage` (sim-tool path); audit
documented in the PR body classified all remaining `Substring(0, n)` /
`Math.Min(n, ...Length)` hits in the repo as either (a) deterministic
parsing/display, (b) error-body excerpts with a visible `...[truncated]`
marker (Anthropic/OpenAI transports), or (c) other non-LLM-bound display.


## Lesson — DRY post-processing belongs at the transport boundary

When a post-processing step (strip-thinking-blocks, normalise-punctuation,
audit-record, rate-limit, …) needs to apply to *every* LLM response
regardless of which call site issued it, **do not implement it as a
per-callsite helper that consumers must remember to call**. Wrap the
`ILlmTransport` (and `IStreamingLlmTransport`) with a decorator and let
the transport boundary enforce the invariant.

The per-callsite pattern fails predictably:

1. The list of call sites grows. Each new prose-only surface (delivery,
   opponent reply, steering, overlays, stake, outfit, interest beat,
   tomorrow's TBD surface) is a new place to remember the helper.
2. Reviewers can't audit the absence of a helper. They see callers that
   *do* call it; they don't notice the new caller that *doesn't*.
3. Tests are noisy: every call site gets a near-identical "asserts the
   strip happened" test. The decorator pattern lets the transport own
   the invariant with one test pair (non-streaming + streaming) instead
   of N.
4. Subtle ordering bugs leak in. Example: refusal-detection runs after
   strip-then-trim in three of the four call sites today. If a future
   call site forgets to strip-before-detect, a thinking-block phrase
   like "I cannot proceed without more context" inside the reasoning
   trace triggers a spurious refusal fallback.

**Streaming caveat.** Decorator semantics are easy for non-streaming
(single string transform). For streaming, the post-processor must handle
the case where a leading sentinel block (e.g. `<thinking>...</thinking>`)
spans multiple fragments. Two acceptable approaches:

  - **Buffer-then-flush** (used by `ThinkingStrippingLlmTransport`):
    accumulate fragments while the buffer might still be a leading
    block; flush either when the closing tag is seen (apply transform)
    or when the leading characters definitively rule out a block (flush
    as-is). A safety cap protects against unbounded buffering when the
    closing tag never arrives.
  - **Suppress-then-yield**: detect the opening tag at the start of the
    buffer; suppress further yields until the closing tag is seen; then
    yield the post-tag stream normally. Closer to streaming semantics
    but more state.

For most cases `buffer-then-flush` is preferred — simpler implementation,
acceptable latency penalty (only on thinking-prefixed responses), and
a clear safety bound.

**Discovered in:** #831 (2026-05-10). The original
`InlineThinkingStripper` was added in #351 with four explicit call
sites in `PinderLlmAdapter.cs`. By the time the issue was filed, four
*more* prose-only surfaces (opponent response, interest beat, stake,
outfit) had accumulated without per-callsite calls — exactly the
failure mode the lesson predicts. The fix lifted the strip to a
transport-level decorator (`ThinkingStrippingLlmTransport`) registered
in DI ahead of `PunctuationNormalizingTransport`.

**Code-review checklist:** when reviewing a new
`ILlmTransport`/`IStreamingLlmTransport` decorator, verify it is
registered in DI in *both* pinder-core's `session-runner/Program.cs`
*and* pinder-web's `LlmProviderFactory.cs`. Cross-repo wiring drift
(decorator landed in core but never wired in web) is a real failure
mode.

### LEGACY-DATA-FALLBACK-PATH-IS-A-TRAP

**Symptom:** `Issue836_TextingStylePlaceholderAggregationTests.cs`
(the predecessor of `Issue836_TextingStyleAggregationRuleTests.cs`)
used a `LoadJson` helper that walked through several known roots
before searching the test binary's ancestor directories. The first
entry, `/root/.openclaw`, resolved to
`agents-extra/pinder/data/items/starter-items.json` — a STALE mirror
of the canonical `pinder-core/data/items/starter-items.json` from
before the #834 texting-style-pool rework. Tests passed against the
stale file because the placeholder aggregator didn't parse axes; it
just picked two raw fragments. When the #836 v1 aggregator (which
requires the new `SYNTAX:`/`TONE:` block format) shipped, the tests
still loaded the stale file and silently produced empty axis maps,
failing with `Expected: ["emoji", …] Actual: []`. Diagnosed only
by probing what `JsonItemRepository` actually returned.

**Root cause:** A multi-root `LoadJson` helper that prefers `~/.openclaw`
or similar global locations over the in-repo path was useful when
tests ran outside the repo. After the data files moved canonical
location to `pinder-core/data/`, the global mirror went stale and the
lookup order silently preferred stale data. There was no signal that
the wrong file was loaded — just `Actual: []`.

**Rule:** test data fixtures used by integration / parsing tests must
load from a single, canonical, in-repo location. Don't allow fallbacks
to out-of-repo mirrors. If tests must run outside the repo (CI without
repo checkout), use embedded resources (`<EmbeddedResource>` in the
csproj) instead of disk-walking lookups.

**Adjacent rule:** when a parser-style aggregator's tests start
failing with `Actual: []`, suspect input-loading first — not the
parser. The parser is small and easy to print; the loader is the
likely silent source of stale or wrong-format input.

**Discovered in:** #836 (2026-05-10). The replacement test file
(`Issue836_TextingStyleAggregationRuleTests.cs`) loads strictly from
`<repo-root>/data/items/starter-items.json` via ancestor walk; no
global-root fallbacks. The `agents-extra/pinder/data/` mirror was not
updated to the new SYNTAX/TONE format because it's understood as a
legacy mirror, but the tests no longer touch it.

### TEXTING-STYLE-AGGREGATION-RULE-IS-DOCUMENTED-CANON

**Rule:** the texting-style aggregation rule (slot → syntax-axis,
anatomy-group → tone-axis) is documented in
`docs/persona/texting-style-aggregation.md`. The doc is the
source-of-truth for designers and operators. Code changes that
affect the rule MUST update the doc in the same PR; the doc must
never drift behind the implementation.

**Discovered in:** #836 (2026-05-10). Designed alongside the
implementation, not after.

### WIRE-CONTRACT-REGRESSION-TESTS

**Pattern:** when a wire detail has a well-known implementer trap — one where the wrong implementation often works in the common case and silently succeeds — pin it with a regression test that uses inputs that would *pass* under the wrong implementation but *fail* correctly only under the right one.

**Two canonical examples from #819 (`Pinder.RemoteAssets`):**

**Base64 alphabet (`X-Asset-Metadata` header).**
The spec requires RFC 4648 *standard padded* base64 (`Convert.FromBase64String`), not base64url (`WebEncoders.Base64UrlDecode`). The two alphabets differ only on two characters: `+`/`/` (standard) vs `-`/`_` (base64url). A developer reaching for the URL-safe variant is a natural mistake (the header travels in an HTTP response header, which feels "URL-adjacent"). Most test inputs happen to encode to only alphanumeric characters and never expose the difference.
The regression test uses byte sequence `0xFB 0xFF 0xBF`, which encodes to `+/+/` in standard base64 and `-_-_` in base64url. The test:
1. Asserts the standard base64 form (`+/+/=`) decodes without error.
2. Asserts the base64url form (`-_-_`) throws `RemoteAssetMalformedMetadataException`.
Any future refactor that swaps the decoder breaks the test immediately on a distinguishing input, not silently on production data.

**Query parameter name (`tag` vs `tags`).**
The eigencore asset-query endpoint uses the singular repeatable `tag=` parameter for multi-tag filtering. The plural `tags=` is not recognized; the backend silently drops unknown query parameters and returns unfiltered results with no error signal. This is exactly the class of bug that manual testing doesn't catch (results look correct for untagged or single-tag queries) and that only shows up in production as mysteriously unfiltered result sets.
The regression test asserts `Assert.DoesNotContain("tags=", builtUrl)` on every code path in `BuildQueryUri`. The test is a canary: any future refactor that introduces a `tags=` emission fails immediately.

**Rule:** when you implement against a wire contract and find yourself handling a detail that has a known "looks right but is wrong" alternative implementation, write the regression test first. The test documents the trap for the next developer and survives refactoring that the original author can no longer reason about.

**Discovered in:** #819 sprint (sub-PRs #856/#857/#858, 2026-05-13). The base64url test and tags-regression test are in `Pinder.RemoteAssets.Tests`. See also pinder-core#851 — the May 2026 drift discovery that revealed both traps existed in the original Pinder-side implementation.

---

### MEASURE-BEFORE-CACHING-AND-USE-P50-NOT-P99-WHEN-GC-DOMINATES-THE-TAIL

**Symptom:** Issue #840 framed itself around "replace the dual loader
with a single assemble-and-cache pipeline." The measurement gate
introduced in the issue body was "if `CharacterDefinitionLoader.Assemble`
p99 < 1ms, skip the cache." Measured numbers on the drain host:

- mean ≈ 0.7-0.9 ms
- p50 ≈ 0.45-0.50 ms
- p99 ranges from 1.9 ms to 14.3 ms across runs (high variance)

The p99 spikes correlated with mean being ~2× p50 — the classic GC
tail-spike signature. The assembler itself is sub-millisecond; the
p99 is GC pauses, not assembler cost. A `CharacterProfile` cache
would not reduce GC pressure (it would cache the heap-allocated
result, not change the allocation pattern); it would just shift the
pauses elsewhere.

**Root cause of the framing miss:** p99 sounds like a fair gate
("how slow does the assembler get?") but in a GC'd runtime it's
measuring "how often did the GC pause during this 1000-iteration
benchmark?" That's a property of the benchmark loop's allocation
rate, not of the assembler. p50 captures the question being asked
("is the assembler intrinsically fast?") cleanly.

**Rule:** When proposing a cache because of a perceived perf
problem in a GC'd runtime, measure p50 first. If p50 is
intrinsically cheap, p99 spikes that look like "slow tail" are
almost always GC, not the code under test. Caching the result of a
computation does not reduce GC pressure unless the cache itself
allocates less than the original — which is rarely true for
"return a cached complex object" patterns. Document p50/p99/mean
side-by-side in the PR body so the reviewer can see whether the
gate is firing on intrinsic cost or on tail noise.

**Detection rule:** if mean is more than ~1.5× p50, the workload
is tail-spike-dominated. The right next step is either to fix the
allocation pattern (lower allocation → less GC → lower p99) or to
accept that p99 is GC and reframe the perf question in terms of
p50.

**Discovered in:** #840 (2026-05-10). The decision to ship Step 1
only (drop the dual-loader / fallback path) and skip Step 2 (the
cache) was made on the basis of p50 sub-ms; p99 spikes were
recorded but recognised as GC, not assembler cost. The full
discussion lives in the drain's `questions.md` (Q1) and in the
#840 PR body. If a future revision finds the assembler IS
intrinsically slow (p50 > 1ms after a real change), Step 2 ships
then — but not speculatively against GC noise.


### OPPONENT-LENGTH-RECIPROCITY

**Symptom:** Opponent response length was governed entirely by the character's
texting-style block, with no relative-length constraint. Observed in prod
session `707fca72` turn 2: player sent 1054 chars (post-shadow-corruption),
opponent replied with 957 chars — combined ~2000 chars in one turn, ~3 phone
screens. A short player message ("k") could also trigger a full texting-style
wall from a "long run-on" opponent.

**Root cause:** The opponent-response prompt had a static length sentence
("Length follows your character's texting style") with no reciprocal-length
constraint tied to the player's message. This failed to model the implicit
conversational norm of message-length convergence.

**Fix (#866):** Replaced the static length sentence in
`data/prompts/templates.yaml` → `opponent-response-instruction` with a
`{length_hint}` placeholder. `SessionDocumentBuilder.BuildOpponentPrompt`
computes `ceiling = min(600, max(playerLen × 2, 80))`, constructs a two-part
hint ("Aim for roughly N characters... Do not exceed M characters..."), and
injects it. Both `AnthropicLlmAdapter` and `OpenAiLlmAdapter` post-validate
the LLM response length against `1.2 × ceiling` and log a `Console.Error`
warning when exceeded (warn-only phase 1; no retry). The slop factor (20%)
avoids noise from minor off-by-a-few-char cases. The 600-char absolute
ceiling prevents essays regardless of player message length.

**Rule:** Opponent response length must be reciprocally bounded to the
player's message length with an absolute ceiling. The formula lives in
`SessionDocumentBuilder.ComputeResponseCeiling` — any future tuning of the
ceiling, floor, or multiplier should touch only that method. The
`{length_hint}` placeholder in the prompt template is the inject point; the
post-LLM warning is the observability hook.

**Adjacent rule:** If the warn-only approach proves insufficient in
production (frequent warnings), escalate to phase 2 (retry with
length-clamped re-prompt) in a follow-up ticket. Do not silently add retry
in a drive-by — it changes latency characteristics and should be tracked as
a separate issue.

**Discovered in:** #866 (2026-05-14). Full design discussion in the issue
body; the chosen option (b) relative-window + 600-char-ceiling was resolved
by the ticket-refiner agent. See also the 707fca72 session log that
motivated the ticket.

### GAME-DEFINITION-SECTIONS-HAVE-ROLE-AFFILIATION

**Symptom:** `BuildPlayer()` included opponent-behavior sections
(OpponentFriction, OpponentCuriosity) that describe how the opponent
should resist and probe — irrelevant to the player-side delivery prompt.
This leaked ~705 tokens of noise into every player-side API call. The
bug was invisible at the LLM output level (the delivery LLM just ignored
the irrelevant sections) and only surfaced through a token audit of
the delivery prompt size (#867).

**Root cause:** When the `Build` → `BuildPlayer` / `BuildOpponent` split
was introduced, every new GameDefinition section was mechanically added
to all three methods without auditing which sections describe which
role's behavior. The assumption was "more context is always better."

**Fix (#867):** Removed `OpponentFriction` and `OpponentCuriosity` from
`BuildPlayer()` (kept in `Build()` and `BuildOpponent()`). The audit:
- OpponentFriction → opponent-only (how opponent resists) → cut
- OpponentCuriosity → opponent-only (how opponent probes) → cut
- ConversationArcProgression → mixed (conversation structure for both
  sides) → keep in both
- PlayerProbing → player-specific (what player should do) → keep in
  BuildPlayer, correctly already absent from BuildOpponent

The `Build()` combined variant (test-only callers) retains all sections
since it assembles a joint system prompt for both characters.

**Rule:** Every `GameDefinition` section has a role-affiliation: it
describes either player behavior, opponent behavior, or shared
conversation structure. When a new section is added:
1. Declare its affiliation in the PR description.
2. Add it only to the methods whose role it describes.
3. Write a regression test that asserts BuildPlayer excludes
   opponent-only sections and BuildOpponent excludes player-only
   sections.
The combined `Build()` method is test-only plumbing; do not adjust it
for role-affiliation unless a production caller appears.

**Detection rule:** any new `GameDefinition` property that appears in
all three of `Build()` / `BuildPlayer()` / `BuildOpponent()` is a red
flag in review — why was the section not role-affiliated? The author
must either (a) justify why it's truly shared in the PR body, or (b)
remove it from the method whose role doesn't match.

**Discovered in:** #867 (2026-05-14). Token audit of session `707fca72`
cosmetic phase prompted by the refiner agent.

