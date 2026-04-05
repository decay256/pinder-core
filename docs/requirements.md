# Pinder.Core — Functional Requirements

## Sprint: Wire GameSession to Rule Engine

### Issue #463 — Wire GameSession to use RuleEngine for §5/§6/§7/§15 rules

#### User Story

As a game designer, I want GameSession to resolve §5/§6/§7/§15 rules through the RuleEngine (loaded from YAML) instead of hardcoded C# constants, so that I can tune game balance by editing YAML without recompilation.

#### Acceptance Criteria

- [ ] `RuleBook` loaded from `rules/extracted/rules-v3-enriched.yaml` at session start
- [ ] §5 failure tier → interest delta flows through the engine
- [ ] §6 interest ranges → InterestState flows through the engine
- [ ] §7 shadow thresholds flow through the engine
- [ ] §15 momentum bonuses flow through the engine
- [ ] §15 risk tier XP multipliers flow through the engine
- [ ] All 45 `RulesSpecTests` assertions pass against the wired implementation
- [ ] All 17 `NotImplementedException` stubs remain as stubs (LLM/qualitative effects)
- [ ] All 2507 existing tests still pass
- [ ] Fallback to hardcoded constants if YAML missing/corrupt
- [ ] Build clean

#### NFR Notes (flag for Architect)

- **Performance**: RuleBook is loaded once at GameSession construction, not per-turn. Rule lookups should be O(1) or O(n) where n is small (< 100 rules per type).
- **Reliability**: Fallback to hardcoded constants if YAML is missing or corrupt — zero regression risk.
- **Dependency**: `Pinder.Rules` project (with YamlDotNet) must NOT be referenced by `Pinder.Core`. The wiring must respect the one-way dependency (`Pinder.Rules → Pinder.Core`). GameSession integration requires either:
  - An abstraction layer (interface) in Core that Rules implements, OR
  - The wiring happens at the session-runner/host level, passing resolved values into GameSession
- **Backward compatibility**: All 2507 existing tests must continue to pass unchanged.

#### Out of Scope

- Wiring LLM/qualitative rules (the 17 skipped stubs)
- Removing hardcoded constants from static classes (they remain as fallback)
- Multi-session (ConversationRegistry) integration
- Changes to Pinder.LlmAdapters

#### Dependencies

- #446 (rule engine exists) — must be merged first

## PM Pre-flight Summary

### Issue #463 — Improved

| Check | Status | Notes |
|-------|--------|-------|
| Acceptance Criteria | ✅ Already present | 11 checkbox items covering load, wiring, tests, fallback |
| Description quality | ✅ Already present | Clear context, phased plan, rationale |
| Role field | ❌ → ✅ Added | `backend-engineer` |
| Maturity field | ❌ → ✅ Added | `prototype` |
| Concern Type | N/A | Not a vision-concern issue |

**Changes made**: Added `**Role**: backend-engineer` and `**Maturity**: prototype` fields to the issue body. Posted a comment documenting the change.

---

## Sprint: Dramatic Arc + Voice Fixes

### Issue #487 — Bug: character voice bleed — both prompts in system causes player to adopt opponent's register

#### User Story

