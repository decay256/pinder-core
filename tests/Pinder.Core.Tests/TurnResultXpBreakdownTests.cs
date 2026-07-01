using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Progression;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Contract tests for ticket 1281: TurnResult.XpBreakdown property.
    /// </summary>
    [Trait("Category", "Core")]
    public class TurnResultXpBreakdownTests
    {
        private static RollResult MakeRoll() =>
            new RollResult(10, null, 10, StatType.Charm, 2, 0, 13, FailureTier.Success);

        private static GameStateSnapshot MakeSnapshot() =>
            new GameStateSnapshot(10, InterestState.Interested, 0, Array.Empty<string>(), 1);

        [Fact]
        public void PropertyType_XpBreakdown_IsIReadOnlyListOfXpEvent()
        {
            var prop = typeof(TurnResult).GetProperty("XpBreakdown");
            Assert.NotNull(prop);
            Assert.Equal(typeof(IReadOnlyList<XpLedger.XpEvent>), prop!.PropertyType);
        }

        [Fact]
        public void TurnResult_XpBreakdown_DefaultsToEmptyList()
        {
            var result = new TurnResult(
                MakeRoll(), "hello", "hi back", null, 1, MakeSnapshot(), false, null);

            Assert.NotNull(result.XpBreakdown);
            Assert.Empty(result.XpBreakdown);
        }

        [Fact]
        public void TurnResult_XpBreakdown_PopulatedFromConstructor()
        {
            var events = new List<XpLedger.XpEvent>
            {
                new XpLedger.XpEvent("Success_DC_Low", 5),
                new XpLedger.XpEvent("OutcomeBonus_DateSecured", 20)
            };

            var result = new TurnResult(
                MakeRoll(), "hello", "hi back", null, 1, MakeSnapshot(), false, null,
                xpBreakdown: events);

            Assert.NotNull(result.XpBreakdown);
            Assert.Equal(2, result.XpBreakdown.Count);
            Assert.Equal("Success_DC_Low", result.XpBreakdown[0].Source);
            Assert.Equal(5, result.XpBreakdown[0].Amount);
            Assert.Equal("OutcomeBonus_DateSecured", result.XpBreakdown[1].Source);
            Assert.Equal(20, result.XpBreakdown[1].Amount);
        }

        [Fact]
        public void TurnResult_XpBreakdown_NullBecomesEmptyList()
        {
            var result = new TurnResult(
                MakeRoll(), "hello", "hi back", null, 1, MakeSnapshot(), false, null,
                xpBreakdown: null);

            Assert.NotNull(result.XpBreakdown);
            Assert.Empty(result.XpBreakdown);
        }
    }

    public partial class XpTrackingSpecTests
    {
        [Fact]
        public async Task ResolveTurn_WithMultipleXpAwards_PopulatesXpBreakdownOnTurnResult()
        {
            // Start at 24 interest, success pushes to 25+ → DateSecured (ends game)
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), startingInterest: 24);
            var session = MakeSession(diceRoll: 15, dateeStatValue: 0, config: config);
            await session.StartTurnAsync();
            
            // ResolveTurnAsync will invoke RollResolutionStage underneath and return TurnResult.
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.IsGameOver);
            Assert.Equal(GameOutcome.DateSecured, result.Outcome);

            // We expect 2 XP events on the TurnResult.XpBreakdown:
            // 1. Roll success (e.g., Success_DC_Low with Hard risk tier multiplier = 5 * 2.0 = 10 XP)
            // 2. DateSecured outcome bonus = 20 XP
            Assert.NotNull(result.XpBreakdown);
            Assert.Equal(2, result.XpBreakdown.Count);

            var rollEvent = result.XpBreakdown[0];
            Assert.Equal("Success_DC_Low", rollEvent.Source);
            Assert.Equal(10, rollEvent.Amount);

            var outcomeEvent = result.XpBreakdown[1];
            Assert.Equal("OutcomeBonus_DateSecured", outcomeEvent.Source);
            Assert.Equal(20, outcomeEvent.Amount);
        }
    }
}
