using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.Core.Tests
{
    [Trait("Category", "Core")]
    public class Issue644_ShadowHintComputerTests
    {
        private static StatBlock MakeStats(int charm = 3, int rizz = 2, int honesty = 1, int chaos = 0, int wit = 2, int sa = 1)
        {
            var baseStats = new Dictionary<StatType, int>
            {
                { StatType.Charm, charm },
                { StatType.Rizz, rizz },
                { StatType.Honesty, honesty },
                { StatType.Chaos, chaos },
                { StatType.Wit, wit },
                { StatType.SelfAwareness, sa }
            };
            var shadowStats = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Madness, 0 },
                { ShadowStatType.Despair, 0 },
                { ShadowStatType.Denial, 0 },
                { ShadowStatType.Fixation, 0 },
                { ShadowStatType.Dread, 0 },
                { ShadowStatType.Overthinking, 0 }
            };
            return new StatBlock(baseStats, shadowStats);
        }

        private static DialogueOption MakeOption(StatType stat, string comboName = null)
        {
            return new DialogueOption(stat, "test text", comboName: comboName);
        }

        private static ShadowHintContext MakeContext(
            List<StatType> statsHistory = null,
            List<bool> highestPctHistory = null,
            int interest = 10,
            int charmUsage = 0,
            bool charmTriggered = false,
            int saUsage = 0,
            bool saTriggered = false,
            int rizzFailures = 0,
            DialogueOption[] options = null,
            StatBlock playerStats = null,
            StatBlock opponentStats = null,
            int levelBonus = 0)
        {
            return new ShadowHintContext
            {
                StatsUsedHistory = statsHistory ?? new List<StatType>(),
                HighestPctHistory = highestPctHistory ?? new List<bool>(),
                CurrentInterest = interest,
                CharmUsageCount = charmUsage,
                CharmMadnessTriggered = charmTriggered,
                SaUsageCount = saUsage,
                SaOverthinkingTriggered = saTriggered,
                RizzCumulativeFailureCount = rizzFailures,
                CurrentOptions = options ?? new DialogueOption[0],
                PlayerStats = playerStats ?? MakeStats(),
                OpponentStats = opponentStats ?? MakeStats(),
                PlayerLevelBonus = levelBonus
            };
        }

        // ── Growth warnings ──────────────────────────────────────

        [Fact]
        public void SameStat3rdTurn_ShowsFixationWarning()
        {
            var history = new List<StatType> { StatType.Charm, StatType.Charm };
            var ctx = MakeContext(statsHistory: history);
            var option = MakeOption(StatType.Charm);

            var hints = ShadowHintComputer.ComputeShadowHints(option, ctx);

            Assert.Contains(hints, h => h.Contains("Fixation +1"));
        }

        [Fact]
        public void SameStat2ndTurn_NoFixationWarning()
        {
            var history = new List<StatType> { StatType.Charm };
            var ctx = MakeContext(statsHistory: history);
            var option = MakeOption(StatType.Charm);

            var hints = ShadowHintComputer.ComputeShadowHints(option, ctx);

            Assert.DoesNotContain(hints, h => h.Contains("Fixation +1"));
        }

        [Fact]
        public void DifferentStat_NoFixationWarning()
        {
            var history = new List<StatType> { StatType.Charm, StatType.Charm };
            var ctx = MakeContext(statsHistory: history);
            var option = MakeOption(StatType.Rizz);

            var hints = ShadowHintComputer.ComputeShadowHints(option, ctx);

            Assert.DoesNotContain(hints, h => h.Contains("Fixation +1") && !h.Contains("safe pick"));
        }

        [Fact]
        public void Charm3rdUse_ShowsMadnessWarning()
        {
            var ctx = MakeContext(charmUsage: 2, charmTriggered: false);
            var option = MakeOption(StatType.Charm);

            var hints = ShadowHintComputer.ComputeShadowHints(option, ctx);

            Assert.Contains(hints, h => h.Contains("Madness +1"));
        }

        [Fact]
        public void Charm3rdUse_AlreadyTriggered_NoWarning()
        {
            var ctx = MakeContext(charmUsage: 2, charmTriggered: true);
            var option = MakeOption(StatType.Charm);

            var hints = ShadowHintComputer.ComputeShadowHints(option, ctx);

            Assert.DoesNotContain(hints, h => h.Contains("Madness +1"));
        }

        [Fact]
        public void SA3rdUse_ShowsOverthinkingWarning()
        {
            var ctx = MakeContext(saUsage: 2, saTriggered: false);
            var option = MakeOption(StatType.SelfAwareness);

            var hints = ShadowHintComputer.ComputeShadowHints(option, ctx);

            Assert.Contains(hints, h => h.Contains("Overthinking +1"));
        }

        [Fact]
        public void SA3rdUse_AlreadyTriggered_NoWarning()
        {
            var ctx = MakeContext(saUsage: 2, saTriggered: true);
            var option = MakeOption(StatType.SelfAwareness);

            var hints = ShadowHintComputer.ComputeShadowHints(option, ctx);

            Assert.DoesNotContain(hints, h => h.Contains("Overthinking +1"));
        }

        [Fact]
        public void Rizz_NearCumulativeThreshold_ShowsDespairWarning()
        {
            // 2 failures so far, next fail would be 3rd (trigger)
            var ctx = MakeContext(rizzFailures: 2);
            var option = MakeOption(StatType.Rizz);

            var hints = ShadowHintComputer.ComputeShadowHints(option, ctx);

            Assert.Contains(hints, h => h.Contains("Despair +1 (on fail)"));
        }

        [Fact]
        public void Rizz_NotNearThreshold_NoDespairWarning()
        {
            var ctx = MakeContext(rizzFailures: 0);
            var option = MakeOption(StatType.Rizz);

            var hints = ShadowHintComputer.ComputeShadowHints(option, ctx);

            Assert.DoesNotContain(hints, h => h.Contains("Despair +1"));
        }

        // ── Reduction opportunities ──────────────────────────────

        [Fact]
        public void Honesty_HighInterest_ShowsDenialReduction()
        {
            var ctx = MakeContext(interest: 15);
            var option = MakeOption(StatType.Honesty);

            var hints = ShadowHintComputer.ComputeShadowHints(option, ctx);

            Assert.Contains(hints, h => h.Contains("Denial -1 (on success)"));
        }

        [Fact]
        public void Honesty_LowInterest_NoDenialReduction()
        {
            var ctx = MakeContext(interest: 10);
            var option = MakeOption(StatType.Honesty);

            var hints = ShadowHintComputer.ComputeShadowHints(option, ctx);

            Assert.DoesNotContain(hints, h => h.Contains("Denial -1"));
        }

        [Fact]
        public void SA_VeryHighInterest_ShowsDespairReduction()
        {
            var ctx = MakeContext(interest: 19);
            var option = MakeOption(StatType.SelfAwareness);

            var hints = ShadowHintComputer.ComputeShadowHints(option, ctx);

            Assert.Contains(hints, h => h.Contains("Despair -1 (on success)"));
        }

        [Fact]
        public void Honesty_VeryHighInterest_ShowsDespairReduction()
        {
            var ctx = MakeContext(interest: 19);
            var option = MakeOption(StatType.Honesty);

            var hints = ShadowHintComputer.ComputeShadowHints(option, ctx);

            Assert.Contains(hints, h => h.Contains("Despair -1 (on success)"));
        }

        [Fact]
        public void SA_Interest18_NoDespairReduction()
        {
            // >18 required, not >=18
            var ctx = MakeContext(interest: 18);
            var option = MakeOption(StatType.SelfAwareness);

            var hints = ShadowHintComputer.ComputeShadowHints(option, ctx);

            Assert.DoesNotContain(hints, h => h.Contains("Despair -1"));
        }

        [Fact]
        public void Chaos_WithCombo_ShowsFixationReduction()
        {
            var ctx = MakeContext();
            var option = MakeOption(StatType.Chaos, comboName: "The Pivot");

            var hints = ShadowHintComputer.ComputeShadowHints(option, ctx);

            Assert.Contains(hints, h => h.Contains("Fixation -1 (on combo)"));
        }

        [Fact]
        public void Chaos_NoCombo_NoFixationReduction()
        {
            var ctx = MakeContext();
            var option = MakeOption(StatType.Chaos);

            var hints = ShadowHintComputer.ComputeShadowHints(option, ctx);

            Assert.DoesNotContain(hints, h => h.Contains("Fixation -1"));
        }

        [Fact]
        public void HighDC_ShowsDreadReduction()
        {
            // Create stats where the option's need is ≥16
            // Player has +0 for Honesty, opponent has high defence
            var playerStats = MakeStats(charm: 0, rizz: 0, honesty: 0, chaos: 0, wit: 0, sa: 0);
            var opponentStats = MakeStats(charm: 0, rizz: 0, honesty: 0, chaos: 0, wit: 0, sa: 0);
            // DC = 16 + opponent defending stat. For Honesty, defence is SA.
            // DC = 16 + 0 = 16, need = 16 - (0 + 0) = 16
            var ctx = MakeContext(playerStats: playerStats, opponentStats: opponentStats, levelBonus: 0);
            var option = MakeOption(StatType.Honesty);

            var hints = ShadowHintComputer.ComputeShadowHints(option, ctx);

            Assert.Contains(hints, h => h.Contains("Dread -1 (nat 20)"));
        }

        [Fact]
        public void LowDC_NoDreadReduction()
        {
            // Player has high stats, easy roll
            var playerStats = MakeStats(charm: 5, rizz: 5, honesty: 5, chaos: 5, wit: 5, sa: 5);
            var opponentStats = MakeStats(charm: 0, rizz: 0, honesty: 0, chaos: 0, wit: 0, sa: 0);
            var ctx = MakeContext(playerStats: playerStats, opponentStats: opponentStats, levelBonus: 2);
            var option = MakeOption(StatType.Charm);

            var hints = ShadowHintComputer.ComputeShadowHints(option, ctx);

            Assert.DoesNotContain(hints, h => h.Contains("Dread -1"));
        }

        // ── FormatHints ──────────────────────────────────────────

        [Fact]
        public void FormatHints_Empty_ReturnsEmpty()
        {
            Assert.Equal("", ShadowHintComputer.FormatHints(new List<string>()));
        }

        [Fact]
        public void FormatHints_Multiple_JoinsWithComma()
        {
            var hints = new List<string> { "\u26a0\ufe0f Fixation +1", "\u2728 Denial -1 (on success)" };
            var result = ShadowHintComputer.FormatHints(hints);
            Assert.Contains("Fixation +1", result);
            Assert.Contains("Denial -1", result);
            Assert.Contains(", ", result);
        }

        [Fact]
        public void HighestPct3rdTurn_ShowsFixationSafePickWarning()
        {
            // Two previous turns where highest-% was picked
            var highestPctHistory = new List<bool> { true, true };
            // Create options where Charm is the highest-% option
            var playerStats = MakeStats(charm: 5, rizz: 0, honesty: 0, chaos: 0, wit: 0, sa: 0);
            var opponentStats = MakeStats(charm: 0, rizz: 0, honesty: 0, chaos: 0, wit: 0, sa: 0);
            var options = new[]
            {
                MakeOption(StatType.Charm),
                MakeOption(StatType.Rizz),
                MakeOption(StatType.Honesty)
            };
            var ctx = MakeContext(
                highestPctHistory: highestPctHistory,
                playerStats: playerStats,
                opponentStats: opponentStats,
                options: options);

            var hints = ShadowHintComputer.ComputeShadowHints(options[0], ctx);

            Assert.Contains(hints, h => h.Contains("Fixation +1 (safe pick)"));
        }

        [Fact]
        public void HighestPct_NotConsecutive_NoSafePickWarning()
        {
            var highestPctHistory = new List<bool> { true, false };
            var playerStats = MakeStats(charm: 5, rizz: 0, honesty: 0, chaos: 0, wit: 0, sa: 0);
            var opponentStats = MakeStats(charm: 0, rizz: 0, honesty: 0, chaos: 0, wit: 0, sa: 0);
            var options = new[]
            {
                MakeOption(StatType.Charm),
                MakeOption(StatType.Rizz)
            };
            var ctx = MakeContext(
                highestPctHistory: highestPctHistory,
                playerStats: playerStats,
                opponentStats: opponentStats,
                options: options);

            var hints = ShadowHintComputer.ComputeShadowHints(options[0], ctx);

            Assert.DoesNotContain(hints, h => h.Contains("safe pick"));
        }

        [Fact]
        public void NullOption_Throws()
        {
            var ctx = MakeContext();
            Assert.Throws<System.ArgumentNullException>(() => ShadowHintComputer.ComputeShadowHints(null, ctx));
        }

        [Fact]
        public void NullContext_Throws()
        {
            var option = MakeOption(StatType.Charm);
            Assert.Throws<System.ArgumentNullException>(() => ShadowHintComputer.ComputeShadowHints(option, null));
        }

        [Fact]
        public void Honesty_VeryHighInterest_ShowsBothDenialAndDespairReduction()
        {
            var ctx = MakeContext(interest: 19);
            var option = MakeOption(StatType.Honesty);

            var hints = ShadowHintComputer.ComputeShadowHints(option, ctx);

            Assert.Contains(hints, h => h.Contains("Denial -1 (on success)"));
            Assert.Contains(hints, h => h.Contains("Despair -1 (on success)"));
        }
    }
}
