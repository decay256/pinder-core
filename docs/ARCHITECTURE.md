# Architecture

Developer guide to the Pinder codebase — systems, subsystems, interfaces, and constraints.

---

## 1. Repository Structure

```
pinder-core/
├── src/
│   ├── Pinder.Core/          # Domain model, game loop, interfaces (netstandard2.0)
│   ├── Pinder.LlmAdapters/   # LLM provider integrations + prompt assembly (netstandard2.0)
│   └── Pinder.Rules/         # Data-driven rule resolution from YAML (netstandard2.0)
├── session-runner/            # CLI harness — runs a full game session (net8.0)
├── tests/
│   ├── Pinder.Core.Tests/
│   ├── Pinder.LlmAdapters.Tests/
│   └── Pinder.Rules.Tests/
├── data/                      # YAML + JSON game data files
├── rules/tools/               # Python rules pipeline (YAML ↔ Markdown)
├── design/                    # Design docs + prompt examples
├── contracts/                 # Interface contracts
└── docs/                      # This file
```

## 2. Assembly Map

### Pinder.Core

The domain kernel. Zero external dependencies — no NuGet packages, no I/O.

| Depends on | Nothing |
|---|---|
| **Purpose** | Game loop, stat model, roll engine, interest meter, shadow tracking, traps, XP, combos |
| **Key files** | `Conversation/GameSession.cs` — the game loop orchestrator |
| | `Interfaces/` — ILlmAdapter, IStatefulLlmAdapter, IGameClock, ITrapRegistry, IDiceRoller, IRuleResolver |
| | `Rolls/RollEngine.cs` — stateless d20 resolution |
| | `Stats/StatBlock.cs` — stat model + shadow pairing table |
| | `Conversation/GameClock.cs` — simulated clock with energy + horniness modifiers |
| | `Characters/CharacterProfile.cs` — assembled character data |
| | `Traps/TrapState.cs` — active trap lifecycle tracking |
| | `Progression/XpLedger.cs`, `LevelTable.cs`, `ComboTracker.cs` |

### Pinder.LlmAdapters

Prompt construction and LLM API integration. Depends on Pinder.Core and Pinder.Rules.

| Depends on | Pinder.Core, Pinder.Rules, Newtonsoft.Json, YamlDotNet |
|---|---|
| **Purpose** | Build prompts from game state, call LLM APIs, parse responses |
| **Key files** | `SessionDocumentBuilder.cs` — static prompt builder for all 4 call types |
| | `SessionSystemPromptBuilder.cs` — system prompt assembly from GameDefinition |
| | `StatDeliveryInstructions.cs` — per-stat, per-tier delivery instructions from YAML |
| | `GameDefinition.cs` — game-level creative direction loaded from YAML |
| | `PromptTemplates.cs` — template strings for ENGINE injection blocks |
| | `RollContextBuilder.cs` — YAML-sourced roll flavor text |
| | `Anthropic/AnthropicLlmAdapter.cs` — Claude adapter (implements IStatefulLlmAdapter) |
| | `OpenAi/OpenAiLlmAdapter.cs` — OpenAI-compatible adapter (Groq, Together, OpenRouter, Ollama) |

### Pinder.Rules

Data-driven rule resolution. Loads YAML rule definitions and evaluates conditions at runtime.

| Depends on | Pinder.Core, YamlDotNet |
|---|---|
| **Purpose** | Replace hardcoded constants with YAML-defined rules |
| **Key files** | `RuleBook.cs` — immutable rule collection loaded from YAML |
| | `RuleBookResolver.cs` — implements IRuleResolver, evaluates conditions against loaded rules |
| | `RuleEntry.cs` — single rule: condition + outcome |
| | `ConditionEvaluator.cs` — evaluates rule conditions against game state |

### session-runner

CLI executable that wires everything together and runs a full game session.

