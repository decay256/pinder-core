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
    // ============================================================
    // GameSession Integration Tests for Combo System
    // ============================================================

    [Trait("Category", "Core")]
    public class ComboGameSessionSpecTests
    {
        private static CharacterProfile MakeProfile(string name, int allStats = 2)
        {
            return TestHelpers.MakeCharacterProfile(
                stats: TestHelpers.MakeStatBlock(allStats),
                assembledSystemPrompt: $"You are {name}.",
                displayName: name,
                timing: new TimingProfile(5, 0.0f, 0.0f, "neutral"),
                level: 1);
        }

        // Mutation: would catch if GameSession didn't apply combo interest bonus to total delta
        [Fact]
        public async Task AC2_Integration_ComboInterestBonusAddsToTotalDelta()
        {
            // Setup: Wit success → Charm success (The Setup, +1)
            // DC = 13 + 2 = 15. Roll 15: 15+2 = 17 >= 15 → success (beat by 2 → SuccessScale +1)
            var dice = new FixedDice(
                5,  // Constructor: horniness roll (1d10)
                15, 50, // Turn 1: Wit
                15, 50  // Turn 2: Charm
            );

            var llm = new ComboTestLlmAdapter();
            llm.EnqueueOptions(new DialogueOption(StatType.Wit, "Witty"));
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Charming"));

            var session = new GameSession(MakeProfile("P"), MakeProfile("O", 0), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            var r1 = await session.ResolveTurnAsync(0);
            Assert.True(r1.Roll.IsSuccess);

            await session.StartTurnAsync();
            var r2 = await session.ResolveTurnAsync(0);
            Assert.True(r2.Roll.IsSuccess);
            Assert.Equal("The Setup", r2.ComboTriggered);

            // Verify combo bonus (+1) is included in interest delta
            // SuccessScale(+1 for beat by 1) + RiskTierBonus(Hard:+3) + combo(+1) = 5
            Assert.Equal(5, r2.InterestDelta);
        }

        // Mutation: would catch if GameSession set ComboTriggered even on failed roll
        [Fact]
        public async Task AC2_Integration_NoComboOnFailedRoll()
        {
            var dice = new FixedDice(
                5,  // Constructor: horniness roll (1d10)
                15, 50, // Turn 1: Wit success
                5, 50   // Turn 2: Charm fail (5+2=7 < 15)
            );

            var llm = new ComboTestLlmAdapter();
            llm.EnqueueOptions(new DialogueOption(StatType.Wit, "Witty"));
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Charming"));

            var session = new GameSession(MakeProfile("P"), MakeProfile("O", 0), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            await session.StartTurnAsync();
            var r2 = await session.ResolveTurnAsync(0);
            Assert.False(r2.Roll.IsSuccess);
            Assert.Null(r2.ComboTriggered);
        }

        // Mutation: would catch if TurnResult.ComboTriggered wasn't populated
        [Fact]
        public async Task AC6_Integration_TurnResultComboTriggeredPopulated()
        {
            var dice = new FixedDice(
                5,  // Constructor: horniness roll (1d10)
                15, 50, // Turn 1
                15, 50  // Turn 2
            );

            var llm = new ComboTestLlmAdapter();
            llm.EnqueueOptions(new DialogueOption(StatType.Chaos, "Chaos"));
            llm.EnqueueOptions(new DialogueOption(StatType.Rizz, "Rizz"));

            var session = new GameSession(MakeProfile("P"), MakeProfile("O", 0), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            var r1 = await session.ResolveTurnAsync(0);
            Assert.Null(r1.ComboTriggered); // first turn, no combo

            await session.StartTurnAsync();
            var r2 = await session.ResolveTurnAsync(0);
            Assert.Equal("The Escalation", r2.ComboTriggered);
        }

        // Mutation: would catch if PeekCombo wasn't called during StartTurnAsync
        [Fact]
        public async Task AC5_Integration_StartTurnPopulatesComboNames()
        {
            var dice = new FixedDice(
                5,  // Constructor: horniness roll (1d10)
                15, 50, // Turn 1
                15, 50  // Turn 2
            );

            var llm = new ComboTestLlmAdapter();
            llm.EnqueueOptions(new DialogueOption(StatType.Honesty, "Honest"));
            // Turn 2: Chaos would complete The Pivot, SA would not
            llm.EnqueueOptions(
                new DialogueOption(StatType.Chaos, "Chaos"),
                new DialogueOption(StatType.SelfAwareness, "SA")
            );

            var session = new GameSession(MakeProfile("P"), MakeProfile("O", 0), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            var start2 = await session.StartTurnAsync();
            Assert.Equal("The Pivot", start2.Options[0].ComboName); // Honesty→Chaos
            Assert.Null(start2.Options[1].ComboName); // Honesty→SA is not a combo
        }

        // Mutation: would catch if GameSession didn't pass externalBonus for Triple
        [Fact]
        public async Task AC4_Integration_TripleBonusAppliedAsExternalBonus()
        {
            // 3 turns with distinct non-overlapping stats, then check external bonus on turn 4
            // Datee allStats=0 → DC=16. Turn 2 reaches VeryIntoIt (interest 10+4+4=18) → advantage from turn 3.
            var dice = new FixedDice(
                5,  // Constructor: horniness roll (1d10)
                15, 50,      // Turn 1: Rizz (d20, d100)
                15, 50,      // Turn 2: SA (d20, d100)
                15, 15, 50,  // Turn 3: Chaos → Triple (VeryIntoIt advantage: d20, d20, d100)
                15, 15, 50   // Turn 4: advantage (d20, d20, d100)
            );

            var llm = new ComboTestLlmAdapter();
            llm.EnqueueOptions(new DialogueOption(StatType.Rizz, "R"));
            llm.EnqueueOptions(new DialogueOption(StatType.SelfAwareness, "SA"));
            llm.EnqueueOptions(new DialogueOption(StatType.Chaos, "C"));
            // Use SA again to avoid triggering a second Triple (SA,Chaos,SA = not 3 distinct)
            llm.EnqueueOptions(new DialogueOption(StatType.SelfAwareness, "SA2"));

            var session = new GameSession(MakeProfile("P"), MakeProfile("O", 0), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            // Turns 1-2
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            // Turn 3: Triple
            await session.StartTurnAsync();
            var r3 = await session.ResolveTurnAsync(0);
            Assert.Equal("The Triple", r3.ComboTriggered);
            Assert.True(r3.StateAfter.TripleBonusActive);

            // Turn 4: verify external bonus applied (triple +2 + momentum +2 from streak=3 at start, #268)
            var start4 = await session.StartTurnAsync();
            Assert.True(start4.State.TripleBonusActive);
            var r4 = await session.ResolveTurnAsync(0);
            Assert.Equal(4, r4.Roll.ExternalBonus);
            Assert.False(r4.StateAfter.TripleBonusActive); // consumed
        }

        // Mutation: would catch if Recovery combo in GameSession didn't add +2 to interest delta
        [Fact]
        public async Task AC3_Integration_RecoveryAdds2ToInterestDelta()
        {
            // Turn 1: fail, Turn 2: SA success → Recovery (+2)
            var dice = new FixedDice(
                5,  // Constructor: horniness roll (1d10)
                5, 50,  // Turn 1: fail (5+2=7 < 15)
                15, 50  // Turn 2: success (15+2=17 >= 15)
            );

            var llm = new ComboTestLlmAdapter();
            llm.EnqueueOptions(new DialogueOption(StatType.Wit, "Wit"));
            llm.EnqueueOptions(new DialogueOption(StatType.SelfAwareness, "SA"));

            var session = new GameSession(MakeProfile("P"), MakeProfile("O", 0), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            var r1 = await session.ResolveTurnAsync(0);
            Assert.False(r1.Roll.IsSuccess);

            await session.StartTurnAsync();
            var r2 = await session.ResolveTurnAsync(0);
            Assert.True(r2.Roll.IsSuccess);
            Assert.Equal("The Recovery", r2.ComboTriggered);

            // Interest delta: SuccessScale(+1) + RiskTierBonus(Hard:+3) + combo(+2) = +6
            Assert.Equal(6, r2.InterestDelta);
        }
    }
}
