You are a backend engineer subagent implementing **pinder-core #904** in one PR. Small, display-only ticket — decouple the player-facing "TropeTrap" label from the enum identity when the check kind is NOT an option roll.

## Ticket summary

Read the full issue first: `gh issue view 904 --repo decay256/pinder-core`. Short version:

- `FailureTier.TropeTrap` enum value stays. YAML keys (`trope_trap`) stay. Wire DTO field stays.
- On the OPTION ROLL, the player-facing label stays "TropeTrap" (the trap DOES fire).
- On HORNINESS / SHADOW / SHADOW-GROWTH checks, the player-facing label becomes "**Severe**" (orchestrator's choice from the ticket's recommendation list — crisp, scale-consistent, removes the trap implication).
- New helper: `FailureTierDisplay.Label(FailureTier, RollCheckKind)` in `src/Pinder.Core/Rolls/FailureTierDisplay.cs`.
- LLM-prompt sites in `Pinder.LlmAdapters/SessionDocumentBuilder.cs:552, 567` ALSO route through this helper (per ticket recommendation — prompts read coherently when kind is non-option).

This ticket depends on `RollCheckKind` from #901, which **just landed on main** (PR #918 merged at 2026-05-16T08:24Z, sha `1f22672`). Branch off latest `main`.

## Workspace

```bash
cd /root/projects/pinder-core
git fetch origin
git worktree add /tmp/work-904 origin/main
cd /tmp/work-904
git checkout -b fix/904-tropetrap-label-per-kind
```

**Work in `/tmp/work-904/` only.**

## Cold-start reading order