| Depends on | Pinder.Core, Pinder.LlmAdapters |
|---|---|
| **Purpose** | Character loading, LLM adapter construction, player agent selection, game loop execution, markdown output |
| **Key files** | `Program.cs` — main entry point, CLI arg parsing, session orchestration |
| | `IPlayerAgent.cs` — decision-making interface for sim agents |
| | `ScoringPlayerAgent.cs` — heuristic-based option scorer (default) |
| | `LlmPlayerAgent.cs` — LLM-powered decision agent |
| | `HumanPlayerAgent.cs` — interactive stdin agent |
| | `MatchupAnalyzer.cs` — pre-session matchup analysis |

## 3. Core Game Loop

A single turn flows through two phases: `StartTurnAsync` (generate options) and `ResolveTurnAsync` (resolve the chosen option). The caller (session-runner or a test) picks the option between phases.

### Turn flow (numbered steps)

1. **StartTurnAsync** — Check end conditions (interest 0/25, ghost trigger at Bored state). Evaluate shadow thresholds for T2+ disadvantage. Draw 3 random stats. Build `DialogueContext` with conversation history, shadow state, traps, horniness, callbacks, tells. Call `ILlmAdapter.GetDialogueOptionsAsync()` → get dialogue options. Apply T3 shadow filters (Fixation forces stat, Denial removes Honesty, Madness inserts unhinged). Peek combos, weakness windows, tell bonuses. Return `TurnStart` with options + state snapshot.

2. **Player picks option** — External to GameSession. The `IPlayerAgent` implementation (scoring heuristic, LLM, or human) examines the options and returns an index.

3. **ResolveTurnAsync(optionIndex)** — Consumes 1 energy from the game clock. Computes external bonuses: tell (+2), callback (distance-based), momentum (streak 3→+2, 5→+3), triple combo (+1).

4. **Roll** — `RollEngine.Resolve()` rolls d20 + stat modifier + level bonus + external bonus vs DC (16 + opponent defending stat). Advantage/disadvantage from interest state, shadow T2+, or crit carry-over.

5. **Interest delta** — Success: base delta from beat margin (+1 to +4) + risk tier bonus. Failure: negative delta from miss margin (−1 to −3). Combo bonus added. Interest meter updated (range 0–25).

6. **Momentum** — Success increments streak, failure resets to 0. Bonus was pre-computed and applied as `externalBonus` in step 4.

7. **Nat 20 effects** — Sets `_pendingCritAdvantage` for next roll. Chaos Nat 20 → Madness −1. Any Nat 20 → Dread −1.

8. **Delivery** — Build `DeliveryContext`. Call `ILlmAdapter.DeliverMessageAsync()` → returns the player's message text with outcome-appropriate degradation (success improves phrasing, failure corrupts it).

9. **Steering roll** — Separate RNG. DC = 16 + average(opponent SA, Rizz, Honesty). On success, calls `IStatefulLlmAdapter.GetSteeringQuestionAsync()` → appends a date-nudge question.

10. **Horniness check** — Separate RNG. DC = 20 − session horniness. On miss, calls `ILlmAdapter.ApplyHorninessOverlayAsync()` → rewrites the message with involuntary heat.

11. **Shadow growth** — `EvaluatePerTurnShadowGrowth()` evaluates 15+ triggers: Nat 1 → paired shadow +1, same stat 3× → Fixation +1, Charm 3× → Madness +1, RIZZ failures → Despair, etc. Also evaluates reductions (combo success → Madness −1, Honesty success at high interest → Denial −1).

12. **Opponent response** — Build `OpponentContext` with full conversation history, interest narrative, resistance level, delivery tier, shadow taint. Call `ILlmAdapter.GetOpponentResponseAsync()` → returns message + optional weakness window + tell.

13. **Cleanup** — Advance trap timers. Increment turn. Clear stored options. Return `TurnResult` with roll, messages, interest delta, shadow events, combo/callback/tell info.

## 4. Key Interfaces

### ILlmAdapter

