# Architecture

Developer guide to the Pinder codebase — systems, subsystems, interfaces, and constraints.

---

## 1. Repository Structure

```
pinder-core/
├── src/
│   ├── Pinder.Core/          # Domain model, game loop, interfaces (netstandard2.0)
│   ├── Pinder.LlmAdapters/   # LLM provider integrations + prompt assembly (netstandard2.0)
│   ├── Pinder.Rules/         # Data-driven rule resolution from YAML (netstandard2.0)
│   └── Pinder.SessionSetup/  # Pre-session matchup + stake generation (netstandard2.0)
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

Consumers: the `session-runner` CLI harness uses Core + LlmAdapters + Rules.
The web-tier `Pinder.GameApi` (separate repo `pinder-web`, mounts this repo as
a git submodule) additionally uses `Pinder.SessionSetup` for matchup + stake
generation outside the per-turn game loop.

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

### Pinder.SessionSetup

Pre-session setup: narrative matchup analysis + stake generation. Isolated
from the per-turn game loop so that the web tier (`Pinder.GameApi`) can run
it eagerly in the background after session create, while the CLI harness
skips it or runs a lighter version.

| Depends on | Pinder.Core, Pinder.LlmAdapters (via ILlmTransport) |
|---|---|
| **Purpose** | Matchup preview + stake copy at session boot time |
| **Key files** | `IMatchupAnalyzer.cs` / `LlmMatchupAnalyzer.cs` — matchup narrative |
| | `IStakeGenerator.cs` / `LlmStakeGenerator.cs` — player + opponent stake strings |
| | `CharacterDefinitionLoader.cs` — shared character JSON loader |

Ported into this library in #756 (`569b9f9`). Previously inlined in
`session-runner/MatchupAnalyzer.cs`; still referenced there via the new
interface for CLI use.

### session-runner

CLI executable that wires everything together and runs a full game session.

| Depends on | Pinder.Core, Pinder.LlmAdapters, Pinder.SessionSetup |
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

The engine deliberately exposes narrow, single-responsibility interfaces so
different consumers (CLI sim runner, web API, tests) can plug in adapters
without reaching into implementation details. The canonical extension points
are:

| Interface | Owner | Purpose |
|---|---|---|
| `ILlmTransport` | Pinder.Core | HTTP wire adapter for an LLM provider — the lowest-level swap point |
| `ILlmAdapter` / `IStatefulLlmAdapter` | Pinder.LlmAdapters | Turn-time prompt assembly + parsing |
| `ITrapRegistry` | Pinder.Core | Supplies trap definitions by stat |
| `IDiceRoller` | Pinder.Core | Deterministic roll injection for tests |
| `IRuleResolver` | Pinder.Rules | YAML-driven constant lookup |
| `IMatchupAnalyzer` | Pinder.SessionSetup | Pre-session matchup narrative |
| `IStakeGenerator` | Pinder.SessionSetup | Pre-session stake generation |
| `IPlayerAgent` | session-runner | Sim-agent decision-making |

### ILlmTransport

Low-level HTTP transport abstraction. Sits underneath `ILlmAdapter`. Tests
inject `RecordingLlmTransport` / `PlaybackLlmTransport` (in pinder-web
`Pinder.GameApi.Tests.Infrastructure`) to capture and replay live LLM traffic
deterministically — no network calls in CI.

| Method | Purpose |
|---|---|
| `SendAsync(LlmRequest)` | Send a raw prompt, receive a raw response |

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

## 4a. Roll Outcomes — Risk Tiers and Failure Tiers

Rolls resolve to a `RollResult` carrying both success/failure classification
and a semantic tier. Interest deltas and XP multipliers are driven by tier,
not raw margin.

### Success path (RiskTier)

Attack d20 + stat mod + level bonus + external bonuses vs DC (16 + opponent
defending stat). Risk tier is derived from the attempt's **effective need**
(i.e., the gap between the player's modifier total and the DC) — not from the
post-roll margin. This keeps "risk" a property of the choice, not of luck.

| Risk tier     | When                                       |
|---------------|--------------------------------------------|
| `Safe`        | Needed ≤ 5 on d20                          |
| `Moderate`    | Needed 6–10                                |
| `Risky`       | Needed 11–15                               |
| `Desperate`   | Needed 16–19                               |
| `Legendary`   | Needed 20 (only a nat 20 succeeds)         |

Success interest delta is `baseDelta(beatMargin) + riskTierBonus` with the
bonus scaling with risk. See `Pinder.Core/Rolls/RollEngine.cs` and
`RuleBookResolver.GetSuccessInterestDelta`.

### Failure path (FailureTier)

Failures are tiered by **miss margin** and natural roll. Delivery instructions
are tier-specific — a `Fumble` corrupts differently from a `Catastrophe`.

| Failure tier   | Trigger                                |
|----------------|----------------------------------------|
| `None`         | Success                                |
| `Fumble`       | Miss by 1–2                            |
| `Misfire`      | Miss by 3–5                            |
| `TropeTrap`    | Miss by 6–10 (or active trap hit)      |
| `Catastrophe`  | Miss by 11+                            |
| `Legendary`    | Nat 1 on a desperate/legendary attempt |

See `Pinder.Core/Rolls/FailureScale.cs` for the mapping used by
`GetFailureInterestDelta`.

### Stat model (6 + 6)

Six primary stats — **Chaos, Honesty, Rizz, SA, Charm, HighBrow** — each
paired with a shadow counterpart (Madness, Denial, Despair, Fixation,
Hollow, Obsession). Shadows corrupt by stat-usage patterns (§7) and, past
threshold T2, impose disadvantage on the paired stat's rolls.

Roll formula (attempt side): `d20 + stat_mod + level_bonus + external_bonus`
vs DC `16 + opponent_defending_stat`. `level_bonus` is a flat +1 per level
applied to all rolls — progression keeps scaling simple; shadows are the
threat that actually eats into it.

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

> When a horniness overlay fires, if interest > 0, it is halved (floor). This represents the character losing track of the connection in the heat of arousal.

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

## 10. Onboarding Reading Order

For an agent / engineer picking up this repo cold, read in this order:

1. **Game design context** — `design/game-definition.md` (what the game is)
2. **This doc** — full systems map
3. **`src/Pinder.Core/Conversation/GameSession.cs`** — the game loop (1,197 lines, canonical spec)
4. **`src/Pinder.Core/Rolls/RollEngine.cs`** — roll + risk-tier + failure-tier math
5. **`src/Pinder.Core/Stats/StatBlock.cs`** — stat + shadow model
6. **`docs/modules/game-session.md`** — loop narrative
7. **`docs/modules/rolls.md`** — rolls deep dive
8. **`docs/modules/llm-adapters.md`** — prompt construction + call types
9. **`docs/modules/rules-dsl.md`** + `docs/modules/rule-engine.md` — YAML-driven rules
10. **`data/game-definition.yaml`** + `data/delivery-instructions.yaml` — creative direction + per-stat delivery prompts
11. **`session-runner/Program.cs`** — the canonical wiring example
12. **`rules/tools/README.md`** — rules pipeline if touching YAML

When working from the web tier (`pinder-web`), also read that repo's
`docs/ARCHITECTURE.md` for how `Pinder.GameApi` wraps this engine.
