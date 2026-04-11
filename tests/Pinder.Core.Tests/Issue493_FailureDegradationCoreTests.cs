using System;
using System.Collections.Generic;
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
    /// Tests for Issue #493: Mechanic — failure degradation should be legible to the opponent.
    /// Covers OpponentContext DTO extension and GameSession wiring of FailureTier.
    /// </summary>
    [Trait("Category", "Core")]
    public class Issue493_FailureDegradationCoreTests
    {
        #region Helpers

        private static StatBlock MakeStatBlock(int allStats = 2)
        {
            return TestHelpers.MakeStatBlock(allStats);
        }

        private static CharacterProfile MakeProfile(string name, int allStats = 2)
        {
            return new CharacterProfile(
                stats: MakeStatBlock(allStats),
                assembledSystemPrompt: $"You are {name}.",
                displayName: name,
                timing: new TimingProfile(5, 0.0f, 0.0f, "neutral"),
                level: 1);
        }

        /// <summary>
        /// LLM adapter that captures the OpponentContext passed to GetOpponentResponseAsync.
        /// </summary>
        private sealed class CapturingLlmAdapter : ILlmAdapter
        {
            public OpponentContext? CapturedOpponentContext { get; private set; }

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
            {
                return Task.FromResult(new[]
                {
                    new DialogueOption(StatType.Charm, "Hello there")
                });
            }

            public Task<string> DeliverMessageAsync(DeliveryContext context)
                => Task.FromResult(context.ChosenOption.IntendedText);

            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
            {
                CapturedOpponentContext = context;
                return Task.FromResult(new OpponentResponse("response"));
            }

            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
                => Task.FromResult<string?>(null);
            public System.Threading.Tasks.Task<string> ApplyHorninessOverlayAsync(string message, string instruction) => System.Threading.Tasks.Task.FromResult(message);
        }

                #endregion

        #region AC1: OpponentContext includes DeliveryTier

        // Mutation: would catch if DeliveryTier property is missing or not assigned from constructor param
        [Fact]
        public void AC1_OpponentContext_DeliveryTier_SetFromConstructor()
        {
            var context = new OpponentContext(
                playerPrompt: "p",
                opponentPrompt: "o",
                conversationHistory: new List<(string, string)> { ("P", "hi") },
                opponentLastMessage: "hi",
                activeTraps: Array.Empty<string>(),
                currentInterest: 12,
                playerDeliveredMessage: "hello",
                interestBefore: 12,
                interestAfter: 10,
                responseDelayMinutes: 1.0,
                deliveryTier: FailureTier.TropeTrap);

            Assert.Equal(FailureTier.TropeTrap, context.DeliveryTier);
        }

        // Mutation: would catch if default value is something other than FailureTier.None
        [Fact]
        public void AC1_OpponentContext_DeliveryTier_DefaultsToNone()
        {
            var context = new OpponentContext(
                playerPrompt: "p",
                opponentPrompt: "o",
                conversationHistory: new List<(string, string)> { ("P", "hi") },
                opponentLastMessage: "hi",
                activeTraps: Array.Empty<string>(),
                currentInterest: 10,
                playerDeliveredMessage: "hello",
                interestBefore: 10,
                interestAfter: 10,
                responseDelayMinutes: 1.0);

            Assert.Equal(FailureTier.None, context.DeliveryTier);
        }

        // Mutation: would catch if DeliveryTier stores wrong enum value (e.g. always Fumble)
        [Theory]
        [InlineData(FailureTier.None)]
        [InlineData(FailureTier.Fumble)]
        [InlineData(FailureTier.Misfire)]
        [InlineData(FailureTier.TropeTrap)]
        [InlineData(FailureTier.Catastrophe)]
        [InlineData(FailureTier.Legendary)]
        public void AC1_OpponentContext_PreservesAllTierValues(FailureTier tier)
        {
            var context = new OpponentContext(
                playerPrompt: "p",
                opponentPrompt: "o",
                conversationHistory: new List<(string, string)> { ("P", "hi") },
                opponentLastMessage: "hi",
                activeTraps: Array.Empty<string>(),
                currentInterest: 10,
                playerDeliveredMessage: "hello",
                interestBefore: 10,
                interestAfter: 10,
                responseDelayMinutes: 1.0,
                deliveryTier: tier);

            Assert.Equal(tier, context.DeliveryTier);
        }

        #endregion

        #region AC2: GameSession passes roll tier to OpponentContext

        // Mutation: would catch if GameSession passes FailureTier.None instead of rollResult.Tier on failure
        [Fact]
        public async Task AC2_GameSession_PassesFailureTier_OnFailedRoll()
        {
            // With stat mod +2, level bonus +0, DC = 13 + 2 = 15
            // Roll of 3: 3 + 2 + 0 = 5 < 15 → fail, miss by 10 → Catastrophe
            var dice = new FixedDice(
                5,   // Constructor: horniness roll (1d10)
                3,   // d20 roll → total 5 vs DC 15 → miss by 10 → Catastrophe
                50   // d100 timing
            );

            var llm = new CapturingLlmAdapter();
            var session = new GameSession(
                MakeProfile("P"), MakeProfile("O"),
                llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            Assert.NotNull(llm.CapturedOpponentContext);
            Assert.NotEqual(FailureTier.None, llm.CapturedOpponentContext!.DeliveryTier);
        }

        // Mutation: would catch if GameSession passes a failure tier when the roll is actually a success
        [Fact]
        public async Task AC2_GameSession_PassesNone_OnSuccessfulRoll()
        {
            // Roll of 18: 18 + 2 + 0 = 20 >= 15 → success
            var dice = new FixedDice(
                5,   // Constructor: horniness roll
                18,  // d20 roll → success
                50   // d100 timing
            );

            var llm = new CapturingLlmAdapter();
            var session = new GameSession(
                MakeProfile("P"), MakeProfile("O"),
                llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            Assert.NotNull(llm.CapturedOpponentContext);
            Assert.Equal(FailureTier.None, llm.CapturedOpponentContext!.DeliveryTier);
        }

        // Mutation: would catch if GameSession hardcodes a specific FailureTier instead of reading from rollResult.Tier
        [Fact]
        public async Task AC2_GameSession_PassesFumble_OnSmallMiss()
        {
            // With stat mod +2, DC = 18
            // Roll of 15: 15 + 2 = 17 < 18 → miss by 1 → Fumble
            var dice = new FixedDice(
                5,   // Constructor: horniness roll
                15,  // d20 roll → total 17 vs DC 18 → miss by 1 → Fumble
                50   // d100 timing
            );

            var llm = new CapturingLlmAdapter();
            var session = new GameSession(
                MakeProfile("P"), MakeProfile("O"),
                llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            Assert.NotNull(llm.CapturedOpponentContext);
            Assert.Equal(FailureTier.Fumble, llm.CapturedOpponentContext!.DeliveryTier);
        }

        #endregion

        #region Edge Cases

        // Mutation: would catch if backward compatibility is broken by requiring deliveryTier param
        [Fact]
        public void EdgeCase_BackwardCompatibility_OldConstructorStillWorks()
        {
            // This test verifies the constructor compiles and works without deliveryTier
            var context = new OpponentContext(
                playerPrompt: "p",
                opponentPrompt: "o",
                conversationHistory: new List<(string, string)>(),
                opponentLastMessage: "",
                activeTraps: Array.Empty<string>(),
                currentInterest: 10,
                playerDeliveredMessage: "",
                interestBefore: 10,
                interestAfter: 10,
                responseDelayMinutes: 0);

            // Should default to None (success)
            Assert.Equal(FailureTier.None, context.DeliveryTier);
        }

        // Mutation: would catch if DeliveryTier interacts with other optional params incorrectly
        [Fact]
        public void EdgeCase_DeliveryTier_WithAllOptionalParams()
        {
            var shadows = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Fixation, 8 }
            };

            var context = new OpponentContext(
                playerPrompt: "p",
                opponentPrompt: "o",
                conversationHistory: new List<(string, string)> { ("P", "hi") },
                opponentLastMessage: "hi",
                activeTraps: Array.Empty<string>(),
                currentInterest: 10,
                playerDeliveredMessage: "hello",
                interestBefore: 10,
                interestAfter: 8,
                responseDelayMinutes: 1.0,
                shadowThresholds: shadows,
                activeTrapInstructions: new[] { "trap1" },
                playerName: "Gerald",
                opponentName: "Velvet",
                currentTurn: 5,
                deliveryTier: FailureTier.Legendary);

            Assert.Equal(FailureTier.Legendary, context.DeliveryTier);
            Assert.Equal("Gerald", context.PlayerName);
            Assert.Equal("Velvet", context.OpponentName);
        }

        #endregion
    }
}
