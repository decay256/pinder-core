using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// LLM adapter that returns options with specific stats, allowing combo testing.
    /// </summary>
    [Trait("Category", "Core")]
    public sealed class ComboTestLlmAdapter : ILlmAdapter
    {
        private readonly Queue<DialogueOption[]> _optionSets = new Queue<DialogueOption[]>();

        public void EnqueueOptions(params DialogueOption[] options)
        {
            _optionSets.Enqueue(options);
        }

        public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
        {
            if (_optionSets.Count > 0)
                return Task.FromResult(_optionSets.Dequeue());

            // Default fallback
            return Task.FromResult(new[]
            {
                new DialogueOption(StatType.Charm, "Default option")
            });
        }

        public Task<string> DeliverMessageAsync(DeliveryContext context)
        {
            return Task.FromResult(context.ChosenOption.IntendedText);
        }

        public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
        {
            return Task.FromResult(new OpponentResponse("..."));
        }

        public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
        {
            return Task.FromResult<string?>(null);
        }
        public System.Threading.Tasks.Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? opponentContext = null, string? archetypeDirective = null) => System.Threading.Tasks.Task.FromResult(message);
            public System.Threading.Tasks.Task<string> ApplyShadowCorruptionAsync(string message, string instruction, Pinder.Core.Stats.ShadowStatType shadow, string? archetypeDirective = null) => System.Threading.Tasks.Task.FromResult(message);
            public System.Threading.Tasks.Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction, string trapName, string? opponentContext = null, string? archetypeDirective = null) => System.Threading.Tasks.Task.FromResult(message);
    }

    [Trait("Category", "Core")]
    public class ComboGameSessionTests
    {
        private static CharacterProfile MakeProfile(string name, int allStats = 2)
        {
            return new CharacterProfile(
                stats: TestHelpers.MakeStatBlock(allStats),
                assembledSystemPrompt: $"You are {name}.",
                displayName: name,
                timing: new TimingProfile(5, 0.0f, 0.0f, "neutral"),
                level: 1);
        }

        /// <summary>
        /// Tests that The Setup combo (Wit → Charm) populates ComboTriggered on TurnResult
        /// and adds +1 to interest delta.
        /// </summary>
        [Fact]
        public async Task TheSetup_WitThenCharm_ComboTriggeredAndBonusApplied()
        {
            // Opponent allStats=0 → DC = 16. Roll 15: 15 + 2 + 0 = 17 >= 16 → success (beat by 1 → SuccessScale +1)
            // need = 16-2=14 → Hard → RiskTierBonus +3. Turn 2 + combo +1 = total 5.
            // Each turn: d20 + d100(timing)
            var dice = new FixedDice(
                5,  // Constructor: horniness roll (1d10)
                15, 50,  // Turn 1: Wit
                15, 50   // Turn 2: Charm
            );

            var llm = new ComboTestLlmAdapter();
            llm.EnqueueOptions(new DialogueOption(StatType.Wit, "A witty remark"));
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "A charming line"));

            var session = new GameSession(MakeProfile("P"), MakeProfile("O", 0), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            // Turn 1: Wit
            await session.StartTurnAsync();
            var r1 = await session.ResolveTurnAsync(0);
            Assert.True(r1.Roll.IsSuccess);
            Assert.Null(r1.ComboTriggered);

            // Turn 2: Charm → The Setup fires
            var start2 = await session.StartTurnAsync();
            Assert.Equal("The Setup", start2.Options[0].ComboName);
            var r2 = await session.ResolveTurnAsync(0);
            Assert.True(r2.Roll.IsSuccess);
            Assert.Equal("The Setup", r2.ComboTriggered);

            // Interest delta: SuccessScale(+1) + RiskTierBonus(+3 for Hard) + momentum(0 streak=2) + combo(+1) = +5
            Assert.Equal(5, r2.InterestDelta);
        }

        /// <summary>
        /// Tests The Recovery combo: any fail → SA success = +2 interest.
        /// </summary>
        [Fact]
        public async Task TheRecovery_FailThenSASuccess_ComboTriggered()
        {
            // Opponent allStats=0 → DC=16.
            // Turn 1: Roll 5 → fail (5 + 2 = 7 vs DC 16, miss by 9 = TropeTrap -2)
            // Turn 2: Roll 15 → success (SA, 15+2=17 vs DC 16, need=14 → Hard +3)
            var dice = new FixedDice(
                5,  // Constructor: horniness roll (1d10)
                5, 50,   // Turn 1: fail
                15, 50   // Turn 2: SA success
            );

            var llm = new ComboTestLlmAdapter();
            llm.EnqueueOptions(new DialogueOption(StatType.Chaos, "Chaos line"));
            llm.EnqueueOptions(new DialogueOption(StatType.SelfAwareness, "SA line"));

            var session = new GameSession(MakeProfile("P"), MakeProfile("O", 0), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            // Turn 1: Chaos fail
            await session.StartTurnAsync();
            var r1 = await session.ResolveTurnAsync(0);
            Assert.False(r1.Roll.IsSuccess);

            // Turn 2: SA success → Recovery
            var start2 = await session.StartTurnAsync();
            Assert.Equal("The Recovery", start2.Options[0].ComboName);
            var r2 = await session.ResolveTurnAsync(0);
            Assert.True(r2.Roll.IsSuccess);
            Assert.Equal("The Recovery", r2.ComboTriggered);

            // Interest delta: SuccessScale(+1) + RiskTierBonus(+3 for Hard) + combo(+2) = +6
            Assert.Equal(6, r2.InterestDelta);
        }

        /// <summary>
        /// Tests The Triple combo: 3 distinct stats → +1 roll bonus next turn via externalBonus.
        /// </summary>
        [Fact]
        public async Task TheTriple_ThreeDistinctStats_RollBonusNextTurn()
        {
            // Use stats that don't form 2-stat combos: Rizz, SelfAwareness, Chaos
            // Opponent allStats=0 → DC=16. Roll 15: 15+2=17 >= 16 → success
            var dice = new FixedDice(
                5,  // Constructor: horniness roll (1d10)
                15, 50,      // Turn 1: Rizz (d20 + d100 timing)
                15, 50,      // Turn 2: SA
                15, 15, 50,  // Turn 3: Chaos → Triple triggers (VeryIntoIt advantage after turn 2)
                15, 15, 50,  // Turn 4: advantage, +1 triple + 2 momentum = 3 external
                15, 50       // Extra safety margin
            );

            var llm = new ComboTestLlmAdapter();
            llm.EnqueueOptions(new DialogueOption(StatType.Rizz, "Rizz line"));
            llm.EnqueueOptions(new DialogueOption(StatType.SelfAwareness, "SA line"));
            llm.EnqueueOptions(new DialogueOption(StatType.Chaos, "Chaos line"));
            // Turn 4: use Chaos again so we don't re-trigger Triple
            llm.EnqueueOptions(new DialogueOption(StatType.Chaos, "Chaos again"));

            var session = new GameSession(MakeProfile("P"), MakeProfile("O", 0), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            // Turns 1-2
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            // Turn 3: Chaos → Triple
            await session.StartTurnAsync();
            var r3 = await session.ResolveTurnAsync(0);
            Assert.Equal("The Triple", r3.ComboTriggered);
            Assert.Equal(0, r3.TripleBonusApplied); // #693: bonus not yet consumed on triggering turn
            Assert.True(r3.StateAfter.TripleBonusActive);

            // Turn 4: should have +1 external bonus from triple + +2 from momentum (streak=3 at start, #268)
            var start4 = await session.StartTurnAsync();
            Assert.True(start4.State.TripleBonusActive);
            var r4 = await session.ResolveTurnAsync(0);
            // Roll: 15+2+0=17 base, +1 triple + 2 momentum = 3 external
            Assert.Equal(3, r4.Roll.ExternalBonus);
            Assert.Equal(1, r4.TripleBonusApplied); // #693: Triple bonus surfaced in TurnResult
            Assert.False(r4.StateAfter.TripleBonusActive); // consumed
        }

        /// <summary>
        /// Tests that combo does NOT trigger when the completing roll fails.
        /// </summary>
        [Fact]
        public async Task ComboDoesNotTriggerOnFailure()
        {
            // Turn 1: Wit success. Turn 2: Charm fail → no Setup
            var dice = new FixedDice(
                5,  // Constructor: horniness roll (1d10)
                15, 50,  // Turn 1: success
                5, 50    // Turn 2: fail (5+2=7 < 15)
            );

            var llm = new ComboTestLlmAdapter();
            llm.EnqueueOptions(new DialogueOption(StatType.Wit, "Wit"));
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Charm"));

            var session = new GameSession(MakeProfile("P"), MakeProfile("O", 0), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            await session.StartTurnAsync();
            var r2 = await session.ResolveTurnAsync(0);
            Assert.False(r2.Roll.IsSuccess);
            Assert.Null(r2.ComboTriggered);
        }

        /// <summary>
        /// Tests that PeekCombo populates DialogueOption.ComboName during StartTurnAsync.
        /// </summary>
        [Fact]
        public async Task PeekCombo_PopulatesDialogueOptionComboName()
        {
            var dice = new FixedDice(
                5,  // Constructor: horniness roll (1d10)
                15, 50,  // Turn 1
                15, 50   // Turn 2 (not used for start, but needed for resolve)
            );

            var llm = new ComboTestLlmAdapter();
            llm.EnqueueOptions(new DialogueOption(StatType.Wit, "Wit"));
            // Turn 2: offer both Charm (Setup combo) and Honesty (Disarm combo)
            llm.EnqueueOptions(
                new DialogueOption(StatType.Charm, "Charm"),
                new DialogueOption(StatType.Honesty, "Honesty"),
                new DialogueOption(StatType.Rizz, "Rizz")
            );

            var session = new GameSession(MakeProfile("P"), MakeProfile("O", 0), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            // Turn 1: Wit
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            // Turn 2: Check combo names on options
            var start2 = await session.StartTurnAsync();
            Assert.Equal("The Setup", start2.Options[0].ComboName);  // Wit → Charm
            Assert.Equal("The Disarm", start2.Options[1].ComboName); // Wit → Honesty
            Assert.Null(start2.Options[2].ComboName);                 // Wit → Rizz = no combo
        }

        /// <summary>
        /// Tests that Triple bonus is consumed by Wait action.
        /// </summary>
        [Fact]
        public async Task TripleBonus_ConsumedByWait()
        {
            // Opponent allStats=0 → DC=16. Turn 2 reaches VeryIntoIt → turn 3 uses advantage (2 d20s).
            var dice = new FixedDice(
                5,  // Constructor: horniness roll (1d10)
                15, 50,      // Turn 1
                15, 50,      // Turn 2
                15, 15, 50,  // Turn 3 (VeryIntoIt advantage after turn 2)
                15, 15, 50   // Turn 5: should NOT have triple bonus (also VeryIntoIt+ advantage)
            );

            var llm = new ComboTestLlmAdapter();
            llm.EnqueueOptions(new DialogueOption(StatType.Rizz, "R"));
            llm.EnqueueOptions(new DialogueOption(StatType.SelfAwareness, "SA"));
            llm.EnqueueOptions(new DialogueOption(StatType.Chaos, "C"));
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Ch"));

            var session = new GameSession(MakeProfile("P"), MakeProfile("O", 0), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            // 3 turns to trigger Triple
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);
            await session.StartTurnAsync();
            var r3 = await session.ResolveTurnAsync(0);
            Assert.Equal("The Triple", r3.ComboTriggered);
            Assert.True(r3.StateAfter.TripleBonusActive);

            // Wait consumes the triple bonus (interest is ~22, no Bored ghost check)
            session.Wait();

            // Next turn should NOT have triple bonus, but still has momentum (streak=3 → +2 roll bonus, #268)
            var start5 = await session.StartTurnAsync();
            Assert.False(start5.State.TripleBonusActive);
            var r5 = await session.ResolveTurnAsync(0);
            Assert.Equal(2, r5.Roll.ExternalBonus); // momentum only, triple was consumed by Wait
        }
    }
}
