You are a **no-context code reviewer** for pinder-core PR **#912** covering ticket **#899** (horniness text overlay runs last, after shadow corruption).

Fresh-eye objectivity.

## ⚠️ Pre-existing breakage NOT in scope

`Pinder.LlmAdapters.Tests` has 72 pre-existing failures on `main` (tracked as #909). Run `Pinder.Core.Tests` only.

## Cold-start

1. Read `/root/.openclaw/skills/eigentakt/agents/code-reviewer.md`.
2. Read the ticket: `gh issue view 899 --repo decay256/pinder-core`.
3. Read PR: `gh pr view 912 --repo decay256/pinder-core` + `gh pr diff 912 --repo decay256/pinder-core`.
4. **Fresh worktree:**
   ```bash
   cd /root/projects/pinder-core
   git fetch origin
   git worktree add /tmp/review-899 origin/fix/899-horniness-text-overlay-last
   cd /tmp/review-899
   ```

## Critically verify

### A. Acceptance criteria

- [ ] Order flipped in `GameSession.cs`. Old: trap → horniness-text → shadow. New: trap → shadow → horniness-text.
- [ ] §15 interest-delta halving STILL LAST (unchanged). This is the load-bearing invariant — verify by reading the code that the halving call sites are exactly where they were before, NOT moved alongside the text overlay.
- [ ] Comment block at the GameSession.cs site (line ~1578/1681 per the research log) rewritten to describe the new invariant + references #899.
- [ ] Doc references updated: `docs/ARCHITECTURE.md`, any `delivery-instructions.yaml` header, `rules-v3*.md`, etc.
- [ ] `LESSONS_LEARNED.md` entry added in the existing style.
- [ ] Ordering-sensitive tests updated. `Issue399_HorninessShadowOrderingTests.cs` was explicitly named — confirm it's been flipped.
- [ ] `Pinder.Core.Tests` green (2707/0/18 per implementer's DoD).

### B. Correctness hazards specific to this PR

1. **HorninessEngine.PeekAsync vs CheckAsync split.** The implementer split `HorninessEngine.CheckAsync` into `PeekAsync(check only) + caller-applied rewrite`. This is a real refactor, not just a call-site swap. Verify:
   - The split preserves the original semantics for non-#899 callers (if any).
   - `HorninessCheckResult.OverlayApplied` is set correctly in `PeekAsync` BEFORE the caller applies the rewrite, so §15 halving in the SAME turn sees `OverlayApplied=true` when appropriate.
   - The §15 halving path consumes `HorninessCheckResult.OverlayApplied` (not the actual text-rewrite outcome) — otherwise the deferred halving could see stale state.
2. **Trap-overlay short-circuit.** If a trap rewrite produces a short-circuit (e.g. message gets fully replaced or truncated), is the new shadow-then-horniness pipeline still well-defined? Look for early-returns between trap and shadow in `GameSession.ResolveTurnAsync` — the order swap shouldn't introduce a path where horniness silently doesn't fire when it should.
3. **`Meta-Prefix Strip` interaction.** Sprint just merged #902 which inserts `MetaPrefixStripper.Strip` after every overlay-producing LLM call. The new horniness-last order means the meta-prefix strip after horniness is now the FINAL message-modifying pass before delivery. Verify the strip is still applied after the new horniness position (it should be, since the strip was wired per-overlay-call-site, not by ordering).
4. **TextDiff ordering in audit log.** The audit log will now show Trap → Shadow → Horniness instead of Trap → Horniness → Shadow. Any external consumer (snapshot replay tooling, debug UI) that relies on ordering needs to handle the flip. Read the implementer's doc updates — does ARCHITECTURE.md step 10a/10b/10c document this?

### C. Cross-cutting

- **Drive-by changes.** Compare PR file list to ticket scope. Anything outside `src/Pinder.Core/Conversation/`, `docs/`, `LESSONS_LEARNED.md`, ordering-sensitive tests?
- **Snapshot schema discipline.** Implementer reports no `.snap.json` golden files exist. Verify by `find . -name "*.snap.json"` in the worktree — if there are any, they must be re-baked.
- **§15 halving deferral.** The implementer's deviation note says "None" — but the prompt was explicit that §15 must stay last. Open `GameSession.ResolveTurnAsync` and walk the flow: trap → shadow → horniness (text) → ... → §15 halving. The halving should be AFTER `ApplyHorninessOverlayAsync` (the text rewrite), at the very end of the turn. Confirm via reading.

### D. Tests soundness

```bash
cd /tmp/review-899
dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj --no-restore > /tmp/test-899-r.txt 2>&1
tail -3 /tmp/test-899-r.txt
# Expect: 0 failed, 2707 passed

# Targeted: ordering tests
dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~Horniness|FullyQualifiedName~Shadow|FullyQualifiedName~Ordering" > /tmp/test-899-ordering.txt 2>&1
tail -5 /tmp/test-899-ordering.txt
```

If `Issue399_HorninessShadowOrderingTests` still exists and runs, read its body and confirm the assertions match the new order. If it was deleted, ask why in your review comment.

## Output

Post review with `gh pr review 912 --repo decay256/pinder-core --approve|--comment ...`.

**Self-approve blocked if gh identity matches PR author.** Post `--comment` with explicit `**Verdict: APPROVE|CHANGES_REQUESTED|NEEDS_DISCUSSION**`.

## Log to agent.log

```bash
cd /tmp/review-899 && bash /root/.openclaw/skills/eigentakt/scripts/log.sh code-reviewer "#899" "core/conversation" "review-started" "Starting review for PR #912"
```

At exit:
```bash
cd /tmp/review-899 && bash /root/.openclaw/skills/eigentakt/scripts/log.sh code-reviewer "#899" "core/conversation" "review-done" "Verdict=<>, Findings=N"
```

## Reply in this session with

- One-paragraph verdict.
- Specific findings if any.
- Test result tails.
- gh review URL.
- agent.log lines.