Core abstraction for all LLM interactions. Stateless per-call.

| Method | Purpose |
|---|---|
| `GetDialogueOptionsAsync(DialogueContext)` | Generate 3–4 dialogue options for the player |
| `DeliverMessageAsync(DeliveryContext)` | Deliver chosen option with outcome degradation |
| `GetOpponentResponseAsync(OpponentContext)` | Generate opponent's reply |
| `GetInterestChangeBeatAsync(InterestChangeContext)` | Narrative beat on interest threshold crossing |
| `ApplyHorninessOverlayAsync(message, instruction)` | Rewrite message with horniness overlay |

**Implementations:** `AnthropicLlmAdapter`, `OpenAiLlmAdapter` (both in Pinder.LlmAdapters)

### IStatefulLlmAdapter : ILlmAdapter

Extends ILlmAdapter with persistent opponent session for memory continuity across turns. Options and delivery remain stateless to prevent voice bleed between player/opponent roles.

| Method | Purpose |
|---|---|
| `StartOpponentSession(systemPrompt)` | Initialize persistent opponent conversation |
| `GetSteeringQuestionAsync(SteeringContext)` | Generate steering question after successful roll |

**Implementations:** `AnthropicLlmAdapter`, `OpenAiLlmAdapter`

### IPlayerAgent

Decision-making interface for sim agents. Lives in session-runner (not Core).

| Method | Purpose |
|---|---|
| `DecideAsync(TurnStart, PlayerAgentContext)` | Pick an option index with reasoning + score breakdowns |

**Implementations:** `ScoringPlayerAgent` (heuristic), `LlmPlayerAgent` (Claude-powered), `HumanPlayerAgent` (stdin), `HighestModAgent` (always picks best modifier)

### IGameClock

Simulated in-game clock with energy budget and time-of-day mechanics.

| Method | Purpose |
|---|---|
| `Now` | Current simulated time |
| `Advance(TimeSpan)` | Advance clock |
| `GetTimeOfDay()` | Return time bucket (Morning/Afternoon/Evening/LateNight/AfterTwoAm) |
| `GetHorninessModifier()` | Time-based horniness modifier (−2 to +5) |
| `ConsumeEnergy(amount)` | Deduct energy; returns false if insufficient |

**Implementation:** `GameClock` (in Pinder.Core). Tests use `FixedGameClock`.

### ITrapRegistry

Supplies trap definitions by stat.

| Method | Purpose |
|---|---|
| `GetTrap(StatType)` | Get trap definition for a stat, or null |
| `GetLlmInstruction(StatType)` | Get LLM prompt taint text for a stat's trap |

**Implementations:** `JsonTrapRepository` (loads from `data/traps/traps.json`), `NullTrapRegistry` (session-runner fallback)

### IDiceRoller

Dice abstraction for deterministic testing.

| Method | Purpose |
|---|---|
| `Roll(int sides)` | Return 1..sides |

**Implementations:** `SystemRandomDiceRoller` (production), `BiasedDiceRoller` (testing — queued values)

### IRuleResolver

Data-driven game constant resolution. When injected, GameSession calls it first and falls back to hardcoded values when it returns null.

| Method | Purpose |
|---|---|
| `GetFailureInterestDelta(missMargin, naturalRoll)` | §5 failure → interest delta |
| `GetSuccessInterestDelta(beatMargin, naturalRoll)` | §5 success → interest delta |
| `GetInterestState(interest)` | §6 interest → state mapping |
| `GetShadowThresholdLevel(shadowValue)` | §7 shadow → tier (0/1/2/3) |
| `GetMomentumBonus(streak)` | §15 streak → roll bonus |
| `GetRiskTierXpMultiplier(riskTier)` | §15 risk → XP multiplier |

**Implementation:** `RuleBookResolver` (in Pinder.Rules)

## 5. Data Files

