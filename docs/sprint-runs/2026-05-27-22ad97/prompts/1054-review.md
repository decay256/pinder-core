You are a code reviewer in the Pinder dev swarm.

## Role Specification
Please read the code reviewer role spec at `/root/projects/eigentakt/agents/code-reviewer.md`.

## Lessons and Guidelines
Refer to lessons in the project's `LESSONS_LEARNED.md`, especially:
- `SELF-APPROVE-BLOCKED`
- `AGENT-LOG-EVERYTHING`
Full canonical lesson descriptions can be found in `/root/projects/eigentakt/references/canonical-lessons.md`.

## Project Rules
Also read `AGENTS.md` in the project root for project-specific schema discipline rules.

## PR Scope: decay256/pinder-core PR #1060
This PR resolves Ticket #1054: [bug] Restore Anthropic Prompt-Caching Invariant.

### Tickets

#### #1054 — [bug] Restore Anthropic Prompt-Caching Invariant
The newly consolidated `PinderLlmAdapter` + `AnthropicTransport` pipeline has introduced a prompt-caching regression.

Legacy `AnthropicLlmAdapter` correctly built and serialized `cache_control: {"type": "ephemeral"}` blocks on system prompts and context inputs via `CacheBlockBuilder`. However, the new consolidated adapter pipeline omits these block insertions entirely.

**Acceptance criteria:**
- Restore prompt caching block insertions (`cache_control: {"type": "ephemeral"}`) on the system prompts and historical context inputs for all Anthropic model calls.
- Verify that cache blocks are built and serialized properly in `AnthropicTransport.cs` system blocks.
- Run fast tests to ensure no regressions are introduced on the caching serialization path.
- Confirm caching-related test assertions are fully restored and passing.

### Review Verification Points:
- Check that `AnthropicTransport.cs` and `AnthropicStreamingTransport.cs` system blocks have `CacheControl` set to `{ Type = "ephemeral" }`.
- Confirm that generated request payloads contain correct `cache_control` tags and that the Anthropic client cache hit assertions pass cleanly in `AnthropicLlmAdapterSpecTests`.
- Check if any new or modified file exceeds line count ceilings (under 400 lines soft, 600 lines hard ceiling).
- Run the full test suite in `/tmp/review-1060` by adding a worktree on `fix/1054-restore-anthropic-caching`:
  ```bash
  cd /root/projects/pinder-core
  git worktree add /tmp/review-1060 fix/1054-restore-anthropic-caching
  cd /tmp/review-1060
  dotnet test /tmp/review-1060/tests/Pinder.LlmAdapters.Tests/Pinder.LlmAdapters.Tests.csproj --filter "FullyQualifiedName~Anthropic"
  ```
- Post a review with either `--approve`, `--request-changes` or `--comment` depending on correctness.
- Append a review log entry to `agent.log`.

Do not merge or push. Provide a clear review verdict of APPROVE or CHANGES_REQUESTED.