1. `/root/.openclaw/skills/eigentakt/agents/backend-engineer.md` — your role spec.
2. `/root/projects/pinder-core/AGENTS.md` — Snapshot Schema Discipline (not applicable here — display label is NOT a player-visible field on `GameSession`, it's a rendering helper).
3. The ticket (`gh issue view 904`).
4. `src/Pinder.Core/Rolls/RollCheckKind.cs` — confirm enum values (`OptionRoll`, `Steering`, `Horniness`, `Shadow`, `ShadowGrowth`).
5. `src/Pinder.Core/Rolls/FailureTier.cs` — confirm enum values.
6. `src/Pinder.LlmAdapters/SessionDocumentBuilder.cs` lines 540-590 — read the existing TropeTrap macro replacement context. Specifically lines 552 and 567 + surrounding switch statements.
7. Search for any other server-side site that converts `FailureTier` → human label:
   ```bash
   grep -rn "FailureTier\." --include="*.cs" src/ | grep -i "label\|display\|name\|tostring" | head -20
   ```

## Implementation plan

### Step 1 — `FailureTierDisplay` helper

New file: `src/Pinder.Core/Rolls/FailureTierDisplay.cs`:

```csharp
using Pinder.Core.Rolls;

namespace Pinder.Core.Rolls
{
    /// <summary>
    /// Maps (FailureTier, RollCheckKind) → player-facing label.
    /// Decouples display from enum identity: TropeTrap activates a trap ONLY on option rolls;
    /// on other check kinds it's just "miss margin 6-9" and should not imply a trap fired.
    /// Wire DTOs, YAML keys, and the FailureTier enum itself are unchanged — only the
    /// human-readable string changes per kind.
    /// </summary>
    public static class FailureTierDisplay
    {
        public static string Label(FailureTier tier, RollCheckKind kind)
        {
            if (tier == FailureTier.TropeTrap && kind != RollCheckKind.OptionRoll)
                return "Severe";
            return tier.ToString();
        }
    }
}
```

This is the entire production helper. All other `FailureTier` values pass through `tier.ToString()` — they're unambiguous across kinds.

**Commit:** `feat(#904): add FailureTierDisplay.Label per-kind helper`.

### Step 2 — wire LLM-prompt sites through the helper

`src/Pinder.LlmAdapters/SessionDocumentBuilder.cs` lines 552, 567 (and any other `FailureTier.TropeTrap` → string sites in that file):

- Identify which `RollCheckKind` is in scope at each site. Most likely the option-roll path (the macro is per-option-roll prompt), but verify. If the site is reached only from option-roll, label = `tier.ToString()` — no behavior change. If it could be reached from horniness/shadow, you MUST plumb the kind through and use `FailureTierDisplay.Label(tier, kind)`.

Concretely:
- Look at the method signature(s) containing lines 552 and 567. What's their input? A `RollResult`? A `HorninessCheckResult`?
- If it's `RollResult`, `kind = RollCheckKind.OptionRoll`. Label is unchanged.
- If it's a polymorphic site, pass `kind` through and use the helper.

**Be cautious.** Don't change LLM prompt content for the option-roll path — that would be a behavior change. The ticket recommendation is to route through the helper for coherence; doing so MUST be a no-op for `OptionRoll` (helper returns `"TropeTrap"`). Confirm this with a unit test:
- `FailureTierDisplay.Label(TropeTrap, OptionRoll) == "TropeTrap"` ← unchanged

**Commit:** `refactor(#904): SessionDocumentBuilder TropeTrap macros route through FailureTierDisplay`.

If after reading the actual code you decide the SessionDocumentBuilder sites are PURELY option-roll and there's no benefit to plumbing the kind enum down, document that in your `## Deviations` block at the end and skip this step. The core deliverable is the helper + the frontend follow-up; the LLM-prompt routing is a recommendation, not a hard requirement.

### Step 3 — tests

New file: `tests/Pinder.Core.Tests/Rolls/FailureTierDisplayTests.cs`:

```csharp
using Pinder.Core.Rolls;
using Xunit;

namespace Pinder.Core.Tests.Rolls
{
    public class FailureTierDisplayTests
    {
        [Fact]
        public void TropeTrap_OnOptionRoll_LabelIsTropeTrap()
            => Assert.Equal("TropeTrap", FailureTierDisplay.Label(FailureTier.TropeTrap, RollCheckKind.OptionRoll));

        [Theory]
        [InlineData(RollCheckKind.Horniness)]
        [InlineData(RollCheckKind.Shadow)]
        [InlineData(RollCheckKind.ShadowGrowth)]
        [InlineData(RollCheckKind.Steering)]
        public void TropeTrap_OnNonOptionKinds_LabelIsSevere(RollCheckKind kind)
            => Assert.Equal("Severe", FailureTierDisplay.Label(FailureTier.TropeTrap, kind));

        [Theory]
        [InlineData(FailureTier.None)]
        [InlineData(FailureTier.Fumble)]
        [InlineData(FailureTier.Misfire)]
        [InlineData(FailureTier.Catastrophe)]
        [InlineData(FailureTier.Legendary)]
        public void NonTropeTrapTiers_LabelPassesThrough(FailureTier tier)
        {
            Assert.Equal(tier.ToString(), FailureTierDisplay.Label(tier, RollCheckKind.OptionRoll));
            Assert.Equal(tier.ToString(), FailureTierDisplay.Label(tier, RollCheckKind.Horniness));
            Assert.Equal(tier.ToString(), FailureTierDisplay.Label(tier, RollCheckKind.Shadow));
        }

        [Fact]
        public void Enum_Identity_Unchanged()
        {
            // Guard against accidental enum mutation. Wire/YAML rely on these names.
            Assert.Equal("TropeTrap", FailureTier.TropeTrap.ToString());
            Assert.Equal("Fumble", FailureTier.Fumble.ToString());
            Assert.Equal("Catastrophe", FailureTier.Catastrophe.ToString());
        }
    }
}
```

If you plumbed the kind through `SessionDocumentBuilder` in step 2, add one integration-style test that asserts the prompt-rendered label for a horniness TropeTrap input is `"Severe"`.

**Commit:** `test(#904): FailureTierDisplay per-kind label tests`.

### Step 4 — spec doc

New file: `docs/specs/issue-904-failure-tier-display.md`. Document:
- Why the enum/wire/YAML stay literal `TropeTrap`.
- Why the player-facing label diverges per kind.
- The chosen non-option-roll label: `Severe`.
- Frontend follow-up filed against pinder-web (to be filed by orchestrator, just mention "see pinder-web#TBD").

Mirror the style of `docs/specs/issue-905-ghost-probability-per-turn.md` (recently landed).

**Commit:** `docs(#904): spec doc for FailureTierDisplay per-kind label`.

## Acceptance criteria

- [ ] `FailureTierDisplay` exists with the (FailureTier, RollCheckKind) → string contract.
- [ ] `Label(TropeTrap, OptionRoll) == "TropeTrap"` (unchanged).
- [ ] `Label(TropeTrap, Horniness | Shadow | ShadowGrowth | Steering) == "Severe"`.
- [ ] All other tier values pass through `tier.ToString()` regardless of kind.
- [ ] `FailureTier` enum unchanged. YAML keys unchanged. Wire DTO unchanged.
- [ ] `SessionDocumentBuilder` either routes through the helper (preferred) OR a documented deviation explains why pure-option-roll sites don't need it.
- [ ] Tests added; all 2752 (now 2756+) tests pass.
- [ ] Spec doc added.
- [ ] PR body contains `Closes #904` on its own line.

## Workflow rules

- Commit incrementally per the 4 steps.
- Run tests after each code-touching step:
  ```bash
  dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj --no-restore > /tmp/test-904.txt 2>&1 && tail -10 /tmp/test-904.txt
  ```
- Open PR:
  ```bash
  git push -u origin fix/904-tropetrap-label-per-kind
  gh pr create --repo decay256/pinder-core --base main --head fix/904-tropetrap-label-per-kind --title "fix/904 TropeTrap tier display label per check kind" --body "$(cat <<EOF
  - feat(#904): add FailureTierDisplay.Label per-kind helper
  - refactor(#904): SessionDocumentBuilder TropeTrap macros route through FailureTierDisplay (if applied)
  - test(#904): FailureTierDisplay per-kind label tests
  - docs(#904): spec doc for FailureTierDisplay per-kind label

  Player-facing label decoupled from FailureTier enum: 'TropeTrap' on option rolls (trap fires);
  'Severe' on horniness/shadow/shadow-growth (no trap). Enum, wire DTO, and YAML keys unchanged.
  Frontend mirror is a separate pinder-web follow-up.

  Closes #904
  EOF
  )"
  ```

## Pre-existing breakage (NOT yours)

`Pinder.LlmAdapters.Tests` has 72 pre-existing failures (#909). Run `Pinder.Core.Tests` only. If you touched `Pinder.LlmAdapters/SessionDocumentBuilder.cs`, also `dotnet build src/Pinder.LlmAdapters/Pinder.LlmAdapters.csproj` to confirm clean build, but do NOT run its test project.

## DO NOT

- Do NOT rename `FailureTier.TropeTrap` enum value.
- Do NOT change YAML keys.
- Do NOT change wire DTO fields.
- Do NOT change LLM prompt behavior on the OPTION-ROLL path (label there stays "TropeTrap"; helper returns the same string).
- Do NOT touch frontend (pinder-web is a separate repo + separate follow-up ticket).
- Do NOT bump submodule pointer.
- Do NOT merge.

## Logging

```bash
cd /tmp/work-904 && bash /root/.openclaw/skills/eigentakt/scripts/log.sh backend-engineer "#904" "core/rolls" "started" "Implementing #904 per branch fix/904-tropetrap-label-per-kind"
```

At exit:
```bash
bash /root/.openclaw/skills/eigentakt/scripts/log.sh backend-engineer "#904" "core/rolls" "completed" "PR #<N> opened" "<commit-sha>"
```

## Output

### `## DoD Evidence` block (mandatory):
- PR URL.
- `dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj` tail.
- `dotnet build` tail (Pinder.Core + Pinder.LlmAdapters if touched).
- `git log --oneline origin/main..HEAD`.
- Push confirmation.
- `gh pr view <N> --json number,title,state,url`.
- agent.log lines.

### `## Research Log` block (mandatory):
| Topic | Source | Key finding |
|---|---|---|

### Deviations

If `SessionDocumentBuilder` plumbing was skipped because the sites are pure-option-roll, document it here with file:line evidence.
