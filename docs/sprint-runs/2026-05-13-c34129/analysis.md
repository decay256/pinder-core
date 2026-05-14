# Sprint analysis — 2026-05-13-c34129

## Sprint summary

Drained 3 tickets (#853, #854, #855) plus parent epic #819, plus sprint-end docs PR. All 4 PRs merged. 2 follow-up tickets filed mid-sprint. No questions queued.

## PRs landed (in merge order)

| PR | Ticket | Title | Merge commit | Rung (impl/review) | Wall-clock |
|---|---|---|---|---|---|
| #856 | #853 | assembly scaffold + read path | `a6ec232` | impl R0 (gemma-4-31b-it) / review R1 (deepseek-v4-pro) | ~26 min |
| #857 | #854 | query + paging | `5ddfb43` | impl R0 / review R1 | ~22 min |
| #858 | #855 + #819 | publish/save/delete (closes epic) | `a5b6296` | impl R0 / code-review R1 + security-review R1 | ~33 min |
| #861 | (docs, no ticket) | sprint-end docs pass | `d130f97` | tech-writer R2 (sonnet-4-6) | ~6 min |

Total elapsed: ~1h28m wall-clock from #853 kickoff to docs-PR merged.

## Issues touched

- **Closed:** #853, #854, #855, #819. All four closed via `Closes` lines in the merge commits.
- **Opened (follow-ups from security review):** #859 (https-scheme enforcement on `Configuration.BaseUrl`), #860 (HttpClient.MaxResponseContentBufferSize cap).

## Rung performance

All 3 implementer attempts succeeded on the first try at Rung 0 (`openrouter/google/gemma-4-31b-it`). Zero escalations. Zero 3-strike triggers. Zero token-explosion or wall-clock-overrun events.

This is a clean signal that Rung 0 is sufficient for "extend an existing C# assembly with a well-scoped feature against a precise spec" — at least when the ticket body itself is dense and prescriptive (these were).

Caveats:
- Sample size 3, all in the same module on the same day. Don't generalize.
- The tickets were authored in the same session that orchestrated the drain, with full wire-contract context. A different author's tickets might not be as prescriptive.
- The implementer at Rung 0 made one prompt-correction (#853: my prompt incorrectly claimed `IRemoteCharacterStore` had no `ExistsAsync`; the implementer correctly read the interface and used `ExistsAsync`). This is a positive datapoint — the implementer didn't blindly follow a wrong prompt.

Reviewer at Rung 1 (`openrouter/deepseek/deepseek-v4-pro`) was thorough on every PR — re-ran greps, re-built, re-ran tests, walked DoD line by line, verified pre-existing-failure-shape against `main`. The structural critic worked.

Security-reviewer at Rung 1 (same model) surfaced two real Medium findings on #855 that were both non-blocking but worth filing. Defense-in-depth value confirmed.

Technical-writer at Rung 2 (`anthropic/claude-sonnet-4-6`) produced a module doc matching house style and a LESSONS_LEARNED entry that captures the regression-test pattern with the actual byte sequences. Sonnet was the right rung for this work — Rung 0/1 likely would have produced a rougher doc.

## Implementer deviations (all called out in PR bodies, all accepted on review)

1. **#853 — `RemoteAssetMalformedMetadataException`.** Ticket explicitly allowed an implementer-choice typed exception for malformed metadata framing; implementer added a dedicated subclass. Accepted.

2. **#853 — `CharacterPayloadParser` delegate on `Configuration`.** Keeps `Pinder.RemoteAssets` independent of `Pinder.SessionSetup` (which would have dragged in `Pinder.LlmAdapters`). Production callers wire the parser from one level up; tests inject stubs. Strictly additive to the surface. Accepted.

3. **#853 — Interpreted ticket's "Exists" as inherited `ExistsAsync`.** My implementer prompt was wrong (claimed the method didn't exist on the interface). Implementer correctly read `ICharacterStore` and used the existing method. Accepted.

4. **#854 — Extended `CharacterAssetQuery` (in `Pinder.Core`) with 5 optional fields.** The ticket's required test list demanded URL-encoding tests for `asset_kind=character/v1` and date filters; the existing type didn't carry those fields. Reviewer applied the three-part deviation test (additive / justified / spec-compatible) and accepted. The spec already documented these as in-scope for v1.x forward-compat.

5. **#855 — `SaveAsync` as a thin delegate to `PublishAsync`** with synthesised minimal metadata. The interface requires both methods; the ticket explicitly forbade inventing a new public method. LSP-honest. Accepted.

6. **#855 — Added two new Configuration knobs** (`MetadataSizeCapBytes`, `PayloadSizeCapBytes`) and an optional `PayloadSerializer` delegate. All three additive with sensible defaults from the spec. Accepted.

7. **#855 — Extended `FakeHttpMessageHandler`** (test infrastructure) with body-snapshot capture. Additive; existing #853/#854 tests untouched. Reviewer verified snapshot timing (inside `SendAsync` before production-code `using` block can dispose). Accepted.

## Trigger observations (calibration-relevant)

All trigger thresholds in `model-routing.yaml` are seed-uncalibrated and ran in their default state this sprint:

| Trigger | Threshold | Fired? | Observed value |
|---|---|---|---|
| `3-strike-review` | 3 fail-cycles | No | 0 (all PRs approved on first review) |
| `token-explosion` | 200k tokens | No | Max ~52k out (#855 implementer) |
| `wall-clock-overrun` | 90 min per PR | No | Max ~15m (#855 implementer) |

Proposed threshold revisions: none yet. Three PRs is not enough signal to claim a calibration. Continuing to run uncalibrated is correct — see TRIGGER-CONSERVATISM-UNTIL-CALIBRATED. The first ~5 sprints should keep these thresholds where they are.

One observation worth tracking across future sprints: the per-PR wall-clock varied 22-33 min for "extend an existing assembly with one new method group plus tests" — the variance was driven by ticket scope (write path is genuinely more work than read path) not by model latency. If a future ticket of similar shape takes >90 min that would actually be a signal, not noise.

## Lessons captured immediately (LESSONS-MUST-BE-WRITTEN-IMMEDIATELY)

One lesson committed to `LESSONS_LEARNED.md` in the docs PR (#861):

- **WIRE-CONTRACT-REGRESSION-TESTS** — when a wire detail has a known implementer trap, pin it with a regression test that uses inputs which would silently succeed under the wrong implementation. The base64url test and the tags-regression test are the canonical examples.

Not in `LESSONS_LEARNED.md` but worth noting in this analysis:

- **agent.log conflict-on-pull pattern.** Every time main moved during this sprint, the orchestrator's local agent.log mutations conflicted with the squash-merged implementer entries. JSONL conflict resolution is mechanical (keep both halves, drop the markers) but it's noise. Future sprints could either (a) skip the orchestrator's local agent.log writes during a sprint and append them at the end as a single commit, or (b) write orchestrator log entries to a separate file like `agent.orchestrator.log`. Filed mentally as a potential workflow-tooling tweak; not worth a ticket yet.

## Cross-cutting checks at sprint end

Verified once on `main` post-#858 + docs merge:

- `grep -RIn 'using Eigencore' --include='*.cs' --exclude-dir=bin --exclude-dir=obj .` → 0 hits.
- `grep -RIn 'Pinder\.RemoteAssets' src/Pinder.Core/ src/Pinder.SessionSetup/` → 0 hits.
- `dotnet build` clean (0 errors).
- `Pinder.RemoteAssets.Tests`: 47/47 pass.
- Pre-existing failure baseline unchanged (Rules 46, LlmAdapters 63 — both verified reproducible on `main` pre-sprint).

Architectural rule (Eigencore = third-party app) preserved end-to-end.

## What worked

- **Sequential drain.** Three dependent tickets with strict ordering (#853 → #854 → #855) merged in 90 minutes with zero rework. No worktree collisions, no submodule pointer drift (no submodule in this repo).
- **Precise tickets.** All three sub-PR tickets had dense bodies with explicit error-mapping tables, explicit test lists, and explicit out-of-scope demarcation. The Rung 0 implementer executed against them without ambiguity.
- **Reviewer skepticism.** The Rung 1 reviewer re-ran greps and tests on every PR rather than trusting the implementer's DoD claims. On #854 the reviewer empirically verified that pre-#854 `CharacterAssetQuery` rejects the new tests at compile time — that's the right kind of "is this deviation truly necessary" evidence.
- **Security review surfacing real Mediums.** The two security findings (#859, #860) are both real and worth fixing; neither was a stretch. The structural separation between code-review and security-review delivered value.
- **Docs as part of the sprint.** The technical-writer pass captured the architectural-rule statement, the wire-trap regression-test pattern, and the consumer paragraph in `ARCHITECTURE.md` — none of which would have been written if docs had been deferred to "later."

## What to watch

- The implementer at Rung 0 changed `Pinder.Core` on #854 (the `CharacterAssetQuery` extension). That was correct in this case, but a less-careful implementer at Rung 0 could change an engine assembly in a non-additive way. The architectural-rule grep on every review catches the worst case (`using Eigencore.*` leaking in), but additive-mutation-to-an-existing-type is harder to catch with grep. The reviewer-side three-part deviation test (additive / justified / spec-compatible) is the safety net.
- Two of the four PRs had `agent.log` JSONL conflicts on pull. Mechanical to resolve, but if a future sprint involves more parallel mutations it could become annoying. Watch.

## Questions queue

Empty. No mid-sprint or end-of-sprint questions to escalate.
