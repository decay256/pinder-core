You are a code-reviewer subagent reviewing **pinder-core PR #922** (Issue #904 — TropeTrap tier display label per check kind).

You are a **no-context fresh-eye reviewer**. Read the PR cold; don't read the implementer's deviation explanation before forming your own opinion.

## PR

- URL: https://github.com/decay256/pinder-core/pull/922
- Branch: `fix/904-tropetrap-label-per-kind`
- Base: `main`
- 3 commits, small surface area

## Cold-start

1. Read `/root/.openclaw/skills/eigentakt/agents/code-reviewer.md`.
2. Read the ticket: `gh issue view 904 --repo decay256/pinder-core`.
3. Fetch the PR:
   ```bash
   cd /root/projects/pinder-core && git fetch origin
   git diff origin/main...origin/fix/904-tropetrap-label-per-kind
   ```

## Specific verification points

1. **`FailureTierDisplay.Label(tier, kind)` exists** with contract:
   - `(TropeTrap, OptionRoll) → "TropeTrap"` (unchanged from `tier.ToString()`).
   - `(TropeTrap, Horniness | Shadow | ShadowGrowth | Steering) → "Severe"`.
   - All other tier values pass through `tier.ToString()` regardless of kind.

2. **`FailureTier` enum unchanged.** YAML keys unchanged. Wire DTO unchanged. Grep the diff for any changes to `FailureTier.cs` — should be zero. Same for any `.yaml` file.

3. **The implementer's `SessionDocumentBuilder` deviation.** They claim:
   - Lines 552/567 return all-caps LLM macros (`"TROPE_TRAP"`), not player-facing labels.
   - Both call sites (lines 253, 319) are pure option-roll paths.
   - Routing through the helper would alter LLM prompt content.

   **Verify this independently** by reading `src/Pinder.LlmAdapters/SessionDocumentBuilder.cs` lines 540-600 and the two call sites at 253, 319. Specifically:
   - Are the returned strings actually all-caps LLM macros (`TROPE_TRAP`, `FUMBLE`, etc.) or player-facing labels (`TropeTrap`)?
   - Are the call sites genuinely option-roll-only? Trace: `BuildDeliveryPrompt` ← who calls it? `BuildOpponentPrompt` ← who calls it? If both are reached only via option-roll flows, deviation is sound.
   - Is there ANY other LLM-prompt or player-facing site that emits a `FailureTier` → string for a non-option check? Grep:
     ```bash
     grep -rn "FailureTier\." --include="*.cs" src/ | grep -iE "tostring|label|display|name" | head -30
     ```
     The ticket explicitly lists `Pinder.LlmAdapters/SessionDocumentBuilder.cs:552, 567` as the ONLY server-side display sites. Confirm.

   If the deviation is sound, the helper's existence (even unused by current code) is still correct — it's the contract the frontend follow-up will consume. Mark as APPROVE.

   If you find a missed non-option `FailureTier` → string site that DOES need the helper, that's a blocker.

4. **Tests in `tests/Pinder.Core.Tests/Rolls/FailureTierDisplayTests.cs`:**
   - Cover the (TropeTrap, OptionRoll) = "TropeTrap" case.
   - Cover the (TropeTrap, non-OptionRoll kinds) = "Severe" cases — all 4 (Horniness, Shadow, ShadowGrowth, Steering).
   - Cover the pass-through cases for None, Fumble, Misfire, Catastrophe, Legendary.
   - Include the enum-identity guard test.
   - Reverse-verify: temporarily change the helper to return `"X"` for `(TropeTrap, OptionRoll)`. Confirm the test FAILS. Revert.

5. **Spec doc** at `docs/specs/issue-904-failure-tier-display.md` documents the why (enum/wire/YAML stay literal, label diverges per kind) and the chosen name "Severe".

6. **PR body** contains `Closes #904` on its own line.

7. **No drive-bys.** Files touched: 1 new helper, 1 new test file, 1 new spec doc. Confirm.

8. **No submodule pointer bump.** No package version bumps.

## Run the tests yourself

```bash
cd /root/projects/pinder-core
git fetch origin
git checkout origin/fix/904-tropetrap-label-per-kind --detach 2>&1
git submodule update --init --recursive 2>&1
dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj --no-restore > /tmp/review-922-tests.txt 2>&1
tail -10 /tmp/review-922-tests.txt
```

Expected: 2763 passed, 0 failed, 18 skipped.

`Pinder.LlmAdapters.Tests` has 72 pre-existing failures (#909) — don't run.

## Verdict

```bash
gh pr review 922 --repo decay256/pinder-core --comment --body "$(cat <<'EOF'
**Verdict: APPROVE** (or **Verdict: CHANGES_REQUESTED**)

<structured review>
EOF
)"
```

## Authority

First-pass review. Orchestrator decides next.

## Logging

```bash
bash /root/.openclaw/skills/eigentakt/scripts/log.sh code-reviewer "#904" "core/rolls" "started" "Reviewing PR #922 (first pass)"
```

At exit:
```bash
bash /root/.openclaw/skills/eigentakt/scripts/log.sh code-reviewer "#904" "core/rolls" "completed" "Verdict: <V>, blockers: <N>, follow-ups: <N>"
```

## Output

Final response is the same body posted to PR.
