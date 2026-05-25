using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Pinder.SessionRunner;
using Xunit;

namespace Pinder.Core.Tests
{
    [Trait("Category", "Core")]
    public class PlayerAgentContextSpecTests
    {
        private static StatBlock MakeStats(int charm = 3, int rizz = 2, int honesty = 2,
            int chaos = 2, int wit = 2, int sa = 2)
        {
            return new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, charm }, { StatType.Rizz, rizz }, { StatType.Honesty, honesty },
                    { StatType.Chaos, chaos }, { StatType.Wit, wit }, { StatType.SelfAwareness, sa }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 }, { ShadowStatType.Despair, 0 },
                    { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 0 }
                });
        }

        // -- AC3: All properties set correctly --

        [Fact]
        public void Constructor_SetsAllProperties()
        {
            var player = MakeStats(charm: 4);
            var opponent = MakeStats(charm: 2);
            var shadows = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Despair, 6 },
                { ShadowStatType.Dread, 3 }
            };

            var ctx = new PlayerAgentContext(
                player, opponent, 15, InterestState.VeryIntoIt, 3,
                new[] { "IckTrap", "Cringe" }, 6, shadows, 8);

            Assert.Same(player, ctx.PlayerStats);
            Assert.Same(opponent, ctx.OpponentStats);
            Assert.Equal(15, ctx.CurrentInterest);
            Assert.Equal(InterestState.VeryIntoIt, ctx.InterestState);
            Assert.Equal(3, ctx.MomentumStreak);
            Assert.Equal(2, ctx.ActiveTrapNames.Length);
            Assert.Equal(6, ctx.SessionHorniness);
            Assert.NotNull(ctx.ShadowValues);
            Assert.Equal(6, ctx.ShadowValues![ShadowStatType.Despair]);
            Assert.Equal(8, ctx.TurnNumber);
        }

        // -- AC3: Null validation --

        [Fact]
        public void Constructor_NullPlayerStats_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new PlayerAgentContext(null!, MakeStats(), 10, InterestState.Interested,
                    0, Array.Empty<string>(), 0, null, 1));
        }

        [Fact]
        public void Constructor_NullOpponentStats_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new PlayerAgentContext(MakeStats(), null!, 10, InterestState.Interested,
                    0, Array.Empty<string>(), 0, null, 1));
        }

        [Fact]
        public void Constructor_NullActiveTrapNames_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new PlayerAgentContext(MakeStats(), MakeStats(), 10, InterestState.Interested,
                    0, null!, 0, null, 1));
        }

        // -- AC3: ShadowValues nullable --

        [Fact]
        public void ShadowValues_Null_IsAccepted()
        {
            var ctx = new PlayerAgentContext(
                MakeStats(), MakeStats(), 10, InterestState.Interested,
                0, Array.Empty<string>(), 0, null, 1);
            Assert.Null(ctx.ShadowValues);
        }

        // -- Edge case: extreme interest values --

        [Fact]
        public void CurrentInterest_Zero_Accepted()
        {
            var ctx = new PlayerAgentContext(
                MakeStats(), MakeStats(), 0, InterestState.Unmatched,
                0, Array.Empty<string>(), 0, null, 1);
            Assert.Equal(0, ctx.CurrentInterest);
        }

        [Fact]
        public void CurrentInterest_TwentyFive_Accepted()
        {
            var ctx = new PlayerAgentContext(
                MakeStats(), MakeStats(), 25, InterestState.DateSecured,
                0, Array.Empty<string>(), 0, null, 1);
            Assert.Equal(25, ctx.CurrentInterest);
        }

        // -- Edge case: momentum streak values --

        [Fact]
        public void MomentumStreak_Zero_Accepted()
        {
            var ctx = new PlayerAgentContext(
                MakeStats(), MakeStats(), 10, InterestState.Interested,
                0, Array.Empty<string>(), 0, null, 1);
            Assert.Equal(0, ctx.MomentumStreak);
        }

        [Fact]
        public void MomentumStreak_HighValue_Accepted()
        {
            var ctx = new PlayerAgentContext(
                MakeStats(), MakeStats(), 10, InterestState.Interested,
                10, Array.Empty<string>(), 0, null, 1);
            Assert.Equal(10, ctx.MomentumStreak);
        }

        // -- Edge case: empty trap names --

        [Fact]
        public void ActiveTrapNames_EmptyArray_Accepted()
        {
            var ctx = new PlayerAgentContext(
                MakeStats(), MakeStats(), 10, InterestState.Interested,
                0, Array.Empty<string>(), 0, null, 1);
            Assert.Empty(ctx.ActiveTrapNames);
        }
    }
}
