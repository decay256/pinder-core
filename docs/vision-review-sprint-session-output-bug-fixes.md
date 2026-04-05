# Vision Review — Sprint: Session Output Bug Fixes

## Alignment: ✅
This sprint directly addresses playtest-visible bugs from the Session Architecture sprint (#541-#545). All four issues fix data flow breakages between the LLM adapter layer and user-facing output. This is the right work at the right time — the stateful conversation mode just shipped, and these bugs are blocking meaningful playtesting. Fixing them unblocks iteration on LLM prompt quality and game feel.

## Data Flow Traces

### #572 — [RESPONSE] tag leaking into output
- User action → GameSession.ResolveTurnAsync() → ILlmAdapter.GetOpponentResponseAsync() → AnthropicLlmAdapter sends request → LLM returns `[RESPONSE]\n*unmatched*` (no quotes) → `ParseOpponentResponse()` regex fails (requires quotes) → alt regex fails → **fallback takes full text including `[RESPONSE]` tag** → OpponentResponse.Message contains `[RESPONSE]\n*unmatched*` → displayed to user
- Required fields: clean message text without format markers
- ⚠️ Missing: fallback path does not strip `[RESPONSE]` tag — only strips `[SIGNALS]`

### #573 — NarrativeBeat echoes previous message
- Interest threshold crossed → GameSession calls `GetInterestChangeBeatAsync()` → adapter appends beat request as user message to `_session` → LLM sees opponent's last message as most recent assistant turn → generates in opponent voice → **beat echoes previous dialogue** → beat appended to `_session` as assistant turn → conversation history permanently polluted
- Required fields: ephemeral narrator stage direction, NOT part of conversation thread
- ⚠️ **BLOCKING data flow issue**: beat appends to `_session.Messages`, polluting all subsequent turns. Already captured in vision concern #577.

### #574 — ParseBio returns empty
- `--player gerald` → `LoadCharacter()` → tries `CharacterDefinitionLoader` (JSON path, line 117) → reads bio from JSON → passes to `PromptBuilder.BuildSystemPrompt()` → **BUT does NOT pass bio to `CharacterProfile` constructor** → `CharacterProfile.Bio = ""` → playtest header shows empty bio
- Required fields: `bio` flowing from JSON → `CharacterProfile.Bio`
- ⚠️ **Root cause misidentified**: Issue says "verify fix from sprint landed" (referring to `CharacterLoader.ParseBio` fix #513). Actual root cause is `CharacterDefinitionLoader` line 101 skipping the `bio` parameter. Filed as vision concern #579.

### #575 — game-definition.yaml not loading
- GameSession constructor → detects `IStatefulLlmAdapter` → builds system prompt as `player.AssembledSystemPrompt + "\n\n---\n\n" + opponent.AssembledSystemPrompt` → **`SessionSystemPromptBuilder.Build()` never called** → game vision, world rules, player/opponent roles, meta contract, writing rules all missing from LLM context → no dating frame in generated options
- Required fields: game vision, world description, player role, opponent role, meta contract, writing rules (all 5 sections from `SessionSystemPromptBuilder`)
- ⚠️ **Architecture gap**: `GameSession` (Core) can't call `SessionSystemPromptBuilder` (LlmAdapters) due to dependency direction. Needs config slot or host-level wiring. Already captured in vision concern #576.

## Unstated Requirements
- **All format tags must be stripped in all fallback paths**: if `[RESPONSE]` leaks, `[SIGNALS]` and `[ENGINE]` could too in edge cases. The implementer should audit all tag-stripping fallbacks.
- **Beat generation in stateful mode must not affect conversation coherence**: every non-beat turn after a beat should see the same conversation state as if the beat never happened.
- **System prompt wiring must be testable without LLM calls**: the system prompt passed to `StartConversation()` should be verifiable in unit tests.

## Domain Invariants
- Opponent message text delivered to the player must never contain internal format markers (`[RESPONSE]`, `[SIGNALS]`, `[ENGINE]`)
- Conversation session message history must contain only in-character dialogue — no narrator, no meta-prompts
- Character identity fields (bio, name, level) must survive all loading paths (prompt file AND assembler pipeline)
- System prompt for stateful sessions must include game-level context (dating frame, roles) in addition to character prompts

## Gaps

### Missing
- **#574 AC should reference assembler path**: current AC only tests `CharacterLoader` path. Must also verify `CharacterDefinitionLoader` produces non-empty bio. (Addressed by concern #579)

### Unnecessary
- Nothing — all four bugs are playtest-blocking.

### Assumptions to validate
- **#575**: The fix must decide where the `GameDefinition` → system prompt wiring lives. `GameSessionConfig` gaining a `string? SystemPromptOverride` is the cleanest path (Core stays zero-dep). The implementer needs to read concern #576.
- **#573**: The fix must decide between ephemeral API call vs append-then-remove. Ephemeral is cleaner (concern #577).

## Requirements Compliance Check
- No FR/NFR/DC violations identified. All fixes are backward-compatible bug fixes.
- All changes respect the one-way dependency: `LlmAdapters → Core`.
- Zero NuGet dependency constraint on Pinder.Core is maintained.

## Concerns Filed
| # | Title | Blocks? |
|---|-------|---------|
| #576 | GameSession builds system prompt by string concatenation — SessionSystemPromptBuilder.Build() never called | Already existed |
| #577 | NarrativeBeat in stateful mode appends to conversation — pollutes message history | Already existed |
| #579 | CharacterDefinitionLoader skips bio param — CharacterProfile.Bio always empty via assembler path | **NEW** |

## Recommendations
1. **#574 implementer must fix `CharacterDefinitionLoader` line 101** — add `bio: bio` to the `CharacterProfile` constructor call. This is the actual root cause, not `ParseBio`.
2. **#575 implementer must add a `string? SessionSystemPrompt` field to `GameSessionConfig`** so the host (session-runner) can build the full prompt via `SessionSystemPromptBuilder.Build()` and pass it in. `GameSession` uses this instead of naive concatenation when available.
3. **#573 implementer must make beat calls ephemeral** in stateful mode — build a separate request with the session's system prompt but only the beat user message, no accumulated history. Do NOT append to `_session`.
4. **#572 implementer should strip ALL known format tags** in the fallback path, not just `[SIGNALS]`. Add `[RESPONSE]` stripping and consider a generic tag-stripping utility.

## Verdict: ADVISORY

All four bugs are real, well-scoped, and high-priority. One new concern filed (#579) identifying the actual root cause of #574. Two pre-existing concerns (#576, #577) cover the architecture issues for #575 and #573. The sprint should proceed with the concerns added to scope.
