using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Data;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Spec-driven tests for Issue #52: Trap Taint Injection.
    /// Tests verify behaviors documented in docs/specs/issue-52-spec.md.
    /// Maturity: Prototype — happy-path per acceptance criterion.
    /// </summary>
    public class TrapTaintInjectionSpecTests
    {
        #region JSON Fixtures

        private const string SingleCharmTrapJson = @"[
  {
    ""id"": ""charm_trap_cringe"",
    ""stat"": ""charm"",
    ""effect"": ""disadvantage"",
    ""effect_value"": 0,
    ""duration_turns"": 3,
    ""llm_instruction"": ""Be painfully over-eager and cringy."",
    ""clear_method"": ""Succeed on a Charm roll"",
    ""nat1_bonus"": ""Send unsolicited selfie.""
  }
]";

        private const string TwoTrapJson = @"[
  {
    ""id"": ""charm_trap_cringe"",
    ""stat"": ""charm"",
    ""effect"": ""disadvantage"",
    ""effect_value"": 0,
    ""duration_turns"": 3,
    ""llm_instruction"": ""Be painfully over-eager and cringy."",
    ""clear_method"": ""Succeed on a Charm roll"",
    ""nat1_bonus"": ""Send unsolicited selfie.""
  },
  {
    ""id"": ""wit_trap_ramble"",
    ""stat"": ""wit"",
    ""effect"": ""stat_penalty"",
    ""effect_value"": 2,
    ""duration_turns"": 2,
    ""llm_instruction"": ""Ramble endlessly about nothing."",
    ""clear_method"": ""Succeed on a Wit roll"",
    ""nat1_bonus"": ""Send 500-word essay about cat.""
  }
]";

        private const string AllStatsTrapJson = @"[
  { ""id"": ""charm_trap"", ""stat"": ""charm"", ""effect"": ""disadvantage"", ""effect_value"": 0, ""duration_turns"": 3, ""llm_instruction"": ""Charm instruction"", ""clear_method"": ""clear"", ""nat1_bonus"": ""bonus"" },
  { ""id"": ""rizz_trap"", ""stat"": ""rizz"", ""effect"": ""stat_penalty"", ""effect_value"": 1, ""duration_turns"": 2, ""llm_instruction"": ""Rizz instruction"", ""clear_method"": ""clear"", ""nat1_bonus"": ""bonus"" },
  { ""id"": ""honesty_trap"", ""stat"": ""honesty"", ""effect"": ""opponent_dc_increase"", ""effect_value"": 2, ""duration_turns"": 4, ""llm_instruction"": ""Honesty instruction"", ""clear_method"": ""clear"", ""nat1_bonus"": ""bonus"" },
  { ""id"": ""chaos_trap"", ""stat"": ""chaos"", ""effect"": ""disadvantage"", ""effect_value"": 0, ""duration_turns"": 1, ""llm_instruction"": ""Chaos instruction"", ""clear_method"": ""clear"", ""nat1_bonus"": ""bonus"" },
  { ""id"": ""wit_trap"", ""stat"": ""wit"", ""effect"": ""stat_penalty"", ""effect_value"": 3, ""duration_turns"": 2, ""llm_instruction"": ""Wit instruction"", ""clear_method"": ""clear"", ""nat1_bonus"": ""bonus"" },
  { ""id"": ""sa_trap"", ""stat"": ""self_awareness"", ""effect"": ""disadvantage"", ""effect_value"": 0, ""duration_turns"": 2, ""llm_instruction"": ""SA instruction"", ""clear_method"": ""clear"", ""nat1_bonus"": ""bonus"" }
]";

        private const string CustomOverrideJson = @"[
  {
    ""id"": ""charm_trap_custom"",
    ""stat"": ""charm"",
    ""effect"": ""stat_penalty"",
    ""effect_value"": 3,
    ""duration_turns"": 5,
    ""llm_instruction"": ""Custom charm instruction."",
    ""clear_method"": ""Custom clear"",
    ""nat1_bonus"": ""Custom bonus""
  }
]";

        #endregion

        #region AC1: JsonTrapRepository loads trap data

        // What: AC1 — JsonTrapRepository parses JSON array into TrapDefinition instances
        // Mutation: would catch if constructor ignored JSON input and returned empty collection
        [Fact]
        public void AC1_Constructor_ParsesJsonArray_IntoTrapDefinitions()
        {
            var repo = new JsonTrapRepository(SingleCharmTrapJson);
            var trap = repo.GetTrap(StatType.Charm);

            Assert.NotNull(trap);
            Assert.Equal("charm_trap_cringe", trap!.Id);
            Assert.Equal(StatType.Charm, trap.Stat);
            Assert.Equal(TrapEffect.Disadvantage, trap.Effect);
            Assert.Equal(0, trap.EffectValue);
            Assert.Equal(3, trap.DurationTurns);
            Assert.Equal("Be painfully over-eager and cringy.", trap.LlmInstruction);
            Assert.Equal("Succeed on a Charm roll", trap.ClearMethod);
            Assert.Equal("Send unsolicited selfie.", trap.Nat1Bonus);
        }

        // What: AC1 — All 8 TrapDefinition fields populated correctly
        // Mutation: would catch if any field was swapped or defaulted
        [Fact]
        public void AC1_AllFieldsPopulated_FromJson()
        {
            var repo = new JsonTrapRepository(TwoTrapJson);
            var witTrap = repo.GetTrap(StatType.Wit);

            Assert.NotNull(witTrap);
            Assert.Equal("wit_trap_ramble", witTrap!.Id);
            Assert.Equal(StatType.Wit, witTrap.Stat);
            Assert.Equal(TrapEffect.StatPenalty, witTrap.Effect);
            Assert.Equal(2, witTrap.EffectValue);
            Assert.Equal(2, witTrap.DurationTurns);
            Assert.Equal("Ramble endlessly about nothing.", witTrap.LlmInstruction);
            Assert.Equal("Succeed on a Wit roll", witTrap.ClearMethod);
            Assert.Equal("Send 500-word essay about cat.", witTrap.Nat1Bonus);
        }

        // What: AC1 — LoadAdditional (via constructor overload) merges custom traps into repository
        // Mutation: would catch if custom files were ignored
        [Fact]
        public void AC1_LoadAdditional_MergesCustomTraps()
        {
            var customJson = @"[
  {
    ""id"": ""rizz_trap_smooth"",
    ""stat"": ""rizz"",
    ""effect"": ""stat_penalty"",
    ""effect_value"": 1,
    ""duration_turns"": 2,
    ""llm_instruction"": ""Try too hard to be smooth."",
    ""clear_method"": ""Succeed on Rizz"",
    ""nat1_bonus"": ""Rizz bonus""
  }
]";
            var repo = new JsonTrapRepository(SingleCharmTrapJson, new[] { customJson });

            Assert.NotNull(repo.GetTrap(StatType.Charm));
            Assert.NotNull(repo.GetTrap(StatType.Rizz));
            Assert.Equal("Try too hard to be smooth.", repo.GetTrap(StatType.Rizz)!.LlmInstruction);
        }

        // What: AC1 — Custom trap overwrites base trap for same stat (last-write-wins)
        // Mutation: would catch if first-write-wins or duplicate error
        [Fact]
        public void AC1_CustomOverwritesBase_LastWriteWins()
        {
            var repo = new JsonTrapRepository(SingleCharmTrapJson, new[] { CustomOverrideJson });

            var charm = repo.GetTrap(StatType.Charm);
            Assert.NotNull(charm);
            Assert.Equal("charm_trap_custom", charm!.Id);
            Assert.Equal("Custom charm instruction.", charm.LlmInstruction);
            Assert.Equal(TrapEffect.StatPenalty, charm.Effect);
        }

        // What: AC1 — GetAll returns all loaded traps
        // Mutation: would catch if GetAll returned empty or subset
        [Fact]
        public void AC1_GetAll_ReturnsAllLoadedTraps()
        {
            var repo = new JsonTrapRepository(TwoTrapJson);
            var all = repo.GetAll().ToList();

            Assert.Equal(2, all.Count);
            Assert.Contains(all, t => t.Stat == StatType.Charm);
            Assert.Contains(all, t => t.Stat == StatType.Wit);
        }

        // What: AC1 — Parses all six stat types correctly
        // Mutation: would catch if any StatType enum value was not handled in parsing
        [Fact]
        public void AC1_ParsesAllSixStatTypes()
        {
            var repo = new JsonTrapRepository(AllStatsTrapJson);

            Assert.NotNull(repo.GetTrap(StatType.Charm));
            Assert.NotNull(repo.GetTrap(StatType.Rizz));
            Assert.NotNull(repo.GetTrap(StatType.Honesty));
            Assert.NotNull(repo.GetTrap(StatType.Chaos));
            Assert.NotNull(repo.GetTrap(StatType.Wit));
            Assert.NotNull(repo.GetTrap(StatType.SelfAwareness));
        }

        // What: AC1 — Parses all three TrapEffect enum values
        // Mutation: would catch if a TrapEffect parsing branch was missing
        [Fact]
        public void AC1_ParsesAllThreeTrapEffects()
        {
            var json = @"[
  { ""id"": ""t1"", ""stat"": ""charm"", ""effect"": ""disadvantage"", ""effect_value"": 0, ""duration_turns"": 1, ""llm_instruction"": ""i1"", ""clear_method"": ""c"", ""nat1_bonus"": ""b"" },
  { ""id"": ""t2"", ""stat"": ""rizz"", ""effect"": ""stat_penalty"", ""effect_value"": 2, ""duration_turns"": 1, ""llm_instruction"": ""i2"", ""clear_method"": ""c"", ""nat1_bonus"": ""b"" },
  { ""id"": ""t3"", ""stat"": ""wit"", ""effect"": ""opponent_dc_increase"", ""effect_value"": 3, ""duration_turns"": 1, ""llm_instruction"": ""i3"", ""clear_method"": ""c"", ""nat1_bonus"": ""b"" }
]";
            var repo = new JsonTrapRepository(json);

            Assert.Equal(TrapEffect.Disadvantage, repo.GetTrap(StatType.Charm)!.Effect);
            Assert.Equal(TrapEffect.StatPenalty, repo.GetTrap(StatType.Rizz)!.Effect);
            Assert.Equal(TrapEffect.OpponentDCIncrease, repo.GetTrap(StatType.Wit)!.Effect);
        }

        #endregion

        #region AC2: ITrapRegistry exposes GetLlmInstruction

        // What: AC2 — GetLlmInstruction returns instruction text for defined stat
        // Mutation: would catch if method returned Id instead of LlmInstruction
        [Fact]
        public void AC2_GetLlmInstruction_ReturnsInstructionText()
        {
            var repo = new JsonTrapRepository(SingleCharmTrapJson);
            string? instruction = repo.GetLlmInstruction(StatType.Charm);

            Assert.Equal("Be painfully over-eager and cringy.", instruction);
        }

        // What: AC2 — GetLlmInstruction returns null for undefined stat
        // Mutation: would catch if method threw instead of returning null
        [Fact]
        public void AC2_GetLlmInstruction_ReturnsNull_ForUndefinedStat()
        {
            var repo = new JsonTrapRepository(SingleCharmTrapJson);
            string? instruction = repo.GetLlmInstruction(StatType.Rizz);

            Assert.Null(instruction);
        }

        // What: AC2 — GetLlmInstruction and GetTrap?.LlmInstruction are equivalent
        // Mutation: would catch if GetLlmInstruction diverged from GetTrap logic
        [Fact]
        public void AC2_GetLlmInstruction_EquivalentToGetTrapLlmInstruction()
        {
            var repo = new JsonTrapRepository(TwoTrapJson);

            foreach (var stat in new[] { StatType.Charm, StatType.Wit, StatType.Rizz })
            {
                var directResult = repo.GetLlmInstruction(stat);
                var indirectResult = repo.GetTrap(stat)?.LlmInstruction;
                Assert.Equal(indirectResult, directResult);
            }
        }

        #endregion

        #region AC3: Context types carry ActiveTrapInstructions

        // What: AC3 — DialogueContext stores ActiveTrapInstructions
        // Mutation: would catch if constructor ignored the activeTrapInstructions parameter
        [Fact]
        public void AC3_DialogueContext_CarriesActiveTrapInstructions()
        {
            var instructions = new[] { "Be cringy.", "Ramble endlessly." };
            var ctx = new DialogueContext(
                playerPrompt: "p",
                opponentPrompt: "o",
                conversationHistory: new List<(string, string)>(),
                opponentLastMessage: "",
                activeTraps: new List<string> { "charm_trap", "wit_trap" },
                currentInterest: 10,
                activeTrapInstructions: instructions
            );

            Assert.NotNull(ctx.ActiveTrapInstructions);
            Assert.Equal(2, ctx.ActiveTrapInstructions!.Length);
            Assert.Equal("Be cringy.", ctx.ActiveTrapInstructions[0]);
            Assert.Equal("Ramble endlessly.", ctx.ActiveTrapInstructions[1]);
        }

        // What: AC3 — DeliveryContext stores ActiveTrapInstructions
        // Mutation: would catch if DeliveryContext didn't accept/store instructions
        [Fact]
        public void AC3_DeliveryContext_CarriesActiveTrapInstructions()
        {
            var instructions = new[] { "Be cringy." };
            var option = new DialogueOption(StatType.Charm, "Hello!");
            var ctx = new DeliveryContext(
                playerPrompt: "p",
                opponentPrompt: "o",
                conversationHistory: new List<(string, string)>(),
                opponentLastMessage: "",
                chosenOption: option,
                outcome: FailureTier.None,
                beatDcBy: 3,
                activeTraps: new List<string> { "charm_trap" },
                activeTrapInstructions: instructions
            );

            Assert.NotNull(ctx.ActiveTrapInstructions);
            Assert.Single(ctx.ActiveTrapInstructions!);
            Assert.Equal("Be cringy.", ctx.ActiveTrapInstructions![0]);
        }

        // What: AC3 — OpponentContext stores ActiveTrapInstructions
        // Mutation: would catch if OpponentContext didn't accept/store instructions
        [Fact]
        public void AC3_OpponentContext_CarriesActiveTrapInstructions()
        {
            var instructions = new[] { "Be cringy.", "Ramble." };
            var ctx = new OpponentContext(
                playerPrompt: "p",
                opponentPrompt: "o",
                conversationHistory: new List<(string, string)>(),
                opponentLastMessage: "",
                activeTraps: new List<string> { "charm_trap", "wit_trap" },
                currentInterest: 10,
                playerDeliveredMessage: "Hello!",
                interestBefore: 10,
                interestAfter: 11,
                responseDelayMinutes: 1.5,
                activeTrapInstructions: instructions
            );

            Assert.NotNull(ctx.ActiveTrapInstructions);
            Assert.Equal(2, ctx.ActiveTrapInstructions!.Length);
            Assert.Equal("Be cringy.", ctx.ActiveTrapInstructions[0]);
            Assert.Equal("Ramble.", ctx.ActiveTrapInstructions[1]);
        }

        // What: AC3 — Context types accept null for ActiveTrapInstructions (backward compat)
        // Mutation: would catch if null was rejected when it should be optional
        [Fact]
        public void AC3_ContextTypes_AcceptNullInstructions()
        {
            var dialogue = new DialogueContext(
                playerPrompt: "p",
                opponentPrompt: "o",
                conversationHistory: new List<(string, string)>(),
                opponentLastMessage: "",
                activeTraps: new List<string>(),
                currentInterest: 10,
                activeTrapInstructions: null
            );
            Assert.Null(dialogue.ActiveTrapInstructions);

            var option = new DialogueOption(StatType.Charm, "Hi");
            var delivery = new DeliveryContext(
                playerPrompt: "p",
                opponentPrompt: "o",
                conversationHistory: new List<(string, string)>(),
                opponentLastMessage: "",
                chosenOption: option,
                outcome: FailureTier.None,
                beatDcBy: 0,
                activeTraps: new List<string>(),
                activeTrapInstructions: null
            );
            Assert.Null(delivery.ActiveTrapInstructions);

            var opponent = new OpponentContext(
                playerPrompt: "p",
                opponentPrompt: "o",
                conversationHistory: new List<(string, string)>(),
                opponentLastMessage: "",
                activeTraps: new List<string>(),
                currentInterest: 10,
                playerDeliveredMessage: "Hi",
                interestBefore: 10,
                interestAfter: 10,
                responseDelayMinutes: 1.0,
                activeTrapInstructions: null
            );
            Assert.Null(opponent.ActiveTrapInstructions);
        }

        // What: AC3 — Empty instructions array is preserved (not converted to null)
        // Mutation: would catch if empty array was collapsed to null
        [Fact]
        public void AC3_EmptyInstructionsArray_IsPreserved()
        {
            var ctx = new DialogueContext(
                playerPrompt: "p",
                opponentPrompt: "o",
                conversationHistory: new List<(string, string)>(),
                opponentLastMessage: "",
                activeTraps: new List<string>(),
                currentInterest: 10,
                activeTrapInstructions: new string[0]
            );

            Assert.NotNull(ctx.ActiveTrapInstructions);
            Assert.Empty(ctx.ActiveTrapInstructions!);
        }

        #endregion

        #region AC4: GameSession populates instructions from active traps

        // What: AC4 — GameSession passes ActiveTrapInstructions to DialogueContext when trap active
        // Mutation: would catch if GameSession didn't populate instructions in StartTurnAsync
        [Fact]
        public async Task AC4_StartTurnAsync_WithActiveTrap_PopulatesDialogueContextInstructions()
        {
            var trapDef = new TrapDefinition(
                "charm_trap_cringe", StatType.Charm, TrapEffect.Disadvantage,
                0, 3, "Be painfully over-eager and cringy.",
                "Succeed on a Charm roll", "Send unsolicited selfie.");

            var trapRegistry = new SpecTestTrapRegistry();
            trapRegistry.Register(trapDef);

            // DC = 13 + 2 = 15. Roll 7: 7+2=9. Miss by 6 → TropeTrap tier → activates charm trap
            var dice = new FixedDice(
                7, 50,   // Turn 1: d20=7 (TropeTrap), d100 for timing
                15, 50   // Turn 2 start: d20=15 (used for options if needed), d100
            );

            var llm = new SpecCapturingLlmAdapter();
            var player = MakeProfile("Player");
            var opponent = MakeProfile("Opponent");
            var session = new GameSession(player, opponent, llm, dice, trapRegistry);

            // Turn 1: activate trap via TropeTrap tier
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0); // Charm → TropeTrap → activates charm trap

            // Turn 2: verify instructions passed to DialogueContext
            await session.StartTurnAsync();

            Assert.True(llm.DialogueContexts.Count >= 2, "Expected at least 2 DialogueContext captures");
            var turn2Ctx = llm.DialogueContexts[llm.DialogueContexts.Count - 1];
            Assert.NotNull(turn2Ctx.ActiveTrapInstructions);
            Assert.Contains("Be painfully over-eager and cringy.", turn2Ctx.ActiveTrapInstructions!);
        }

        // What: AC4 — GameSession passes empty/null instructions when no traps active
        // Mutation: would catch if stale instructions were left from previous state
        [Fact]
        public async Task AC4_StartTurnAsync_NoActiveTraps_InstructionsEmptyOrNull()
        {
            var dice = new FixedDice(15, 50); // High roll → success, no trap
            var llm = new SpecCapturingLlmAdapter();
            var session = new GameSession(
                MakeProfile("Player"), MakeProfile("Opponent"),
                llm, dice, new NullTrapRegistry());

            await session.StartTurnAsync();

            var ctx = llm.DialogueContexts[0];
            Assert.True(
                ctx.ActiveTrapInstructions == null || ctx.ActiveTrapInstructions.Length == 0,
                "Expected null or empty ActiveTrapInstructions when no traps active");
        }

        // What: AC4 — GameSession passes instructions to DeliveryContext
        // Mutation: would catch if ResolveTurnAsync skipped delivery context instructions
        [Fact]
        public async Task AC4_ResolveTurnAsync_WithActiveTrap_PopulatesDeliveryContextInstructions()
        {
            var trapDef = new TrapDefinition(
                "charm_trap_cringe", StatType.Charm, TrapEffect.Disadvantage,
                0, 5, "Be painfully over-eager and cringy.",
                "Succeed on a Charm roll", "Send unsolicited selfie.");

            var trapRegistry = new SpecTestTrapRegistry();
            trapRegistry.Register(trapDef);

            // Turn 1: TropeTrap to activate trap, Turn 2: check delivery context
            var dice = new FixedDice(
                7, 50,   // Turn 1: TropeTrap
                15, 50,  // Turn 2 start
                15, 50   // Turn 2 resolve
            );

            var llm = new SpecCapturingLlmAdapter();
            var session = new GameSession(
                MakeProfile("Player"), MakeProfile("Opponent"),
                llm, dice, trapRegistry);

            // Turn 1: activate trap
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            // Turn 2: resolve and check delivery context
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            // Trap has duration 5, after 1 AdvanceTurn → 4 left → still active in turn 2
            Assert.True(llm.DeliveryContexts.Count >= 2);
            var turn2Delivery = llm.DeliveryContexts[llm.DeliveryContexts.Count - 1];
            Assert.NotNull(turn2Delivery.ActiveTrapInstructions);
            Assert.Contains("Be painfully over-eager and cringy.", turn2Delivery.ActiveTrapInstructions!);
        }

        // What: AC4 — GameSession passes instructions to OpponentContext
        // Mutation: would catch if OpponentContext was not wired with trap instructions
        [Fact]
        public async Task AC4_ResolveTurnAsync_WithActiveTrap_PopulatesOpponentContextInstructions()
        {
            var trapDef = new TrapDefinition(
                "charm_trap_cringe", StatType.Charm, TrapEffect.Disadvantage,
                0, 5, "Be painfully over-eager and cringy.",
                "Succeed on a Charm roll", "Send unsolicited selfie.");

            var trapRegistry = new SpecTestTrapRegistry();
            trapRegistry.Register(trapDef);

            var dice = new FixedDice(
                7, 50,   // Turn 1: TropeTrap
                15, 50,  // Turn 2 start
                15, 50   // Turn 2 resolve
            );

            var llm = new SpecCapturingLlmAdapter();
            var session = new GameSession(
                MakeProfile("Player"), MakeProfile("Opponent"),
                llm, dice, trapRegistry);

            // Activate trap turn 1
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            // Turn 2: check opponent context
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            Assert.True(llm.OpponentContexts.Count >= 2);
            var turn2Opponent = llm.OpponentContexts[llm.OpponentContexts.Count - 1];
            Assert.NotNull(turn2Opponent.ActiveTrapInstructions);
            Assert.Contains("Be painfully over-eager and cringy.", turn2Opponent.ActiveTrapInstructions!);
        }

        // What: AC4 — Instructions for no-trap resolves are empty/null
        // Mutation: would catch if default instructions contained garbage data
        [Fact]
        public async Task AC4_ResolveTurnAsync_NoTraps_InstructionsEmptyOrNull()
        {
            var dice = new FixedDice(15, 50); // success, no trap
            var llm = new SpecCapturingLlmAdapter();
            var session = new GameSession(
                MakeProfile("Player"), MakeProfile("Opponent"),
                llm, dice, new NullTrapRegistry());

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            var delivery = llm.DeliveryContexts[0];
            Assert.True(
                delivery.ActiveTrapInstructions == null || delivery.ActiveTrapInstructions.Length == 0,
                "Expected null or empty instructions when no traps active");

            var opponent = llm.OpponentContexts[0];
            Assert.True(
                opponent.ActiveTrapInstructions == null || opponent.ActiveTrapInstructions.Length == 0,
                "Expected null or empty instructions when no traps active");
        }

        #endregion

        #region Edge Cases

        // What: Edge — Empty JSON array produces repository with zero traps
        // Mutation: would catch if empty array threw instead of succeeding
        [Fact]
        public void Edge_EmptyJsonArray_ProducesEmptyRepository()
        {
            var repo = new JsonTrapRepository("[]");

            Assert.Null(repo.GetTrap(StatType.Charm));
            Assert.Null(repo.GetLlmInstruction(StatType.Charm));
            Assert.Empty(repo.GetAll());
        }

        // What: Edge — Unknown stat in JSON throws FormatException
        // Mutation: would catch if unknown stats were silently ignored
        [Fact]
        public void Edge_UnknownStat_ThrowsFormatException()
        {
            var json = @"[{ ""id"": ""t"", ""stat"": ""strength"", ""effect"": ""disadvantage"", ""effect_value"": 0, ""duration_turns"": 1, ""llm_instruction"": ""i"", ""clear_method"": ""c"", ""nat1_bonus"": ""b"" }]";
            var ex = Assert.Throws<FormatException>(() => new JsonTrapRepository(json));
            Assert.Contains("strength", ex.Message);
        }

        // What: Edge — Unknown effect in JSON throws FormatException
        // Mutation: would catch if unknown effects were silently defaulted
        [Fact]
        public void Edge_UnknownEffect_ThrowsFormatException()
        {
            var json = @"[{ ""id"": ""t"", ""stat"": ""charm"", ""effect"": ""stun"", ""effect_value"": 0, ""duration_turns"": 1, ""llm_instruction"": ""i"", ""clear_method"": ""c"", ""nat1_bonus"": ""b"" }]";
            var ex = Assert.Throws<FormatException>(() => new JsonTrapRepository(json));
            Assert.Contains("stun", ex.Message);
        }

        // What: Edge — Non-array JSON throws FormatException
        // Mutation: would catch if object JSON was accepted as valid
        [Fact]
        public void Edge_NonArrayJson_ThrowsFormatException()
        {
            Assert.Throws<FormatException>(() => new JsonTrapRepository(@"{}"));
        }

        // What: Edge — Null JSON throws ArgumentNullException
        // Mutation: would catch if null was treated as empty
        [Fact]
        public void Edge_NullJson_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new JsonTrapRepository(null!));
        }

        // What: Edge — Missing required field (llm_instruction) throws FormatException
        // Mutation: would catch if missing fields were silently defaulted to empty
        [Fact]
        public void Edge_MissingLlmInstruction_ThrowsFormatException()
        {
            var json = @"[{ ""id"": ""t"", ""stat"": ""charm"", ""effect"": ""disadvantage"", ""effect_value"": 0, ""duration_turns"": 1, ""clear_method"": ""c"", ""nat1_bonus"": ""b"" }]";
            Assert.Throws<FormatException>(() => new JsonTrapRepository(json));
        }

        // What: Edge — Two traps same stat in single JSON, last wins
        // Mutation: would catch if first-write-wins or duplicate error thrown
        [Fact]
        public void Edge_DuplicateStat_LastWriteWins()
        {
            var json = @"[
  { ""id"": ""first"", ""stat"": ""charm"", ""effect"": ""disadvantage"", ""effect_value"": 0, ""duration_turns"": 1, ""llm_instruction"": ""First instruction"", ""clear_method"": ""c"", ""nat1_bonus"": ""b"" },
  { ""id"": ""second"", ""stat"": ""charm"", ""effect"": ""stat_penalty"", ""effect_value"": 2, ""duration_turns"": 3, ""llm_instruction"": ""Second instruction"", ""clear_method"": ""c"", ""nat1_bonus"": ""b"" }
]";
            var repo = new JsonTrapRepository(json);
            var trap = repo.GetTrap(StatType.Charm);

            Assert.NotNull(trap);
            Assert.Equal("second", trap!.Id);
            Assert.Equal("Second instruction", trap.LlmInstruction);
        }

        // What: Edge — GetTrap returns null for stats without traps loaded
        // Mutation: would catch if method returned a default trap object
        [Fact]
        public void Edge_GetTrap_ReturnsNull_ForUnloadedStat()
        {
            var repo = new JsonTrapRepository(SingleCharmTrapJson);
            Assert.Null(repo.GetTrap(StatType.Honesty));
            Assert.Null(repo.GetTrap(StatType.Chaos));
            Assert.Null(repo.GetTrap(StatType.Rizz));
        }

        // What: Edge — Missing id field throws FormatException
        // Mutation: would catch if missing id was silently accepted
        [Fact]
        public void Edge_MissingId_ThrowsFormatException()
        {
            var json = @"[{ ""stat"": ""charm"", ""effect"": ""disadvantage"", ""effect_value"": 0, ""duration_turns"": 1, ""llm_instruction"": ""i"", ""clear_method"": ""c"", ""nat1_bonus"": ""b"" }]";
            Assert.Throws<FormatException>(() => new JsonTrapRepository(json));
        }

        #endregion

        #region Test Helpers

        private static CharacterProfile MakeProfile(string name, int allStats = 2)
        {
            var stats = TestHelpers.MakeStatBlock(allStats);
            return new CharacterProfile(
                stats: stats,
                assembledSystemPrompt: $"You are {name}.",
                displayName: name,
                timing: new TimingProfile(5, 0.0f, 0.0f, "neutral"),
                level: 1);
        }

        /// <summary>
        /// LLM adapter that captures all contexts passed to it for assertion.
        /// Independent from TrapTaintInjectionTests helpers to maintain isolation.
        /// </summary>
        private sealed class SpecCapturingLlmAdapter : ILlmAdapter
        {
            public List<DialogueContext> DialogueContexts { get; } = new List<DialogueContext>();
            public List<DeliveryContext> DeliveryContexts { get; } = new List<DeliveryContext>();
            public List<OpponentContext> OpponentContexts { get; } = new List<OpponentContext>();

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
            {
                DialogueContexts.Add(context);
                return Task.FromResult(new[]
                {
                    new DialogueOption(StatType.Charm, "Charm line"),
                    new DialogueOption(StatType.Honesty, "Honesty line"),
                    new DialogueOption(StatType.Wit, "Wit line"),
                    new DialogueOption(StatType.Chaos, "Chaos line")
                });
            }

            public Task<string> DeliverMessageAsync(DeliveryContext context)
            {
                DeliveryContexts.Add(context);
                return Task.FromResult(context.ChosenOption.IntendedText);
            }

            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
            {
                OpponentContexts.Add(context);
                return Task.FromResult(new OpponentResponse("Reply", null, null));
            }

            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
            {
                return Task.FromResult<string?>(null);
            }
        }

        /// <summary>
        /// Trap registry for test setup — allows registering specific trap definitions.
        /// </summary>
        private sealed class SpecTestTrapRegistry : ITrapRegistry
        {
            private readonly Dictionary<StatType, TrapDefinition> _traps =
                new Dictionary<StatType, TrapDefinition>();

            public void Register(TrapDefinition trap) => _traps[trap.Stat] = trap;

            public TrapDefinition? GetTrap(StatType stat)
                => _traps.TryGetValue(stat, out var t) ? t : null;

            public string? GetLlmInstruction(StatType stat)
                => GetTrap(stat)?.LlmInstruction;
        }

        #endregion
    }
}
