# Sprint 13 Captured Lessons

The following project-specific lessons were captured during Sprint 13:

## 1. EIGENTAKT-BASH-SHIM-RECURSION
- **Symptom:** Subagent build/test runs consume 100% CPU on the host, hang indefinitely, and produce nested bash processes.
- **Root cause:** Running `install-wrappers.sh` on a project with custom test commands that use `bash` (e.g., `cmd: "bash scripts/check-prompt-content.sh"`) installs a shadowing shim for `bash` itself in `.eigentakt-bin/`. Since the shim itself uses `#!/usr/bin/env bash` and is prepended to `PATH`, it recursively calls itself, causing infinite fork-loop recursion.
- **Rule:** Never install shadowing shims for `bash` or other shells. If `install-wrappers.sh` attempts to install a shell shim, exclude it from the `.eigentakt-bin/` directory or delete it immediately before running build or test commands.

## 2. EIGENTAKT-SUBAGENT-TIMEOUT-RECOVERY
- **Symptom:** A subagent task times out with `status: timed out` at the gateway level, but later announces successful completion.
- **Root cause:** Large refactoring tasks or complex test-suite execution can exceed the gateway's transient wait timeout, triggering an early "timeout" status. However, the background process on the sandbox/host remains alive and completes the work successfully.
- **Rule:** When a subagent "times out", do not immediately abort the run. Inspect the repository worktree, git logs, and `agent.log` to see if the subagent actually completed the work. If the work is complete and tests pass, proceed with code review and merge, recovering the run seamlessly.

## 3. EIGENTAKT-NULL-GAMEDEF-OPTION-CAP
- **Symptom:** Mock LLM outputs return 4 options in unit tests, but `PinderLlmAdapter` only returns 3, causing test assertions to fail.
- **Root cause:** `PinderLlmAdapter` capped option generation to `_options.GameDefinition?.MaxDialogueOptions ?? 3`. In unit tests where `GameDefinition` is null, it prematurely fell back to 3, cutting off valid mocked test options.
- **Rule:** Set the default dialogue options cap on the fallback path to a very large number (e.g. 99) when `GameDefinition` is null, ensuring unit tests can mock any number of options without being truncated.