| File | Purpose | Loaded by | Key fields |
|---|---|---|---|
| `data/game-definition.yaml` | Game-level creative direction, writing rules, meta contract | `GameDefinition.LoadFrom()` | `vision`, `writing_rules`, `meta_contract`, `horniness_time_modifiers`, `improvement_prompt`, `steering_prompt` |
| `data/delivery-instructions.yaml` | Per-stat, per-tier LLM delivery prompts | `StatDeliveryInstructions.LoadFrom()` | `delivery_instructions.<stat>.<tier>` (clean/strong/critical/exceptional/nat20/fumble/misfire/trope_trap/catastrophe/nat1) |
| `data/traps/traps.json` | Trap definitions per stat | `JsonTrapRepository` / `TrapRegistryLoader` | `stat`, `effect`, `effectValue`, `duration`, `llmInstruction` |
| `data/traps/trap-schema.json` | JSON schema for trap files | Validation only | — |
| `data/characters/*.json` | Character definitions (items, anatomy, stats) | `CharacterDefinitionLoader` | `displayName`, `stats`, `equippedItems`, `anatomy`, `textingStyle` |
| `data/items/starter-items.json` | Item catalog (stat modifiers, fragments) | `JsonItemRepository` | `id`, `statModifiers`, `promptFragment` |
| `data/anatomy/anatomy-parameters.json` | Anatomy options (stat modifiers, fragments) | `JsonAnatomyRepository` | `id`, `statModifiers`, `promptFragment` |
| `data/timing/response-profiles.json` | Opponent response delay curves | `CharacterProfile.Timing` | `baseDelay`, `interestMultiplier` |

## 6. LLM Call Types

GameSession makes 4 primary LLM calls per turn (plus 2 conditional), all routed through `ILlmAdapter`. Prompts are assembled by `SessionDocumentBuilder`.

### 1. Dialogue Options (`GetDialogueOptionsAsync`)

| Aspect | Detail |
|---|---|
| **Context** | Player system prompt, opponent visible profile (name + bio only), full conversation history, shadow thresholds, active traps + LLM instructions, horniness level, callback opportunities, active tell, archetype directive, 3 drawn stats |
| **Returns** | Array of `DialogueOption` (stat, intended text, optional callback turn) |
| **Prompt builder** | `SessionDocumentBuilder.BuildDialogueOptionsPrompt()` |

### 2. Delivery (`DeliverMessageAsync`)

| Aspect | Detail |
|---|---|
| **Context** | Player + opponent prompts, conversation history, chosen option, roll outcome (success tier / failure tier + miss margin), beat-DC-by, active traps, shadow thresholds, stat-specific delivery instruction |
| **Returns** | `string` — the delivered message text (improved on success, corrupted on failure) |
| **Prompt builder** | `SessionDocumentBuilder.BuildDeliveryPrompt()` |

### 3. Opponent Response (`GetOpponentResponseAsync`)

| Aspect | Detail |
|---|---|
| **Context** | Both prompts, full history, player's delivered message, interest before/after, response delay, active traps, opponent shadow thresholds, delivery tier, archetype directive, resistance level |
| **Returns** | `OpponentResponse` — message text + optional `WeaknessWindow` + optional `Tell` |
| **Prompt builder** | `SessionDocumentBuilder.BuildOpponentPrompt()` |

### 4. Improvement Pass (two-stage rewrite)

Configured via `GameDefinition.ImprovementPrompt`. Appended after initial generation to trigger self-critique and rewrite. The output **must be same length or shorter** — hard rule in the prompt.

### 5. Steering Question (`GetSteeringQuestionAsync`) — conditional

Called only on successful steering roll via `IStatefulLlmAdapter`. Receives player/opponent names, delivered message, conversation history. Returns a single question to append.

### 6. Horniness Overlay (`ApplyHorninessOverlayAsync`) — conditional

Called on failed horniness check. Receives the delivered message + tier-specific overlay instruction. Returns the rewritten message with involuntary heat.

## 7. Constraints & Best Practices

