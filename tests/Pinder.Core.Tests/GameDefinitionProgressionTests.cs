using System;
using System.Collections.Generic;
using Xunit;
using Pinder.LlmAdapters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Progression;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;

namespace Pinder.Core.Tests
{
    public class GameDefinitionProgressionTests
    {
        private const string ValidYamlWithProgression = @"
name: ""TestGame""
game_master_prompt: ""GM Prompt""
player_avatar_role_description: ""Player Description""
datee_role_description: ""Datee Description""
global_dc_bias: 0
max_turns: 30
max_dialogue_options: 3
max_delivery_words: 80
active_trap_interest_penalty: -0.25
hunger_for_intimacy: 0
terror_of_rejection: 0
horniness_time_modifiers:
  morning: 3
  afternoon: 0
  evening: 2
  overnight: 5
xp_flat_awards:
  nat20: 25
  nat1: 10
  failure: 2
xp_success_base:
  dc_low_max: 16
  dc_low_xp: 5
  dc_mid_max: 20
  dc_mid_xp: 10
  dc_high_xp: 15
xp_risk_multipliers:
  safe: 1.0
  medium: 1.5
  hard: 2.0
  bold: 3.0
  reckless: 10.0
xp_terminal_multipliers:
  date_secured: 3.0
  unmatched: 1.0
  ghosted: 0.0
progression_xp_thresholds:
  ""1"": 0
  ""2"": 50
  ""3"": 150
  ""4"": 300
  ""5"": 500
  ""6"": 750
  ""7"": 1100
  ""8"": 1500
  ""9"": 2000
  ""10"": 2750
  ""11"": 3500
progression_build_points:
  ""1"": 0
  ""2"": 2
  ""3"": 2
  ""4"": 2
  ""5"": 3
  ""6"": 3
  ""7"": 3
  ""8"": 4
  ""9"": 4
  ""10"": 5
  ""11"": 0
progression_level_bonuses:
  ""1"": 0
  ""2"": 0
  ""3"": 1
  ""4"": 1
  ""5"": 2
  ""6"": 2
  ""7"": 3
  ""8"": 3
  ""9"": 4
  ""10"": 4
  ""11"": 5
progression_item_slots:
  ""1"": 2
  ""2"": 2
  ""3"": 3
  ""4"": 3
  ""5"": 4
  ""6"": 4
  ""7"": 5
  ""8"": 5
  ""9"": 6
  ""10"": 6
  ""11"": 6
progression_failure_pool_tiers:
  intermediate_min: 4
  advanced_min: 7
  legendary_min: 10
progression_currency_per_xp: 10
character_prompt_structure:
  character_spec_header: ""== CHARACTER YOU CONTROL ==""
  player_avatar_character_tag: ""PLAYER_AVATAR_CHARACTER""
  datee_character_tag: ""DATEE_CHARACTER""
";

