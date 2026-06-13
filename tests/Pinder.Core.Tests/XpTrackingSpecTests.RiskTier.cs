using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Progression;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests
{
    [Trait("Category", "Core")]
    public partial class XpTrackingSpecTests
    {
        // ====================== Risk-Tier XP Multiplier Tests (#314) ======================

        // What: Safe risk tier applies 1x multiplier to base XP
        // Mutation: Fails if Safe multiplier is not 1.0
        [Fact]
        public async Task RiskTierXp_Safe_1xMultiplier()
        {
            // Player stat 10 → need = 13 - 10 = 3 → Safe (≤5)
            // DC = 13, base XP = 5, multiplier 1x → 5 XP
            var session = MakeSession(diceRoll: 15, dateeStatValue: 0, playerStatValue: 10);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            Assert.Equal(RiskTier.Safe, result.Roll.RiskTier);
            Assert.Equal(5, result.XpEarned); // 5 * 1.0 = 5
        }

        // What: Medium risk tier applies 1.5x multiplier to base XP
        // Mutation: Fails if Medium multiplier is not 1.5
        [Fact]
        public async Task RiskTierXp_Medium_1_5xMultiplier()
        {
            // Player stat 6 → need = 16 - 6 = 10 → Medium (8–11)
            // DC = 16, base XP = 5, multiplier 1.5x → 8
            var session = MakeSession(diceRoll: 15, dateeStatValue: 0, playerStatValue: 6);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            Assert.Equal(RiskTier.Medium, result.Roll.RiskTier);
            Assert.Equal(8, result.XpEarned); // 5 * 1.5 = 7.5 → 8
        }

        // What: Hard risk tier applies 2x multiplier to base XP
        // Mutation: Fails if Hard multiplier is not 2.0
        [Fact]
        public async Task RiskTierXp_Hard_2xMultiplier()
        {
            // Player stat 3 → need = 14 - 3 = 11 → Hard (11–15)
            // DC = 14, base XP = 10, multiplier 2x → 20
            var session = MakeSession(diceRoll: 18, dateeStatValue: 1, playerStatValue: 3);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            Assert.Equal(RiskTier.Hard, result.Roll.RiskTier);
            Assert.Equal(20, result.XpEarned); // 10 * 2.0 = 20
        }

        // What: Bold risk tier applies 3x multiplier to base XP
        // Mutation: Fails if Bold multiplier is not 3.0
        [Fact]
        public async Task RiskTierXp_Bold_3xMultiplier()
        {
            // Player stat 3 → need = 22 - 3 = 19 → Bold (16–19)
            // DC = 22 (datee=6), base XP = 15 (DC>20), multiplier 3x → 45
            var session = MakeSession(diceRoll: 19, dateeStatValue: 6, playerStatValue: 3);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            Assert.Equal(RiskTier.Bold, result.Roll.RiskTier);
            Assert.Equal(45, result.XpEarned); // 15 * 3.0 = 45
        }

        // What: Nat20 is NOT affected by risk-tier multiplier (overrides DC-tier XP entirely)
        // Mutation: Fails if Nat20 XP is multiplied
        [Fact]
        public async Task RiskTierXp_Nat20_NoMultiplier()
        {
            var session = MakeSession(diceRoll: 20, dateeStatValue: 0, playerStatValue: 3);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsNatTwenty);
            Assert.Equal(25, result.XpEarned); // Flat 25, no multiplier
        }

        // What: Nat1 is NOT affected by risk-tier multiplier
        // Mutation: Fails if Nat1 XP is multiplied
        [Fact]
        public async Task RiskTierXp_Nat1_NoMultiplier()
        {
            var session = MakeSession(diceRoll: 1, dateeStatValue: 0, playerStatValue: 3);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsNatOne);
            Assert.Equal(10, result.XpEarned); // Flat 10, no multiplier
        }

        // What: Failure XP is NOT affected by risk-tier multiplier
        // Mutation: Fails if failure XP is multiplied
        [Fact]
        public async Task RiskTierXp_Failure_NoMultiplier()
        {
            var session = MakeSession(diceRoll: 5, dateeStatValue: 0, playerStatValue: 3);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.False(result.Roll.IsSuccess);
            Assert.False(result.Roll.IsNatOne);
            Assert.Equal(2, result.XpEarned); // Flat 2, no multiplier
        }

        // What: Medium multiplier rounds correctly (1.5 * 5 = 7.5 → 8)
        // Mutation: Fails if rounding is floor (7) instead of round (8)
        [Fact]
        public async Task RiskTierXp_Medium_RoundsCorrectly()
        {
            // base 5 * 1.5 = 7.5, Math.Round → 8. Player stat 6, datee 0 → DC=16, need=10 → Medium
            var session = MakeSession(diceRoll: 15, dateeStatValue: 0, playerStatValue: 6);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(RiskTier.Medium, result.Roll.RiskTier);
            Assert.Equal(8, result.XpEarned);
        }

        // What: Medium multiplier with base 10 (1.5 * 10 = 15, exact)
        // Mutation: Fails if multiplier calculation is wrong for mid DC
        [Fact]
        public async Task RiskTierXp_Medium_MidDc_ExactMultiply()
        {
            // Player stat 6 → need = 14 - 6 = 8 → Medium (6–10)
            // DC = 14, base XP = 10, multiplier 1.5x → 15
            var session = MakeSession(diceRoll: 15, dateeStatValue: 1, playerStatValue: 6);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            Assert.Equal(RiskTier.Medium, result.Roll.RiskTier);
            Assert.Equal(15, result.XpEarned); // 10 * 1.5 = 15
        }
    }
}
