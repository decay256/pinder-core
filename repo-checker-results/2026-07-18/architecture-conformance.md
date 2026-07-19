# Repo-Checker Report: architecture-conformance

> Scope: full multi-repo audit of `A:\Data\ClaudeCodex\pinder-core` and `A:\Data\ClaudeCodex\pinder-web` as one Pinder system.
> Approved exceptions loaded: 4.
> Suppressed by approved exception: 0.
> Findings: U1=0, U2=1, U3=0.

## Topic

Architecture, data-flow, dependency direction, declared architecture/ADR conformance, duplicated ownership across `pinder-core` and `pinder-web`, layer bypasses, and cross-repository contract drift.

## Findings

### Finding 1: pinder-web builds against a stale pinder-core submodule instead of the current sibling core repo
**File**: `A:\Data\ClaudeCodex\pinder-web\src\Pinder.GameApi\Pinder.GameApi.csproj:19`
**Issue**: `pinder-web` declares the C# service should "reference only the real pinder-core submodule projects" and then references `..\..\pinder-core\src\Pinder.Core\Pinder.Core.csproj`, `Pinder.LlmAdapters`, `Pinder.Rules`, `Pinder.SessionSetup`, `Pinder.NarrativeHarness`, and `Pinder.RemoteAssets` from that nested submodule at lines 21-26. The Docker build also copies `pinder-core/src/` and `pinder-core/data/` into the GameApi image (`A:\Data\ClaudeCodex\pinder-web\src\Pinder.GameApi\Dockerfile:31`, `A:\Data\ClaudeCodex\pinder-web\src\Pinder.GameApi\Dockerfile:37`). The parent README says the submodule pointer must be verified against `origin/main` after a pinder-core squash merge (`A:\Data\ClaudeCodex\pinder-web\README.md:99`). As audited, the standalone core repo is `e96a75f4c4fb7b8c008f8c61403aae6327eb6ca2` on `origin/main`, while the `pinder-web` submodule and parent tree point at `a0c59c8cdd111bb0bf44bafe50722c5bd4df5e09`. This is not just a harmless SHA mismatch: the current core parser recognizes `[TELL_BONUS: yes]` via `TellBonusRegex` and passes `hasTellBonus` into `DialogueOption` (`A:\Data\ClaudeCodex\pinder-core\src\Pinder.LlmAdapters\Anthropic\DialogueOptionParsers.cs:35`, `A:\Data\ClaudeCodex\pinder-core\src\Pinder.LlmAdapters\Anthropic\DialogueOptionParsers.cs:147`, `A:\Data\ClaudeCodex\pinder-core\src\Pinder.LlmAdapters\Anthropic\DialogueOptionParsers.cs:156`), but the web-pinned copy still rejects any `TELL_BONUS` metadata as unsupported (`A:\Data\ClaudeCodex\pinder-web\pinder-core\src\Pinder.LlmAdapters\Anthropic\DialogueOptionParsers.cs:35`, `A:\Data\ClaudeCodex\pinder-web\pinder-core\src\Pinder.LlmAdapters\Anthropic\DialogueOptionParsers.cs:76`, `A:\Data\ClaudeCodex\pinder-web\pinder-core\src\Pinder.LlmAdapters\Anthropic\DialogueOptionParsers.cs:338`). The web-pinned data also lacks current source-of-truth content edits, for example `hair2` has no current one-sentence texting-style constraint in the submodule while the sibling core repo has it (`A:\Data\ClaudeCodex\pinder-core\data\items\starter-items.json:1172`; missing at the same item in `A:\Data\ClaudeCodex\pinder-web\pinder-core\data\items\starter-items.json:1163`).
**Impact**: The documented system shape is `React SPA -> FastAPI proxy -> Pinder.GameApi -> pinder-core engine` (`A:\Data\ClaudeCodex\pinder-web\README.md:7`), and `pinder-core/data/` is treated as the single source of truth for game content (`A:\Data\ClaudeCodex\pinder-web\docs\ARCHITECTURE.md:1520`). With the parent repo pinned behind current `pinder-core`, web GameApi builds and serves an older engine/data contract than the sibling repository under audit. Fixes merged to core can appear complete in `pinder-core` while production web behavior still rejects or omits them.
**Urgency**: U2 - topic default; this is cross-repository contract drift on the active GameApi build path, but it is not an immediate data-loss or secret-leak path.
**Fixer-Agent Action Plan**: Decide whether `pinder-web` should track the current `pinder-core` main tip or an explicitly documented release SHA. If current main is intended, update the `pinder-web/pinder-core` submodule to `e96a75f4c4fb7b8c008f8c61403aae6327eb6ca2`, commit the parent pointer, and run the GameApi tests plus any prompt/dialogue parser tests. Add a lightweight CI/local guard that compares the submodule pointer with the expected core SHA so future audits do not have to rediscover drift by hand.

## No Additional Concrete Findings

Checked the documented tier boundary (`frontend` uses `/api/*`, not internal GameApi ports), core project dependencies, `Pinder.RemoteAssets` dependency direction, operation contract versioning, API contract versioning, and the documented FastAPI/GameApi ownership split. No additional architecture-conformance findings were found with concrete evidence in this pass.