        [Fact]
        public void LoadFrom_WithValidProgressionYaml_ParsesAllBlocksSuccessfully()
        {
            var gd = GameDefinition.LoadFrom(ValidYamlWithProgression);

            Assert.False(gd.AllowDefaultFallback);

            // 1. xp_flat_awards
            Assert.NotNull(gd.XpFlatAwards);
            Assert.Equal(25, gd.XpFlatAwards["nat20"]);
            Assert.Equal(10, gd.XpFlatAwards["nat1"]);
            Assert.Equal(2, gd.XpFlatAwards["failure"]);

            // 2. xp_success_base
            Assert.NotNull(gd.XpSuccessBase);
            Assert.Equal(16, gd.XpSuccessBase["dc_low_max"]);
            Assert.Equal(5, gd.XpSuccessBase["dc_low_xp"]);
            Assert.Equal(20, gd.XpSuccessBase["dc_mid_max"]);
            Assert.Equal(10, gd.XpSuccessBase["dc_mid_xp"]);
            Assert.Equal(15, gd.XpSuccessBase["dc_high_xp"]);

            // 3. xp_risk_multipliers
            Assert.NotNull(gd.XpRiskMultipliers);
            Assert.Equal(1.0, gd.XpRiskMultipliers["safe"]);
            Assert.Equal(1.5, gd.XpRiskMultipliers["medium"]);
            Assert.Equal(2.0, gd.XpRiskMultipliers["hard"]);
            Assert.Equal(3.0, gd.XpRiskMultipliers["bold"]);
            Assert.Equal(10.0, gd.XpRiskMultipliers["reckless"]);

            // 4. xp_terminal_multipliers
            Assert.NotNull(gd.XpTerminalMultipliers);
            Assert.Equal(3.0, gd.XpTerminalMultipliers["date_secured"]);
            Assert.Equal(1.0, gd.XpTerminalMultipliers["unmatched"]);
            Assert.Equal(0.0, gd.XpTerminalMultipliers["ghosted"]);

            // 5. progression_xp_thresholds
            Assert.NotNull(gd.ProgressionXpThresholds);
            Assert.Equal(0, gd.ProgressionXpThresholds["1"]);
            Assert.Equal(50, gd.ProgressionXpThresholds["2"]);
            Assert.Equal(3500, gd.ProgressionXpThresholds["11"]);

            // 6. progression_build_points
            Assert.NotNull(gd.ProgressionBuildPoints);
            Assert.Equal(0, gd.ProgressionBuildPoints["1"]);
            Assert.Equal(2, gd.ProgressionBuildPoints["2"]);
            Assert.Equal(5, gd.ProgressionBuildPoints["10"]);

            // 7. progression_level_bonuses
            Assert.NotNull(gd.ProgressionLevelBonuses);
            Assert.Equal(0, gd.ProgressionLevelBonuses["1"]);
            Assert.Equal(1, gd.ProgressionLevelBonuses["3"]);
            Assert.Equal(5, gd.ProgressionLevelBonuses["11"]);

            // 8. progression_item_slots
            Assert.NotNull(gd.ProgressionItemSlots);
            Assert.Equal(2, gd.ProgressionItemSlots["1"]);
            Assert.Equal(3, gd.ProgressionItemSlots["3"]);
            Assert.Equal(6, gd.ProgressionItemSlots["11"]);

            // 9. progression_failure_pool_tiers
            Assert.NotNull(gd.ProgressionFailurePoolTiers);
            Assert.Equal(4, gd.ProgressionFailurePoolTiers["intermediate_min"]);
            Assert.Equal(7, gd.ProgressionFailurePoolTiers["advanced_min"]);
            Assert.Equal(10, gd.ProgressionFailurePoolTiers["legendary_min"]);
            Assert.Equal(10, gd.ProgressionCurrencyPerXp);
        }

        [Theory]
        [InlineData("xp_flat_awards")]
        [InlineData("xp_success_base")]
        [InlineData("xp_risk_multipliers")]
        [InlineData("xp_terminal_multipliers")]
        [InlineData("progression_xp_thresholds")]
        [InlineData("progression_build_points")]
        [InlineData("progression_level_bonuses")]
        [InlineData("progression_item_slots")]
        [InlineData("progression_failure_pool_tiers")]
        [InlineData("progression_currency_per_xp")]
        public void LoadFrom_MissingTopLevelKey_ThrowsException(string missingKey)
        {
            var lines = ValidYamlWithProgression.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var filteredLines = new List<string>();
            bool skippingBlock = false;

            foreach (var line in lines)
            {
                if (line.StartsWith(missingKey + ":"))
                {
                    skippingBlock = true;
                    continue;
                }
                if (skippingBlock && (line.StartsWith(" ") || line.StartsWith("\t") || string.IsNullOrWhiteSpace(line)))
                {
                    continue;
                }
                skippingBlock = false;
                filteredLines.Add(line);
            }

            var badYaml = string.Join("\n", filteredLines);

            // GameDefinition.LoadFrom must throw KeyNotFoundException, ConfigurationException, or FormatException containing key name
            var ex = Assert.ThrowsAny<Exception>(() => GameDefinition.LoadFrom(badYaml));
            Assert.True(
                ex is KeyNotFoundException || 
                ex is FormatException || 
                ex.Message.Contains(missingKey, StringComparison.OrdinalIgnoreCase),
                $"Expected exception containing '{missingKey}' or a key-related exception, but got {ex.GetType().Name}: {ex.Message}");
        }

