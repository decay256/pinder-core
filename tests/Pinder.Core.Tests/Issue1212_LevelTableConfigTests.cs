using System;
using System.Collections.Generic;
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
    public class Issue1212_LevelTableConfigTests
    {
        // PART A — defaults unchanged (these likely PASS already; regression guards)

        [Fact]
        public void GetLevel_DefaultThresholdEdges()
        {
            Assert.Equal(1, LevelTable.GetLevel(49));
            Assert.Equal(2, LevelTable.GetLevel(50));
            Assert.Equal(2, LevelTable.GetLevel(149));
            Assert.Equal(3, LevelTable.GetLevel(150));
            Assert.Equal(1, LevelTable.GetLevel(0));
            Assert.Equal(11, LevelTable.GetLevel(3500));
            Assert.Equal(11, LevelTable.GetLevel(99999));
        }

        [Fact]
        public void GetBonus_GetItemSlots_GetBuildPoints_Defaults()
        {
            Assert.Equal(0, LevelTable.GetBonus(1));
            Assert.Equal(2, LevelTable.GetBonus(5));
            Assert.Equal(5, LevelTable.GetBonus(11));

            Assert.Equal(2, LevelTable.GetItemSlots(1));
            Assert.Equal(6, LevelTable.GetItemSlots(9));

            Assert.Equal(2, LevelTable.GetBuildPointsForLevel(2));
            Assert.Equal(5, LevelTable.GetBuildPointsForLevel(10));
        }

        // PART B — config override (these are the RED ones — they WON'T COMPILE until the optional rules param + IRuleResolver methods exist)

        [Fact]
        public void GetBonus_ConfigOverride_WinsOverDefault()
        {
            var fake = new FakeRuleResolver();
            Assert.Equal(99, LevelTable.GetBonus(3, fake));
            Assert.Equal(1, LevelTable.GetBonus(3, null));
        }

        [Fact]
        public void GetLevel_ConfigOverride_UsesConfiguredThresholds()
        {
            var fake = new FakeRuleResolver();
            // Default needs 50 for L2. Fake gives L2 at 10.
            Assert.Equal(2, LevelTable.GetLevel(10, fake));
            Assert.Equal(1, LevelTable.GetLevel(10, null));
        }

        [Fact]
        public void ItemSlots_BuildPoints_ConfigOverride()
        {
            var fake = new FakeRuleResolver();
            Assert.Equal(42, LevelTable.GetItemSlots(1, fake));
            Assert.Equal(7, LevelTable.GetBuildPointsForLevel(1, fake));
            
            Assert.Equal(2, LevelTable.GetItemSlots(1, null));
            Assert.Equal(0, LevelTable.GetBuildPointsForLevel(1, null));
        }

        // PART C — production wiring (end-to-end; proves config bonus applies in a live roll)

        [Fact]
        public async Task RollEngine_UsesConfiguredLevelBonus_InLiveTurn()
        {
            var fakeResolver = new FakeRuleResolver();
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), rules: fakeResolver);
            var session = MakeSession(diceRoll: 10, config: config);
            
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // FakeRuleResolver sets GetLevelRollBonus(level) = level == 1 ? 7 : 99
            // Player level is 1
            Assert.Equal(7, result.Roll.LevelBonus);
        }

        // Helpers

        private static GameSession MakeSession(
            int diceRoll,
            GameSessionConfig? config = null)
        {
            var playerStats = MakeStatBlock();
            var player = MakeProfile("player", playerStats);

            var dateeStats = MakeStatBlock();
            var datee = MakeProfile("datee", dateeStats);

            return new GameSession(
                player,
                datee,
                new NullLlmAdapter(),
                new ConstantDice(diceRoll),
                new NullTrapRegistry(),
                config ?? new GameSessionConfig(clock: TestHelpers.MakeClock()));
        }

        private static StatBlock MakeStatBlock()
        {
            return new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, 2 }, { StatType.Rizz, 2 },
                    { StatType.Honesty, 2 }, { StatType.Chaos, 2 },
                    { StatType.Wit, 2 }, { StatType.SelfAwareness, 2 }
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
            return TestHelpers.MakeCharacterProfile(
                stats,
                "system prompt",
                name,
                timing,
                1,
                psychiatricDiagnosis: TestHelpers.MakePsychiatricDiagnosis());
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

        // FAKE RESOLVER
        private sealed class FakeRuleResolver : IRuleResolver
        {
            // Existing interface methods
            public int? GetFailureInterestDelta(int missMargin, int naturalRoll) => null;
            public int? GetSuccessInterestDelta(int beatMargin, int naturalRoll) => null;
            public InterestState? GetInterestState(int interest) => null;
            public int? GetShadowThresholdLevel(int shadowValue) => null;
            public int? GetMomentumBonus(int streak) => null;
            public double? GetRiskTierXpMultiplier(RiskTier riskTier) => null;
            public double? GetTerminalOutcomeMultiplier(GameOutcome outcome) => null;
            public int? GetSuccessBaseXp(int dc) => null;
            public Pinder.Core.Progression.SuccessDcLabelThresholds? GetSuccessDcLabelThresholds() => null;
            public int? GetFlatXpAward(string awardType) => null;

            // NEW methods for issue #1212
            int? IRuleResolver.GetXpThresholdForLevel(int level)
            {
                if (level == 2) return 10;
                return null;
            }

            int? IRuleResolver.GetLevelRollBonus(int level)
            {
                if (level == 1) return 7;
                return 99; // For level 3 test
            }

            int? IRuleResolver.GetBuildPointsForLevel(int level)
            {
                return 7;
            }

            int? IRuleResolver.GetItemSlotsForLevel(int level)
            {
                return 42;
            }

            public int? GetFailurePoolTierMinLevel(string tierName) => null;

            public int? GetProgressionCurrencyPerXp() => 10;

            // Behaves like a production resolver: unresolved rules fall back to defaults.
            public bool AllowDefaultFallback => true;
        }
    }
}