As a player, I want my character's dialogue options to use my character's voice (not the opponent's register) so that each character feels distinct and the dating simulation maintains two separate identities.

#### Acceptance Criteria

- [ ] Given `GetDialogueOptionsAsync` is called, when system blocks are built, then only the player's prompt is in the system block
- [ ] Given opponent context is needed for option generation, when the prompt is assembled, then opponent profile appears in the user message as informational context (not system identity)
- [ ] Given a Velvet vs Sable session, when Velvet's options are generated, then they use Velvet's register (lowercase-with-intent, precise, ironic) not Sable's (omg, 😭, fast-talk)
- [ ] Given `GetOpponentResponseAsync` is called, then behavior is unchanged (already uses opponent-only system)
- [ ] Given `DeliverMessageAsync` is called, then behavior is unchanged (already uses player-only system)
- [ ] Build clean, all tests pass

#### NFR Notes (flag for Architect)

- **Performance**: No additional LLM calls; only restructuring existing prompt assembly
- **Backward compatibility**: Only `GetDialogueOptionsAsync` prompt structure changes; other adapter methods unchanged

#### Out of Scope

- Changes to `GetOpponentResponseAsync` or `DeliverMessageAsync`
- Changes to Pinder.Core game logic
- New tests for LLM output quality (qualitative verification via session playtest)

---

### Issue #489 — Prompt: voice distinctness — explicit texting style constraint before option generation

#### User Story

As a player, I want my character's texting style to be strongly enforced in dialogue options so that the character voice is unmistakable and doesn't drift to generic Gen-Z texting.

#### Acceptance Criteria

- [ ] Given `BuildDialogueOptionsPrompt` is called, when the user message is assembled, then the TEXTING STYLE block is injected verbatim immediately before the task instruction
- [ ] Given `CharacterProfile` is used, when texting style is needed, then `TextingStyleFragment` is accessible
- [ ] Given a Velvet vs Sable session, when options are generated, then Velvet uses her register and Sable uses hers
- [ ] Build clean, all tests pass

#### NFR Notes (flag for Architect)

- **Dependency**: Depends on #487 (voice bleed fix) for full effect — texting style reinforcement works with prompt separation

#### Out of Scope

- Changes to opponent response generation
- Automated voice quality scoring

---

### Issue #490 — Design: opponent is always in opposition below Interest 25

#### User Story

As a player, I want the opponent to maintain varying resistance below Interest 25 so that conversations have dramatic tension and the opponent doesn't feel like a willing collaborator.

#### Acceptance Criteria

- [ ] Given `OpponentResponseInstruction` is used, when the prompt is built, then it includes the fundamental resistance rule (below 25 = not won over)
- [ ] Given `SessionDocumentBuilder.BuildOpponentPrompt` is called with interest level, when interest is 1-4, then resistance descriptor indicates active disengagement
- [ ] Given interest is 10-14, when opponent responds, then response shows warmth with visible holdback
- [ ] Given interest is 21-24, when opponent responds, then resistance is subtle but present
- [ ] Given interest is 25, when opponent responds, then resistance dissolves genuinely
- [ ] Build clean, all tests pass

#### NFR Notes (flag for Architect)

- **LLM prompt quality**: Resistance descriptors are prompt engineering — qualitative verification via session playtest
- **Per-archetype resistance**: Archetype-specific resistance is described but enforcement depends on LLM prompt following

#### Out of Scope

- Mechanical changes to interest system
- New game rules or roll modifications
- Per-archetype resistance as code branching (it's prompt text)

---

### Issue #491 — Prompt: success delivery — additions must improve existing sentiment, not add new ideas

#### User Story

As a player, I want strong roll successes to make my message land better (sharper phrasing, better timing) rather than adding new content I didn't write, so that the reward of a strong roll is visible in message quality.

#### Acceptance Criteria

- [ ] Given `SuccessDeliveryInstruction` in PromptTemplates.cs, when updated, then it specifies margin-based delivery tiers (clean 1-4, strong 5-9, critical/Nat20)
- [ ] Given a strong success (margin 5-9), when the message is delivered, then phrasing is sharpened without introducing new ideas
- [ ] Given a critical success, when the message is delivered, then it lands with precision (not expanded)
- [ ] Given the instruction is applied, when comparing intended vs delivered, then every idea in delivered has a counterpart in intended
- [ ] Build clean, all tests pass

#### NFR Notes (flag for Architect)

- **LLM prompt quality**: Delivery quality is LLM-dependent — qualitative verification via session playtest

#### Out of Scope

- Changes to failure delivery
- Mechanical changes to success scale or interest deltas

---

### Issue #492 — LlmPlayerAgent: Sonnet makes option choices based on character fit and narrative moment

#### User Story

As a playtester, I want an LLM-based player agent that makes character-consistent and narratively interesting choices so that automated sessions produce more realistic and engaging conversations.

#### Acceptance Criteria

- [ ] Given `--agent llm` CLI arg, when session runs, then `LlmPlayerAgent` is used for option selection
- [ ] Given LlmPlayerAgent picks an option, when the result is logged, then reasoning block appears in playtest output
- [ ] Given options with different risk profiles, when LlmPlayerAgent decides, then it can pick bold/callback/combo plays that ScoringAgent would reject (at least once per session)
- [ ] Given LLM call fails, when fallback triggers, then ScoringPlayerAgent is used
- [ ] Build clean, all tests pass

#### NFR Notes (flag for Architect)

- **Performance**: One additional Anthropic API call per turn for agent decision
- **Cost**: Uses claude-sonnet-4-20250514 (not Opus) for speed/cost balance
- **Dependency**: Depends on #346 (IPlayerAgent interface), #489 (voice distinctness)

#### Out of Scope

- Training or fine-tuning the agent
- Automated quality metrics for agent decisions
- Changes to ScoringPlayerAgent logic

---

### Issue #493 — Mechanic: failure degradation should be legible to the opponent

#### User Story

As a player, I want the opponent to react to my failed messages based on how badly the failure was so that failures have dramatic consequences visible through the opponent's response.

#### Acceptance Criteria

- [ ] Given a roll fails, when `OpponentContext` is constructed, then it includes `DeliveryTier` (FailureTier enum, None for success)
- [ ] Given `GameSession` resolves a turn with failure, when opponent context is built, then the roll tier is passed through
- [ ] Given `BuildOpponentPrompt` receives a non-None tier, when the prompt is assembled, then failure context is injected with per-tier guidance
- [ ] Given a Fumble (miss by 1-2), when opponent responds, then response shows slight coolness (not explicit reaction)
- [ ] Given a TropeTrap or Catastrophe, when opponent responds, then response shows visible discomfort or confusion
- [ ] Given a success (tier = None), when opponent responds, then no failure context is injected
- [ ] Per-tier guidance text exists in PromptTemplates
- [ ] Build clean, all tests pass

#### NFR Notes (flag for Architect)

- **DTO change**: `OpponentContext` gains a new optional field — backward-compatible via default
- **LLM prompt quality**: Tier-appropriate reactions are LLM-dependent — qualitative verification via session playtest

#### Out of Scope

- Changes to failure scale or interest deltas
- Player-visible failure diff/comparison
- Changes to `DeliverMessageAsync` behavior

---

## Sprint: Session Runner Bug Fixes

### Issue #513 — Bug: CharacterLoader.ParseBio returns empty — bio lines have no quotes

#### User Story

As a playtester, I want character bios to load correctly from prompt files so that the playtest header displays actual bio text instead of empty strings.

#### Acceptance Criteria

- [ ] Given any of the 5 starter characters, when loaded via CharacterLoader, then ParseBio returns the actual bio text
- [ ] Given a bio line without quotes (e.g. `- Bio: I will absolutely judge your taste in music.`), when parsed, then the full text after `- Bio:` is returned
- [ ] Given a bio line with optional surrounding quotes, when parsed, then quotes are stripped and content returned
- [ ] Build clean, all tests pass

#### NFR Notes (flag for Architect)

- No architectural implications — isolated parser fix in session-runner

#### Out of Scope

- Changes to Pinder.Core
- Changes to CharacterDefinitionLoader (JSON path)

---

### Issue #514 — Bug: DC Reference table header hardcoded as 'Sable attacking, Brick defending'

#### User Story

As a playtester, I want the DC Reference table in playtest output to show the actual character names so that the output is accurate for any character matchup.

#### Acceptance Criteria

- [ ] Given any two characters are loaded, when the DC table is printed, then the header shows their actual names (not hardcoded 'Sable attacking, Brick defending')
- [ ] Given the DC table column header, when printed, then it uses the actual player character name
- [ ] Build clean, all tests pass

#### NFR Notes (flag for Architect)

- No architectural implications — string interpolation fix in session-runner Program.cs

#### Out of Scope

- Changes to DC calculation logic
- Changes to Pinder.Core

---

### Issue #515 — Bug: SessionFileCounter writes session-006 repeatedly

#### User Story

As a playtester, I want each session run to produce a uniquely numbered output file so that previous sessions are not overwritten and session history is preserved.

#### Acceptance Criteria

- [ ] Given N session files exist, when a new session completes, then file is named session-(N+1)
- [ ] Given the session just wrote session-006, when the next session runs, then it writes session-007 (not session-006 again)
- [ ] Given `PINDER_PLAYTESTS_PATH` env var is set, when resolving the output directory, then it is used as the canonical path
- [ ] Build clean, all tests pass

#### NFR Notes (flag for Architect)

- Path mismatch between counter and writer is the likely root cause — needs diagnosis

#### Out of Scope

- Changes to session file content format
- Changes to Pinder.Core

---

### Issue #516 — Bug: CharacterLoader.ParseLevel reads wrong level — Velvet loads as 4 instead of 7

#### User Story

As a playtester, I want characters to load their correct level from prompt files so that stat bonuses and game mechanics reflect the intended character build.

#### Acceptance Criteria

- [ ] Given Velvet's prompt file with `- Level: 7 (Veteran) | Level bonus: +3`, when loaded, then level is 7
- [ ] Given all 5 starter characters, when loaded via CharacterLoader, then each loads their correct level
- [ ] Given a unit test for ParseLevel, when run against all example files, then all pass
- [ ] Build clean, all tests pass

#### NFR Notes (flag for Architect)

- Incorrect level affects stat bonuses, roll calculations, and XP thresholds — high gameplay impact

#### Out of Scope

- Changes to CharacterDefinitionLoader (JSON path)
- Changes to Pinder.Core level/XP logic

---

### Issue #517 — Bug: ScoringPlayerAgent applies combo/tell bonus to options with near-0% success

#### User Story

As a playtester, I want the scoring agent to accurately evaluate option risk so that it doesn't pick high-risk options where combo/tell bonuses mask near-certain failure and trap costs.

#### Acceptance Criteria

- [ ] Given an option with <20% success probability, when combo/tell bonuses are calculated, then they are scaled by actual success probability (not applied at full value)
- [ ] Given an option where miss margin lands in TropeTrap range (6-9), when EV is computed, then trap cost is applied before combo/tell bonuses
- [ ] Given a unit test with 15% success and TropeTrap-range miss, when scored, then EV is lower than same option with 50% success
- [ ] Build clean, all tests pass

#### NFR Notes (flag for Architect)

- **Dependency**: Depends on #416 (ScoringPlayerAgent shadow growth risk)
- Scoring accuracy affects automated playtest quality

#### Out of Scope

- Changes to LlmPlayerAgent
- Changes to Pinder.Core game mechanics
- Changes to combo/tell detection logic