        [Theory]
        [InlineData("xp_flat_awards", "nat20")]
        [InlineData("xp_success_base", "dc_low_max")]
        [InlineData("xp_risk_multipliers", "safe")]
        [InlineData("xp_terminal_multipliers", "date_secured")]
        [InlineData("progression_xp_thresholds", "1")]
        [InlineData("progression_xp_thresholds", "11")]
        [InlineData("progression_build_points", "7")]
        [InlineData("progression_level_bonuses", "7")]
        [InlineData("progression_item_slots", "7")]
        [InlineData("progression_failure_pool_tiers", "intermediate_min")]
        [InlineData("progression_failure_pool_tiers", "legendary_min")]
        public void LoadFrom_MissingSubKey_ThrowsException(string parentKey, string subKey)
        {
            var lines = ValidYamlWithProgression.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var filteredLines = new List<string>();
            bool insideParent = false;

            foreach (var line in lines)
            {
                if (line.StartsWith(parentKey + ":"))
                {
                    insideParent = true;
                    filteredLines.Add(line);
                    continue;
                }
                if (insideParent)
                {
                    if (line.StartsWith(" ") || line.StartsWith("\t"))
                    {
                        if (line.Trim().StartsWith(subKey + ":") || line.Trim().StartsWith($"\"{subKey}\":") || line.Trim().StartsWith($"'{subKey}':"))
                        {
                            // skip this sub-key
                            continue;
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(line))
                    {
                        insideParent = false;
                    }
                }
                filteredLines.Add(line);
            }

            var badYaml = string.Join("\n", filteredLines);

            var ex = Assert.ThrowsAny<Exception>(() => GameDefinition.LoadFrom(badYaml));
            Assert.True(
                ex is KeyNotFoundException || 
                ex is FormatException || 
                ex.Message.Contains(subKey, StringComparison.OrdinalIgnoreCase),
                $"Expected exception containing '{subKey}' or a key-related exception, but got {ex.GetType().Name}: {ex.Message}");
        }

        [Fact]
        public void SessionXpRecorder_ResolvesConfiguredFlatAwards_WithNoSilentFallbacks()
        {
            var ledger = new XpLedger();
            var resolver = new FakeRuleResolver();

            // Setup valid flat awards
            resolver.FlatAwards["Nat20"] = 100;
            resolver.FlatAwards["Nat1"] = 40;
            resolver.FlatAwards["Failure"] = 5;

            var recorder = new SessionXpRecorder(ledger, resolver);

            // Test Nat 20
            var rollNat20 = new RollResult(20, null, 20, StatType.Charm, 0, 0, 10, FailureTier.Success);
            recorder.RecordRollXp(rollNat20);
            Assert.Equal(100, ledger.TotalXp);

            // Test Nat 1
            var rollNat1 = new RollResult(1, null, 1, StatType.Charm, 0, 0, 10, FailureTier.Fumble);
            recorder.RecordRollXp(rollNat1);
            Assert.Equal(140, ledger.TotalXp); // 100 + 40

            // Test Failure
            var rollFailure = new RollResult(10, null, 10, StatType.Charm, 0, 0, 15, FailureTier.Misfire);
            recorder.RecordRollXp(rollFailure);
            Assert.Equal(145, ledger.TotalXp); // 140 + 5
        }

        [Fact]
        public void SessionXpRecorder_MissingFlatAwardConfig_ThrowsException()
        {
            var ledger = new XpLedger();
            var resolver = new FakeRuleResolver();

            // Let one of them return null (simulating missing config value with no silent fallbacks)
            resolver.FlatAwards["Nat20"] = null;

            var recorder = new SessionXpRecorder(ledger, resolver);
            var roll = new RollResult(20, null, 20, StatType.Charm, 0, 0, 10, FailureTier.Success);

            Assert.ThrowsAny<Exception>(() => recorder.RecordRollXp(roll));
        }

        [Fact]
        public void SessionXpRecorder_ResolvesSuccessBaseAndRiskMultipliers_WithNoSilentFallbacks()
        {
            var ledger = new XpLedger();
            var resolver = new FakeRuleResolver();

            // Setup resolver
            resolver.SuccessBase[12] = 8; // base XP for low DC
            resolver.SuccessDcThresholds = new SuccessDcLabelThresholds(12, 18);
            resolver.RiskMultipliers[RiskTier.Hard] = 2.5;

            var recorder = new SessionXpRecorder(ledger, resolver);

            // Hard check success
            // UsedDieRoll = 14, modifiers such that need is 14 -> Hard risk tier
            // (UsedDieRoll + Mod + LevelBonus = 14 + 0 + 0 = 14) >= DC (12)
            var roll = new RollResult(14, null, 14, StatType.Charm, 0, 0, 12, FailureTier.Success);
            
            recorder.RecordRollXp(roll);

            // Expected XP = 8 * 2.5 = 20 XP
            Assert.Equal(20, ledger.TotalXp);
        }

        [Fact]
        public void SessionXpRecorder_LabelsSuccessXpUsingConfiguredDcThresholds()
        {
            var ledger = new XpLedger();
            var resolver = new FakeRuleResolver
            {
                SuccessDcThresholds = new SuccessDcLabelThresholds(lowMax: 11, midMax: 13)
            };
            resolver.SuccessBase[11] = 5;
            resolver.SuccessBase[12] = 10;
            resolver.SuccessBase[14] = 15;
            resolver.RiskMultipliers[RiskTier.Medium] = 1.0;
            resolver.RiskMultipliers[RiskTier.Hard] = 1.0;

            var recorder = new SessionXpRecorder(ledger, resolver);

            recorder.RecordRollXp(new RollResult(11, null, 11, StatType.Charm, 0, 0, 11, FailureTier.Success));
            recorder.RecordRollXp(new RollResult(12, null, 12, StatType.Charm, 0, 0, 12, FailureTier.Success));
            recorder.RecordRollXp(new RollResult(14, null, 14, StatType.Charm, 0, 0, 14, FailureTier.Success));

            Assert.Collection(
                ledger.Events,
                e => Assert.Equal("Success_DC_Low", e.Source),
                e => Assert.Equal("Success_DC_Mid", e.Source),
                e => Assert.Equal("Success_DC_High", e.Source));
        }

        [Fact]
        public void GameDefinition_InvalidSuccessDcThresholdOrder_ThrowsFromTypedResolver()
        {
            var yaml = ValidYamlWithProgression
                .Replace("dc_low_max: 16", "dc_low_max: 21");

            var gd = GameDefinition.LoadFrom(yaml);

            var ex = Assert.Throws<ConfigurationException>(() => gd.GetSuccessDcLabelThresholds());
            Assert.Contains("dc_low_max", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("dc_mid_max", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SessionXpRecorder_MissingSuccessBaseOrMultiplierConfig_ThrowsException()
        {
            var ledger = new XpLedger();
            var resolver = new FakeRuleResolver();

            // Setup base but missing multiplier
            resolver.SuccessBase[12] = 8;
            resolver.RiskMultipliers[RiskTier.Hard] = null;

            var recorder = new SessionXpRecorder(ledger, resolver);
            var roll = new RollResult(14, null, 14, StatType.Charm, 0, 0, 12, FailureTier.Success);

            Assert.ThrowsAny<Exception>(() => recorder.RecordRollXp(roll));
        }

        [Fact]
        public void SessionXpRecorder_ResolvesTerminalMultiplier_WithNoSilentFallbacks()
        {
            var ledger = new XpLedger();
            var resolver = new FakeRuleResolver();

            resolver.FlatAwards["Nat20"] = 100;
            resolver.TerminalMultipliers[GameOutcome.DateSecured] = 4.0;

            var recorder = new SessionXpRecorder(ledger, resolver);
            var roll = new RollResult(20, null, 20, StatType.Charm, 0, 0, 10, FailureTier.Success);
            recorder.RecordRollXp(roll); // Adds 100 XP

            recorder.RecordEndOfGameXp(GameOutcome.DateSecured);

            // 100 * 4.0 = 400 XP
            Assert.Equal(400, ledger.TotalXp);
        }

        [Fact]
        public void SessionXpRecorder_MissingTerminalMultiplierConfig_ThrowsException()
        {
            var ledger = new XpLedger();
            var resolver = new FakeRuleResolver();

            resolver.FlatAwards["Nat20"] = 100;
            resolver.TerminalMultipliers[GameOutcome.DateSecured] = null;

            var recorder = new SessionXpRecorder(ledger, resolver);
            var roll = new RollResult(20, null, 20, StatType.Charm, 0, 0, 10, FailureTier.Success);
            recorder.RecordRollXp(roll);

            Assert.ThrowsAny<Exception>(() => recorder.RecordEndOfGameXp(GameOutcome.DateSecured));
        }

        [Fact]
        public void LevelTable_ResolvesConfiguredProgressionValues_WithNoSilentFallbacks()
        {
            var resolver = new FakeRuleResolver();

            resolver.XpThresholds[1] = 0;
            resolver.XpThresholds[2] = 60;
            resolver.XpThresholds[3] = 180;
            resolver.XpThresholds[4] = 400;

            resolver.LevelBonuses[3] = 2;
            resolver.BuildPoints[3] = 4;
            resolver.ItemSlots[3] = 5;

            resolver.FailurePoolTierMinLevels["intermediate_min"] = 4;
            resolver.FailurePoolTierMinLevels["advanced_min"] = 7;
            resolver.FailurePoolTierMinLevels["legendary_min"] = 10;

            // Verify GetLevel using thresholds
            Assert.Equal(1, LevelTable.GetLevel(59, resolver));
            Assert.Equal(2, LevelTable.GetLevel(60, resolver));
            Assert.Equal(2, LevelTable.GetLevel(179, resolver));
            Assert.Equal(3, LevelTable.GetLevel(180, resolver));

            // Verify getters
            Assert.Equal(2, LevelTable.GetBonus(3, resolver));
            Assert.Equal(4, LevelTable.GetBuildPointsForLevel(3, resolver));
            Assert.Equal(5, LevelTable.GetItemSlots(3, resolver));
            Assert.Equal(FailurePoolTier.Intermediate, LevelTable.GetFailurePoolTier(5, resolver));
        }

        [Fact]
        public void LevelTable_MissingProgressionValueConfig_ThrowsException()
        {
            var resolver = new FakeRuleResolver();

            // Return null for a queried value, mimicking missing configuration
            resolver.LevelBonuses[3] = null;

            Assert.ThrowsAny<Exception>(() => LevelTable.GetBonus(3, resolver));
        }

        [Fact]
        public void LevelTable_MissingProductionXpThresholds_ThrowsInsteadOfUsingDefaultResolver()
        {
            var resolver = new FakeRuleResolver();

            var ex = Assert.Throws<InvalidOperationException>(() => LevelTable.GetLevel(50, resolver));

            Assert.Contains("progression XP threshold", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void LevelTable_MissingProductionFailurePoolTiers_ThrowsInsteadOfHardcodedTierDefaults()
        {
            var resolver = new FakeRuleResolver();

            var ex = Assert.Throws<InvalidOperationException>(() => LevelTable.GetFailurePoolTier(10, resolver));

            Assert.Contains("failure pool tiers", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        private class FakeRuleResolver : IRuleResolver
        {
            public Dictionary<string, int?> FlatAwards { get; set; } = new();
            public Dictionary<int, int?> SuccessBase { get; set; } = new();
            public SuccessDcLabelThresholds? SuccessDcThresholds { get; set; }
            public Dictionary<RiskTier, double?> RiskMultipliers { get; set; } = new();
            public Dictionary<GameOutcome, double?> TerminalMultipliers { get; set; } = new();
            public Dictionary<int, int?> XpThresholds { get; set; } = new();
            public Dictionary<int, int?> LevelBonuses { get; set; } = new();
            public Dictionary<int, int?> BuildPoints { get; set; } = new();
            public Dictionary<int, int?> ItemSlots { get; set; } = new();
            public Dictionary<string, int?> FailurePoolTierMinLevels { get; set; } = new();

            public int? GetFailureInterestDelta(int missMargin, int naturalRoll) => null;
            public int? GetSuccessInterestDelta(int beatMargin, int naturalRoll) => null;
            public InterestState? GetInterestState(int interest) => null;
            public int? GetShadowThresholdLevel(int shadowValue) => null;
            public int? GetMomentumBonus(int streak) => null;

            public double? GetRiskTierXpMultiplier(RiskTier riskTier) => 
                RiskMultipliers.TryGetValue(riskTier, out var v) ? v : null;

            public double? GetTerminalOutcomeMultiplier(GameOutcome outcome) => 
                TerminalMultipliers.TryGetValue(outcome, out var v) ? v : null;

            public int? GetSuccessBaseXp(int dc) => 
                SuccessBase.TryGetValue(dc, out var v) ? v : null;

            public SuccessDcLabelThresholds? GetSuccessDcLabelThresholds() => SuccessDcThresholds;

            public int? GetFlatXpAward(string awardType) => 
                FlatAwards.TryGetValue(awardType, out var v) ? v : null;

            public int? GetXpThresholdForLevel(int level) => 
                XpThresholds.TryGetValue(level, out var v) ? v : null;

            public int? GetLevelRollBonus(int level) => 
                LevelBonuses.TryGetValue(level, out var v) ? v : null;

            public int? GetBuildPointsForLevel(int level) => 
                BuildPoints.TryGetValue(level, out var v) ? v : null;

            public int? GetItemSlotsForLevel(int level) => 
                ItemSlots.TryGetValue(level, out var v) ? v : null;

            public int? GetFailurePoolTierMinLevel(string tierName) =>
                FailurePoolTierMinLevels.TryGetValue(tierName, out var v) ? v : null;

            public int? GetProgressionCurrencyPerXp() => 10;

            // These tests assert "no silent fallback": a missing config value must throw,
            // not quietly resolve to a hardcoded default. Explicitly opt out of fallback.
            public bool AllowDefaultFallback => false;
        }
    }
}
