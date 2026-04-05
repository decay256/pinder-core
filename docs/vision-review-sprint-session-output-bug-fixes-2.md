# Vision Review — Session Output Bug Fixes

## Alignment: ✅
This sprint is correctly prioritized. The Session Architecture sprint (#540-#545) shipped the stateful conversation infrastructure, and these four bugs (#572-#575) are the immediate post-ship fixes needed to make that infrastructure actually work. Without these fixes, automated playtests produce broken output (leaked format tags, echoed beats, missing game frame, empty bios). Fixing these is prerequisite to any further prompt engineering or gameplay tuning.

## Data Flow Traces

### #575: Game Definition → System Prompt → LLM
- `GameDefinition.PinderDefaults` exists in `Pinder.LlmAdapters`
- `SessionSystemPromptBuilder.Build(player, opponent, gameDef)` exists and produces 5-section prompt
- `GameSession` constructor detects `IStatefulLlmAdapter` and calls `StartConversation(systemPrompt)`
- ⚠️ **BLOCKING**: GameSession line 174 builds prompt as `_player.AssembledSystemPrompt + "\n\n---\n\n" + _opponent.AssembledSystemPrompt` — **bypasses SessionSystemPromptBuilder entirely**. Game vision, world rules, character roles, meta contract, writing rules never reach the LLM. Filed as #576.
- Required fields: game vision, world description, player role, opponent role, meta contract, writing rules, player prompt, opponent prompt
- ⚠️ Missing: All GameDefinition content (6 of 8 sections) not flowing from `GameDefinition` → `GameSession` → `ConversationSession` → Anthropic API

### #573: NarrativeBeat → Stateful Session → LLM
- `GameSession` calls `ILlmAdapter.GetInterestChangeBeatAsync(context)` on interest threshold cross
- In stateful mode, adapter appends beat prompt as user message → gets response → appends as assistant message
- ⚠️ Beat content (narrator stage directions) pollutes conversation history. Filed as #577.
- Required: ephemeral beat call that doesn't mutate session state

### #572: Opponent Response → Parser → Output
- LLM returns opponent text, optionally wrapped in `[RESPONSE]` tags
- `ParseOpponentResponse` regex requires `[RESPONSE]\s*"([^"]+)"` — fails when LLM omits quotes
- Fallback strips `[SIGNALS]` block but doesn't strip `[RESPONSE]` tag itself
- Required fields: clean message text (no format markers)
- Fix is straightforward: strip `[RESPONSE]` in fallback path

### #574: CharacterLoader.ParseBio → Playtest Header
- `ParseBio` code in `session-runner/CharacterLoader.cs` already handles unquoted bios correctly (lines 375-391)
- Need to verify whether the merged PR (#521) fix actually deployed — may be a stale binary or path issue
- No data flow gap in the code; likely an operational verification issue

## Unstated Requirements
- **Session-runner should load GameDefinition from YAML at startup** — currently no code in `session-runner/Program.cs` references `GameDefinition` or `SessionSystemPromptBuilder`. Even after #575 is fixed in GameSession, the session-runner needs to pass a `GameDefinition` through config.
- **Conversation session should have a `MessageCount` or diagnostic** — debugging stateful session issues (like #573) requires visibility into what messages are accumulated.
- **Format-tag stripping should be universal** — if `[RESPONSE]` leaks, other tags like `[SIGNALS]`, `[ENGINE]`, `[STAT:]` could too. A general "strip known format markers" pass on final output would be more robust.

## Domain Invariants
- The LLM system prompt MUST contain game context (dating frame, character roles) for every session — characters without direction produce generic conversation, not romantic pursuit
- Conversation session messages MUST only contain diegetic content (character dialogue + ENGINE blocks) — narrator/beat content is non-diegetic and must not accumulate
- Playtest output MUST never contain raw format markers (`[RESPONSE]`, `[SIGNALS]`, `[STAT:]`, etc.)
- Character loader ParseBio/ParseLevel MUST produce correct values for all 5 starter characters

## Gaps
- **Missing (critical)**: GameSession doesn't use `SessionSystemPromptBuilder` — this is the root cause of #575 and #531. Filed as #576.
- **Missing**: No session-runner wiring to load/pass `GameDefinition` even after GameSession is fixed.
- **Missing**: #573 fix must ensure beat is ephemeral — not just a prompt change but an architectural change to how stateful adapter handles non-conversation calls. Filed as #577.
- **Assumption**: #574 assumes the ParseBio fix from #513/#521 didn't land — but the code looks correct. Need to verify whether it's a deployment/path issue vs. a code issue.

## Requirements Compliance Check
- **DC-zero-dep**: No concerns — all bugs are in LlmAdapters or session-runner, not Pinder.Core.
- **Backward compat**: #572 and #573 fixes are LlmAdapters-only. #575 touches GameSession constructor but the `IStatefulLlmAdapter` path is already non-default (NullLlmAdapter skips it). No test regression risk.
- **FR (GameSession orchestration)**: #575 fix must not change GameSession's public API — the wiring should flow through `GameSessionConfig` or a similar injection point.

## Recommendations
1. **#576 is BLOCKING for #575**: The fix for #575 must use `SessionSystemPromptBuilder.Build()` in GameSession, not just load the YAML file. The architect should decide whether `GameDefinition` flows via `GameSessionConfig` (as a pre-built string) or via a new interface in Core.
2. **#577 is BLOCKING for #573**: The beat echo fix requires making the beat call ephemeral in stateful mode. A prompt-only fix won't work because the accumulated non-diegetic messages will still degrade future turns.
3. **#572 is clean**: Straightforward parser fix. No concerns.
4. **#574 needs diagnosis first**: The code looks correct. Before re-implementing, verify whether it's a path resolution issue (session-runner loading from wrong directory) or a stale build.
5. **Role assignments are correct**: All four bugs are backend-engineer work in LlmAdapters or session-runner.

## Concerns Filed
| # | Title | Severity |
|---|-------|----------|
| #576 | GameSession builds system prompt by string concatenation — SessionSystemPromptBuilder.Build() never called | **BLOCKING** |
| #577 | NarrativeBeat in stateful mode appends to conversation — pollutes message history | **BLOCKING** |

## Verdict: ADVISORY
Sprint direction is correct. Two concerns filed that the sprint issues should address. #576 identifies the root cause behind #575 (and likely #531). #577 identifies the architectural issue behind #573. Neither blocks sprint start — they inform the implementation approach.