### Language & framework
- **netstandard2.0 / C# 8.0** — no `record` types, no `init`-only setters, no `is not` pattern, no top-level statements in library projects
- session-runner targets **net8.0** but still uses **LangVersion 8.0**
- **Newtonsoft.Json** (not System.Text.Json) — already a dependency in LlmAdapters
- **YamlDotNet** for all YAML parsing

### No silent fallbacks
- Missing config **throws immediately** (`ArgumentNullException`, `InvalidOperationException`, `FormatException`)
- No `?? default` patterns for required configuration
- `GameClock` is required — constructor throws if null
- `GameDefinition.LoadFrom()` throws on missing required keys
- `IRuleResolver` returns null → caller uses hardcoded fallback (this is the one intentional fallback pattern)

### Source of truth hierarchy
1. **`GameSession.cs`** — the code is the canonical specification
2. **Rules YAML** — data-driven rules loaded by RuleBookResolver
3. **Documentation** — describes intent, not behavior

### Delivery rules
- **Failure delivery:** corrupt from within — never append new content. Same length or shorter
- **Success delivery:** improve phrasing — never add new ideas, topics, or emotional content
- **Improvement pass:** output must be same length or shorter — hard rule in prompt

### Shadow growth
- Triggered by **stat usage patterns** and **roll outcomes** — never by explicit player choice
- Shadow growth evaluation happens in `EvaluatePerTurnShadowGrowth()` and `EvaluateEndOfGameShadowGrowth()`
- Shadow reductions are intentional design — combos, tell reads, high-interest successes can reduce shadows

## Progression

**Level Bonus**: Each level grants a flat +1 bonus to ALL rolls (not per-stat). This is intentional: players outscale opponent DCs over time, but shadows remain threatening even at high level because they eat into the level bonus. Keeping it flat (not per-stat) avoids complex stat-specific levelling decisions and keeps progression simple.

### Testing
- **0 test failures before any merge**
- Deterministic testing via `BiasedDiceRoller` (queued dice values) and `FixedGameClock`
- `InternalsVisibleTo` is set for all test assemblies

## 8. Rules Pipeline

The Python pipeline in `rules/tools/` converts between YAML and Markdown representations of game rules.

| Command | What it does |
|---|---|
| `rules_pipeline.py yaml-to-md` | Convert `rules-v3-enriched.yaml` → `rules-v3.md` |
| `rules_pipeline.py md-to-yaml` | Convert `rules-v3.md` → `rules-v3-enriched.yaml` |
| `rules_pipeline.py check` | Round-trip verification — report diff count |
| `rules_pipeline.py check-diff` | Round-trip + LLM classification (FORMATTING_ONLY / CONTENT_LOSS) |
| `rules_pipeline.py enrich` | Enrichment pass on extracted YAML |
| `rules_pipeline.py extract` | Extract YAML from raw Markdown |
| `rules_pipeline.py game-def` | Generate `game-definition.md` from `game-definition.yaml` |
| `rules_pipeline.py test` | Run all pipeline tests |

### When to run rules tests

```bash
dotnet test --filter "Category=Rules"
```

Run after any change to YAML rule files or to `Pinder.Rules` code.

### Source of truth for rules
1. **Code** (`GameSession.cs`, `RollEngine.cs`) — always wins
2. **YAML** (`rules-v3-enriched.yaml`) — data-driven overrides
3. **Markdown** (`rules-v3.md`) — human-readable documentation, generated from YAML

## 9. Test Categories

Tests use category attributes for selective execution:

```bash
dotnet test --filter "Category=Core"          # Core game logic — fast iteration
dotnet test --filter "Category=Rules"         # Rules pipeline + YAML resolution
dotnet test --filter "Category=LlmAdapters"   # Prompt builder + adapter tests
dotnet test                                    # Everything
```

Categories are additive — a test can belong to multiple categories. Use `Category=Core` during development for fast feedback loops.
