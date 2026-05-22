You are a backend engineer subagent in the Pinder dev swarm. Implement ticket **core#949** in pinder-core: change the LlmStakeGenerator default system prompt so the model produces a bullet list, and audit pinder-web's MarkdownSanitizer so the bullets are preserved on render.

## Workspace isolation
```bash
cd /root/projects/pinder-core
git fetch origin
git worktree add /tmp/work-949 origin/main
cd /tmp/work-949
git checkout -b chore/949-stake-prompt-bullets
```

## Cold-start
1. Read `/root/projects/eigentakt/agents/backend-engineer.md`.
2. Read `/root/projects/pinder-core/LESSONS_LEARNED.md` — focus on #843 (prompt catalog Phase 1) lessons.
3. Read `/root/projects/pinder-core/AGENTS.md`.
4. `gh issue view 949 --repo decay256/pinder-core --json number,title,body,comments`.

## Diagnosis

```bash
cd /tmp/work-949
# Find current prompt content
grep -n "10-15 words\|No markdown\|stem-completion" src/Pinder.SessionSetup/LlmStakeGenerator.cs 2>&1
grep -n "stake:" data/prompts/templates.yaml 2>&1
# Find the byte-identical assertion test
grep -rn "Issue843_PromptCatalogPhase1Tests\|templates.yaml" tests 2>&1 | head -5
# Find MarkdownSanitizer
find /root/projects/pinder-web -name "MarkdownSanitizer*.cs" 2>&1
# Find any field allowlists / opt-outs in the sanitizer
```

## Implementation

### Phase A — prompt template

In `src/Pinder.SessionSetup/LlmStakeGenerator.cs` `DefaultSystemPrompt`:
- Replace the "Plain text only. One stem-completion per line. Include the stem prefix. ~10-15 words per line. No markdown, no dashes, no numbering, no headings." block with explicit bullet-list instructions:
  - "Output a markdown bullet list. One bullet per stem-completion. Each line starts with `- ` (dash + space). Include the stem prefix in the bullet body. ~10-15 words per bullet. No nested bullets. No numbering. No headings."

Update `data/prompts/templates.yaml`'s `stake` entry to match byte-for-byte.

The `Issue843_PromptCatalogPhase1Tests` test asserts the C# constant and the YAML entry are byte-identical. Both must change in lockstep.

### Phase B — MarkdownSanitizer audit

In `pinder-web/src/Pinder.GameApi/Services/MarkdownSanitizer.cs`:
- Determine whether the sanitizer is applied to the stake field. (It's a defense-in-depth pass for LLM-generated text; check which fields are routed through it.)
- If yes: either (a) exempt the stake field from sanitizer or (b) replace it with a whitespace-only sanitizer for stake.

**Preferred path:** make the sanitizer preserve `- ` line-prefixed bullets unconditionally — it's safe markdown, never a sanitization concern. If the existing sanitizer already preserves bullets, just confirm and document.

Note: this is a cross-repo change — pinder-core for the prompt, pinder-web for the sanitizer. If the cross-repo dance is needed, follow the pattern established in #651 and #649:
1. Implementer makes core change in `/tmp/work-949` (NOT the outer worktree), commits + pushes a `chore/...` branch.
2. `gh pr create + gh pr merge --squash` for the core PR FIRST.
3. Then in pinder-web (`git worktree add /tmp/work-949-web origin/main` from `/root/projects/pinder-web`), make the sanitizer change, bump the core submodule pointer to the merged core SHA, commit + push outer PR.

But: do this only if the sanitizer audit shows a real strip. If the sanitizer already preserves `- ` bullets, NO pinder-web PR is needed. Confirm in your final report.

### Phase C — tests

- `Issue843_PromptCatalogPhase1Tests` must still pass after the byte-identical update.
- If a sanitizer change ships, add a test in pinder-web that `MarkdownSanitizer.Sanitize` preserves a `- bullet` line unchanged for the stake field.
- Run:
  ```bash
  cd /tmp/work-949
  dotnet build pinder-core.sln 2>&1 | tail -10
  dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj 2>&1 | tail -20
  ```

## DoD evidence + Research Log

PR body MUST include:

```markdown
Closes #949

## DoD Evidence
- [ ] `DefaultSystemPrompt` updated to request a `- ` bullet list per stem
- [ ] `data/prompts/templates.yaml` `stake` entry byte-identical to the new C# constant
- [ ] `Issue843_PromptCatalogPhase1Tests`: pass
- [ ] MarkdownSanitizer audit: <preserves-bullets-already | exempted-stake-field | added-test>
- [ ] `dotnet build`: clean
- [ ] `dotnet test Pinder.Core.Tests`: <N/N pass>

## Research Log
<1-2 paragraphs: what the old prompt said, what the new prompt says, sanitizer behaviour before vs after, whether a pinder-web companion PR was needed and why>
```

## Open PR(s)

```bash
gh pr create --repo decay256/pinder-core --base main --head chore/949-stake-prompt-bullets \
  --title "chore(#949): LlmStakeGenerator prompt -> bullet-list format" \
  --body "<DoD evidence + Research Log per template>

Closes #949"
```

If a pinder-web companion PR is needed, open it from a sibling worktree under `/tmp/work-949-web` against `pinder-web` main, and cross-link the two PRs.

Report back with PR URL(s) + commit SHA(s).

## DO-NOT list
- Do NOT touch `/root/projects/pinder-core` or `/root/projects/pinder-web` directly. Use worktrees.
- Do NOT merge any PR yourself.
- Do NOT push to `main`.
- Do NOT include unrelated edits.
- Do NOT commit `pnpm-lock.yaml` in pinder-web (web doesn't track it — the #647 implementer broke this rule).
- Do NOT break the `Issue843_PromptCatalogPhase1Tests` byte-identical invariant — update BOTH the C# constant AND the YAML in lockstep.

All upstream events follow USER.md response-style — short, lead with result, no tables.

When done, your final report goes back to the orchestrator. I will handle review/merge.
