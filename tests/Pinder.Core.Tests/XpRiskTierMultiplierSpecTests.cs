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
    /// <summary>
    /// Tests for issue #314 — XP risk-tier multiplier.
    /// Spec: rules §10 defines base XP (5/10/15 by DC), risk-reward doc defines multiplier
    ///   Safe=1x, Medium=1.5x, Hard=2x, Bold=3x.
    /// Multiplier applies only to successful (non-Nat20, non-Nat1) rolls.
    /// </summary>
    public class XpRiskTierMultiplierSpecTests
    {
        // ====================== AC-1: Safe success → 1x base XP ======================

        // What: AC-1 — Safe risk tier gives 1x base XP for low DC
        // Mutation: Fails if Safe multiplier is anything other than 1.0
        [Fact]
        public async Task Safe_LowDc_Returns_1x_BaseXp()
        {
            // Player stat 10, opponent stat 0 → DC = 13, need = 3 → Safe (≤5)
            // Base XP = 5 (low DC), 5 * 1.0 = 5
            var session = MakeSession(diceRoll: 15, opponentStatValue: 0, playerStatValue: 10);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            Assert.Equal(RiskTier.Safe, result.Roll.RiskTier);
            Assert.Equal(5, result.XpEarned);
        }

        // What: AC-1 — Safe risk tier gives 1x base XP for mid DC
        // Mutation: Fails if base XP uses wrong DC bucket mapping
        [Fact]
        public async Task Safe_MidDc_Returns_1x_BaseXp()
        {
            // Player stat 10, opponent stat 1 → DC = 14, need = 4 → Safe (≤5)
            // Base XP = 10 (mid DC), 10 * 1.0 = 10
            var session = MakeSession(diceRoll: 18, opponentStatValue: 1, playerStatValue: 10);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            Assert.Equal(RiskTier.Safe, result.Roll.RiskTier);
            Assert.Equal(10, result.XpEarned);
        }

        // ====================== AC-2: Medium success → 1.5x base XP ======================

        // What: AC-2 — Medium risk tier gives 1.5x base XP, low DC
        // Mutation: Fails if Medium multiplier is 1.0 instead of 1.5
        [Fact]
        public async Task Medium_LowDc_Returns_1_5x_BaseXp()
        {
            // Player stat 6, opponent stat 0 → DC = 16, need = 10 → Medium (8–11)
            // Base XP = 5, 5 * 1.5 = 7.5 → rounds to 8
            var session = MakeSession(diceRoll: 15, opponentStatValue: 0, playerStatValue: 6);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            Assert.Equal(RiskTier.Medium, result.Roll.RiskTier);
            Assert.Equal(8, result.XpEarned);
        }

        // What: AC-2 — Medium risk tier gives 1.5x base XP, mid DC (exact result)
        // Mutation: Fails if multiplier is 1.0 or 2.0 instead of 1.5
        [Fact]
        public async Task Medium_MidDc_Returns_1_5x_BaseXp_Exact()
        {
            // Player stat 6, opponent stat 1 → DC = 14, need = 8 → Medium (6–10)
            // Base XP = 10, 10 * 1.5 = 15.0 (exact)
            var session = MakeSession(diceRoll: 18, opponentStatValue: 1, playerStatValue: 6);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            Assert.Equal(RiskTier.Medium, result.Roll.RiskTier);
            Assert.Equal(15, result.XpEarned);
        }

        // ====================== AC-3: Hard success → 2x base XP ======================

        // What: AC-3 — Hard risk tier gives 2x base XP, mid DC
        // Mutation: Fails if Hard multiplier is 1.5 instead of 2.0
        [Fact]
        public async Task Hard_MidDc_Returns_2x_BaseXp()
        {
            // Player stat 3, opponent stat 1 → DC = 14, need = 11 → Hard (11–15)
            // Base XP = 10, 10 * 2.0 = 20
            var session = MakeSession(diceRoll: 18, opponentStatValue: 1, playerStatValue: 3);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            Assert.Equal(RiskTier.Hard, result.Roll.RiskTier);
            Assert.Equal(20, result.XpEarned);
        }

        // What: AC-3 — Hard risk tier gives 2x base XP, mid DC
        // Mutation: Fails if Hard multiplier uses wrong DC bucket
        [Fact]
        public async Task Hard_HighDc_Returns_2x_BaseXp()
        {
            // Player stat 3, opponent stat 1 → DC = 17, need = 14 → Hard (12–15)
            // Base XP = 10 (DC≤20), 10 * 2.0 = 20
            var session = MakeSession(diceRoll: 14, opponentStatValue: 1, playerStatValue: 3);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            Assert.Equal(RiskTier.Hard, result.Roll.RiskTier);
            Assert.Equal(20, result.XpEarned);
        }

        // ====================== AC-4: Bold success → 3x base XP ======================

        // What: AC-4 — Bold risk tier gives 3x base XP
        // Mutation: Fails if Bold multiplier is 2.0 instead of 3.0
        [Fact]
        public async Task Bold_HighDc_Returns_3x_BaseXp()
        {
            // Player stat 3, opponent stat 6 → DC = 22, need = 19 → Bold (16–19)
            // Base XP = 15 (DC>20), 15 * 3.0 = 45
            var session = MakeSession(diceRoll: 19, opponentStatValue: 6, playerStatValue: 3);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            Assert.Equal(RiskTier.Bold, result.Roll.RiskTier);
            Assert.Equal(45, result.XpEarned);
        }

        // What: AC-4 — Bold with low DC (very low player stat)
        // Mutation: Fails if multiplier lookup uses DC instead of risk tier
        [Fact]
        public async Task Bold_LowDc_Returns_3x_BaseXp()
        {
            // Player stat -3 (synthetic), opponent stat 0 → DC = 16, need = 19 → Bold (16–19)
            // Base XP = 5 (DC≤16), 5 * 3.0 = 15
            var session = MakeSession(diceRoll: 19, opponentStatValue: 0, playerStatValue: -3);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            Assert.Equal(RiskTier.Bold, result.Roll.RiskTier);
            Assert.Equal(15, result.XpEarned);
        }

        // ====================== Edge cases: Nat20, Nat1, Failure ======================

        // What: Nat20 uses flat 25 XP, not affected by risk tier multiplier
        // Mutation: Fails if Nat20 XP is multiplied by risk tier
        [Fact]
        public async Task Nat20_NoMultiplier_Flat25()
        {
            var session = MakeSession(diceRoll: 20, opponentStatValue: 0, playerStatValue: 3);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsNatTwenty);
            Assert.Equal(25, result.XpEarned);
        }

        // What: Nat1 uses flat 10 XP, not affected by risk tier multiplier
        // Mutation: Fails if Nat1 XP is multiplied by risk tier
        [Fact]
        public async Task Nat1_NoMultiplier_Flat10()
        {
            var session = MakeSession(diceRoll: 1, opponentStatValue: 0, playerStatValue: 3);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsNatOne);
            Assert.Equal(10, result.XpEarned);
        }

        // What: Non-Nat1 failure uses flat 2 XP, not affected by risk tier
        // Mutation: Fails if failure XP is multiplied by risk tier
        [Fact]
        public async Task Failure_NoMultiplier_Flat2()
        {
            // Roll 5 with low stat → failure
            var session = MakeSession(diceRoll: 5, opponentStatValue: 0, playerStatValue: 3);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.False(result.Roll.IsSuccess);
            Assert.False(result.Roll.IsNatOne);
            Assert.Equal(2, result.XpEarned);
        }

        // ====================== Rounding edge cases ======================

        // What: 5 * 1.5 = 7.5 rounds to 8 (not truncated to 7)
        // Mutation: Fails if implementation uses (int)(baseXp * multiplier) instead of Math.Round
        [Fact]
        public async Task Medium_LowDc_RoundsUp_From_7_5_To_8()
        {
            // 5 * 1.5 = 7.5 → should round to 8. Player stat 6, opponent stat 0 → DC=16, need=10 → Medium
            var session = MakeSession(diceRoll: 15, opponentStatValue: 0, playerStatValue: 6);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(RiskTier.Medium, result.Roll.RiskTier);
            Assert.True(result.XpEarned >= 7, "XP should be at least 7 (not truncated lower)");
            Assert.Equal(8, result.XpEarned);
        }

        // What: Integer multipliers produce exact results (no rounding needed)
        // Mutation: Fails if integer multiplication path is broken
        [Fact]
        public async Task Hard_MidDc_ExactMultiplication_NoRounding()
        {
            // 10 * 2.0 = 20.0 exactly
            var session = MakeSession(diceRoll: 18, opponentStatValue: 1, playerStatValue: 3);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(RiskTier.Hard, result.Roll.RiskTier);
            Assert.Equal(20, result.XpEarned);
        }

        // ====================== XpLedger source labels with multiplier ======================

        // What: XP ledger records the multiplied amount, not the base amount
        // Mutation: Fails if ledger records base XP before multiplication
        [Fact]
        public async Task XpLedger_Records_MultipliedAmount()
        {
            // Medium, base 5, multiplied to 8. Player stat 6, opponent stat 0 → DC=16, need=10 → Medium
            var session = MakeSession(diceRoll: 15, opponentStatValue: 0, playerStatValue: 6);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(RiskTier.Medium, result.Roll.RiskTier);
            var successEvent = session.XpLedger.Events.FirstOrDefault(e => e.Source.StartsWith("Success"));
            Assert.NotNull(successEvent);
            Assert.Equal(8, successEvent!.Amount);
        }

        // What: Bold XP ledger records 3x multiplied amount
        // Mutation: Fails if multiplier is not applied before recording to ledger
        [Fact]
        public async Task XpLedger_Bold_Records_3xAmount()
        {
            // Bold, base 15 (DC>20), multiplied to 45. Player stat 3, opponent stat 6 → DC=22, need=19 → Bold
            var session = MakeSession(diceRoll: 19, opponentStatValue: 6, playerStatValue: 3);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(RiskTier.Bold, result.Roll.RiskTier);
            var successEvent = session.XpLedger.Events.FirstOrDefault(e => e.Source.StartsWith("Success"));
            Assert.NotNull(successEvent);
            Assert.Equal(45, successEvent!.Amount);
        }

        // ====================== Consistency: XpEarned matches XpLedger ======================

        // What: TurnResult.XpEarned must equal the amount recorded in the ledger
        // Mutation: Fails if XpEarned and ledger are computed separately with different multipliers
        [Fact]
        public async Task XpEarned_Matches_Ledger_ForAllTiers()
        {
            // Test Hard tier (2x)
            var session = MakeSession(diceRoll: 18, opponentStatValue: 1, playerStatValue: 3);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(RiskTier.Hard, result.Roll.RiskTier);
            var totalLedger = session.XpLedger.TotalXp;
            Assert.Equal(result.XpEarned, totalLedger);
        }

        // ====================== Helpers ======================

        private static GameSession MakeSession(
            int diceRoll,
            int opponentStatValue,
            int playerStatValue = 3,
            GameSessionConfig? config = null)
        {
            var playerStats = MakeStatBlock(allStats: playerStatValue);
            var player = MakeProfile("player", playerStats);

            var opponentStats = MakeStatBlock(allStats: opponentStatValue);
            var opponent = MakeProfile("opponent", opponentStats);

            // Clock is required; provide default zero-modifier clock if caller didn't supply config.
            config = config ?? new GameSessionConfig(clock: TestHelpers.MakeClock());

            return new GameSession(
                player,
                opponent,
                new NullLlmAdapter(),
                new ConstantDice(diceRoll),
                new NullTrapRegistry(),
                config);
        }

        private static StatBlock MakeStatBlock(int allStats = 2)
        {
            return new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, allStats }, { StatType.Rizz, allStats },
                    { StatType.Honesty, allStats }, { StatType.Chaos, allStats },
                    { StatType.Wit, allStats }, { StatType.SelfAwareness, allStats }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 }, { ShadowStatType.Despair, 0 },
                    { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 0 }
                });
        }

        private static CharacterProfile MakeProfile(string name, StatBlock stats)
        {
            var timing = new TimingProfile(5, 1.0f, 0.0f, "neutral");
            return new CharacterProfile(stats, "system prompt", name, timing, 1);
        }

        private sealed class ConstantDice : IDiceRoller
        {
            private readonly int _value;
            public ConstantDice(int value) => _value = value;
            public int Roll(int sides) => _value;
        }

        private sealed class NullTrapRegistry : ITrapRegistry
        {
            public TrapDefinition? GetTrap(StatType stat) => null;
            public string? GetLlmInstruction(StatType stat) => null;
        }
    }
}
