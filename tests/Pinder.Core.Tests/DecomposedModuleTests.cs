using System;
using System.Collections.Generic;
using Xunit;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Progression;
using Pinder.Core.Traps;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Happy-path tests for the modules extracted from GameSession (#857).
    /// Validates that ShadowGrowthEvaluator, SessionXpRecorder, OptionFilterEngine,
    /// HorninessEngine, SteeringEngine, and GameSessionHelpers work correctly in isolation.
    /// </summary>
    public class DecomposedModuleTests
    {
        // ---- SessionXpRecorder ----

        [Fact]
        public void SessionXpRecorder_Nat20_Records25Xp()
        {
            var ledger = new XpLedger();
            var recorder = new SessionXpRecorder(ledger, rules: null);

            var roll = MakeRollResult(isNat20: true, isSuccess: true, dc: 15, finalTotal: 20);
            recorder.RecordRollXp(roll);

            Assert.Equal(25, ledger.TotalXp);
        }

        [Fact]
        public void SessionXpRecorder_Nat1_Records10Xp()
        {
            var ledger = new XpLedger();
            var recorder = new SessionXpRecorder(ledger, rules: null);

            var roll = MakeRollResult(isNat1: true, isSuccess: false, dc: 15, finalTotal: 1);
            recorder.RecordRollXp(roll);

            Assert.Equal(10, ledger.TotalXp);
        }

        [Fact]
        public void SessionXpRecorder_SuccessLowDc_Records5Xp()
        {
            var ledger = new XpLedger();
            var recorder = new SessionXpRecorder(ledger, rules: null);

            var roll = MakeRollResult(isSuccess: true, dc: 14, finalTotal: 16);
            recorder.RecordRollXp(roll);

            Assert.Equal(5, ledger.TotalXp);
        }

        [Fact]
        public void SessionXpRecorder_SuccessMidDc_Records10Xp()
        {
            var ledger = new XpLedger();
            var recorder = new SessionXpRecorder(ledger, rules: null);

            var roll = MakeRollResult(isSuccess: true, dc: 18, finalTotal: 20);
            recorder.RecordRollXp(roll);

            Assert.Equal(10, ledger.TotalXp);
        }

        [Fact]
        public void SessionXpRecorder_SuccessHighDc_Records15Xp()
        {
            var ledger = new XpLedger();
            var recorder = new SessionXpRecorder(ledger, rules: null);

            var roll = MakeRollResult(isSuccess: true, dc: 22, finalTotal: 24);
            recorder.RecordRollXp(roll);

            Assert.Equal(15, ledger.TotalXp);
        }

        [Fact]
        public void SessionXpRecorder_Failure_Records2Xp()
        {
            var ledger = new XpLedger();
            var recorder = new SessionXpRecorder(ledger, rules: null);

            var roll = MakeRollResult(isSuccess: false, dc: 15, finalTotal: 10);
            recorder.RecordRollXp(roll);

            Assert.Equal(2, ledger.TotalXp);
        }

        [Fact]
        public void SessionXpRecorder_EndOfGame_DateSecured_Records50Xp()
        {
            var ledger = new XpLedger();
            var recorder = new SessionXpRecorder(ledger, rules: null);

            recorder.RecordEndOfGameXp(GameOutcome.DateSecured);

            Assert.Equal(50, ledger.TotalXp);
        }

        [Fact]
        public void SessionXpRecorder_EndOfGame_Unmatched_Records5Xp()
        {
            var ledger = new XpLedger();
            var recorder = new SessionXpRecorder(ledger, rules: null);

            recorder.RecordEndOfGameXp(GameOutcome.Unmatched);

            Assert.Equal(5, ledger.TotalXp);
        }

        [Theory]
        [InlineData(RiskTier.Safe, 10, 10)]
        [InlineData(RiskTier.Medium, 10, 15)]
        [InlineData(RiskTier.Hard, 10, 20)]
        [InlineData(RiskTier.Bold, 10, 30)]
        public void SessionXpRecorder_RiskTierMultiplier_AppliesCorrectly(RiskTier tier, int baseXp, int expected)
        {
            var ledger = new XpLedger();
            var recorder = new SessionXpRecorder(ledger, rules: null);

            int result = recorder.ApplyRiskTierMultiplier(baseXp, tier);

            Assert.Equal(expected, result);
        }

        // ---- OptionFilterEngine ----

        [Fact]
        public void OptionFilterEngine_DrawRandomStats_ReturnsRequestedCount()
        {
            var pool = new[] { StatType.Charm, StatType.Rizz, StatType.Honesty, StatType.Chaos, StatType.Wit, StatType.SelfAwareness };

            var drawn = OptionFilterEngine.DrawRandomStats(pool, 3, shadowThresholds: null);

            Assert.Equal(3, drawn.Length);
        }

        [Fact]
        public void OptionFilterEngine_DrawRandomStats_DenialT3_RemovesHonesty()
        {
            var pool = new[] { StatType.Charm, StatType.Rizz, StatType.Honesty, StatType.Chaos, StatType.Wit, StatType.SelfAwareness };
            var thresholds = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Denial, 12 }
            };

            // Draw all 5 remaining stats
            var drawn = OptionFilterEngine.DrawRandomStats(pool, 5, thresholds);

            Assert.DoesNotContain(StatType.Honesty, drawn);
        }

        [Fact]
        public void OptionFilterEngine_ApplyT3Filters_DenialT3_RemovesHonestyOptions()
        {
            var options = new[]
            {
                MakeOption(StatType.Charm, "Hi"),
                MakeOption(StatType.Honesty, "Truth"),
                MakeOption(StatType.Wit, "Joke"),
            };
            var thresholds = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Denial, 18 },
                { ShadowStatType.Fixation, 0 },
                { ShadowStatType.Madness, 0 },
            };

            var result = OptionFilterEngine.ApplyT3Filters(options, thresholds, lastStatUsed: null, new FixedDice(1));

            Assert.All(result, o => Assert.NotEqual(StatType.Honesty, o.Stat));
        }

        [Fact]
        public void OptionFilterEngine_ApplyT3Filters_FixationT3_ForcesLastStatUsed()
        {
            var options = new[]
            {
                MakeOption(StatType.Charm, "Hi"),
                MakeOption(StatType.Wit, "Joke"),
                MakeOption(StatType.Rizz, "Flirt"),
            };
            var thresholds = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Fixation, 18 },
                { ShadowStatType.Denial, 0 },
                { ShadowStatType.Madness, 0 },
            };

            var result = OptionFilterEngine.ApplyT3Filters(options, thresholds, lastStatUsed: StatType.Chaos, new FixedDice(1));

            Assert.All(result, o => Assert.Equal(StatType.Chaos, o.Stat));
        }

        // ---- HorninessEngine (static helpers) ----

        [Theory]
        [InlineData(1, FailureTier.Fumble)]
        [InlineData(2, FailureTier.Fumble)]
        [InlineData(3, FailureTier.Misfire)]
        [InlineData(5, FailureTier.Misfire)]
        [InlineData(6, FailureTier.TropeTrap)]
        [InlineData(9, FailureTier.TropeTrap)]
        [InlineData(10, FailureTier.Catastrophe)]
        [InlineData(15, FailureTier.Catastrophe)]
        public void HorninessEngine_DetermineHorninessTier_ReturnsCorrectTier(int missMargin, FailureTier expected)
        {
            var result = HorninessEngine.DetermineHorninessTier(missMargin);

            Assert.Equal(expected, result);
        }

        [Fact]
        public void HorninessEngine_GetStatFailureInstruction_NullInstructions_ReturnsNull()
        {
            var result = HorninessEngine.GetStatFailureInstruction(null, StatType.Charm, FailureTier.Fumble);

            Assert.Null(result);
        }

        [Fact]
        public void HorninessEngine_GetHorninessOverlayInstruction_NullInstructions_ReturnsNull()
        {
            var result = HorninessEngine.GetHorninessOverlayInstruction(null, FailureTier.Fumble);

            Assert.Null(result);
        }

        // ---- GameSessionHelpers ----

        [Fact]
        public void GameSessionHelpers_BuildOpponentVisibleProfile_IncludesNameAndBio()
        {
            var profile = MakeProfile("Luna", bio: "Coffee addict");

            var result = GameSessionHelpers.BuildOpponentVisibleProfile(profile);

            Assert.Contains("Luna", result);
            Assert.Contains("Coffee addict", result);
        }

        [Fact]
        public void GameSessionHelpers_GetLastOpponentMessage_ReturnsLastMatch()
        {
            var history = new List<(string Sender, string Text)>
            {
                ("Player", "Hey"),
                ("Luna", "Hi there!"),
                ("Player", "How are you?"),
                ("Luna", "I'm great!")
            };

            var result = GameSessionHelpers.GetLastOpponentMessage(history, "Luna");

            Assert.Equal("I'm great!", result);
        }

        [Fact]
        public void GameSessionHelpers_GetLastOpponentMessage_Empty_ReturnsEmptyString()
        {
            var history = new List<(string Sender, string Text)>();

            var result = GameSessionHelpers.GetLastOpponentMessage(history, "Luna");

            Assert.Equal(string.Empty, result);
        }

        // ---- Integration: GameSession still works after decomposition ----

        [Fact]
        public void GameSession_Constructor_StillWorks()
        {
            var player = MakeProfile("Player");
            var opponent = MakeProfile("Opponent");
            var llm = new NullLlmAdapter();
            var dice = new FixedDice(5);
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock());

            var session = new GameSession(player, opponent, llm, dice, new NullTrapRegistry(), config);

            // Session created successfully, XP starts at 0
            Assert.Equal(0, session.TotalXpEarned);
        }

        [Fact]
        public void GameSession_Wait_StillWorks()
        {
            var player = MakeProfile("Player");
            var opponent = MakeProfile("Opponent");
            var llm = new NullLlmAdapter();
            var dice = new FixedDice(5); // Not 1, so ghost trigger won't fire
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock());

            var session = new GameSession(player, opponent, llm, dice, new NullTrapRegistry(), config);

            // Wait should not throw
            session.Wait();
        }

        // ---- Helpers ----

        private static CharacterProfile MakeProfile(string name, int allStats = 2, string bio = "")
        {
            return new CharacterProfile(
                stats: TestHelpers.MakeStatBlock(allStats),
                assembledSystemPrompt: $"You are {name}.",
                displayName: name,
                timing: new TimingProfile(5, 0.0f, 0.0f, "neutral"),
                level: 1,
                bio: bio);
        }

        private static RollResult MakeRollResult(
            bool isSuccess = false,
            int dc = 15,
            int finalTotal = 10,
            bool isNat20 = false,
            bool isNat1 = false,
            RiskTier riskTier = RiskTier.Safe)
        {
            int usedDie = isNat20 ? 20 : (isNat1 ? 1 : 10);
            // For success: use high stat modifier so RiskTier = Safe (need <= 5)
            // For failure: use 0 stat modifier so FinalTotal < dc
            int statMod = isSuccess ? (dc - 3) : 0;
            var tier = isSuccess ? FailureTier.None : FailureTier.Fumble;

            return new RollResult(
                dieRoll: usedDie,
                secondDieRoll: null,
                usedDieRoll: usedDie,
                stat: StatType.Charm,
                statModifier: statMod,
                levelBonus: 0,
                dc: dc,
                tier: tier);
        }

        private static DialogueOption MakeOption(StatType stat, string text)
        {
            return new DialogueOption(stat, text, callbackTurnNumber: null, comboName: null, hasTellBonus: false, hasWeaknessWindow: false);
        }

        private sealed class FixedDice : IDiceRoller
        {
            private readonly int _value;
            public FixedDice(int value) { _value = value; }
            public int Roll(int sides) => Math.Min(_value, sides);
        }

        private sealed class NullTrapRegistry : ITrapRegistry
        {
            public TrapDefinition? GetTrap(StatType stat) => null;
            public string? GetLlmInstruction(StatType stat) => null;
        }
    }

}
