using System;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Progression;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.Core.Tests
{
    [Trait("Category", "Core")]
    public class Issue1326_1327_ProgressionSettlementTests
    {
        [Fact]
        public void ExplicitGameDefinitionResolver_WorksWhenDefaultResolverIsNull()
        {
            var previous = DefaultRuleResolver.Instance;
            try
            {
                DefaultRuleResolver.Instance = null;
                var rules = GameDefinition.PinderDefaults;

                Assert.Equal(2, LevelTable.GetLevel(50, rules));
                Assert.Equal(10, rules.GetProgressionCurrencyPerXp());

                var ledger = new XpLedger();
                ledger.Record("Base", 10);
                var recorder = new SessionXpRecorder(ledger, rules);

                recorder.RecordEndOfGameXp(GameOutcome.DateSecured);

                Assert.Equal(30, ledger.TotalXp);
                Assert.Equal(GameOutcome.DateSecured, ledger.TerminalSettlementOutcome);
            }
            finally
            {
                DefaultRuleResolver.Instance = previous;
            }
        }

        [Theory]
        [InlineData(GameOutcome.DateSecured)]
        [InlineData(GameOutcome.Unmatched)]
        [InlineData(GameOutcome.Ghosted)]
        public void SettlementCalculator_UsesConfiguredTerminalOutcomesAndCurrency(GameOutcome outcome)
        {
            var settlement = ProgressionSettlementCalculator.CalculateTerminalSettlement(
                collectedXp: 12,
                outcome: outcome,
                rules: GameDefinition.PinderDefaults);

            Assert.Equal(outcome, settlement.Outcome);
            Assert.Equal(12, settlement.BaseXp);
            Assert.True(settlement.TotalXpAfterOutcome >= 12);
            Assert.Equal(10, settlement.CurrencyPerXp);
            Assert.Equal(settlement.TotalXpAfterOutcome * 10, settlement.CurrencyEarned);
        }

        [Fact]
        public void XpLedger_TerminalSettlement_IsExactlyOnceForSameOutcome()
        {
            var ledger = new XpLedger();
            ledger.Record("Base", 10);
            var recorder = new SessionXpRecorder(ledger, GameDefinition.PinderDefaults);

            recorder.RecordEndOfGameXp(GameOutcome.DateSecured);
            recorder.RecordEndOfGameXp(GameOutcome.DateSecured);

            Assert.Equal(30, ledger.TotalXp);
            Assert.Single(ledger.Events, e => e.Source == "OutcomeBonus_DateSecured");
        }

        [Fact]
        public void SessionXpRecorder_TerminalSettlement_RejectsGlobalDefaultRules()
        {
            var previous = DefaultRuleResolver.Instance;
            try
            {
                DefaultRuleResolver.Instance = GameDefinition.PinderDefaults;
                var ledger = new XpLedger();
                ledger.Record("Base", 10);
                var recorder = new SessionXpRecorder(ledger, rules: null);

                var ex = Assert.Throws<InvalidOperationException>(() =>
                    recorder.RecordEndOfGameXp(GameOutcome.DateSecured));

                Assert.Contains("explicit session rules", ex.Message, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(10, ledger.TotalXp);
            }
            finally
            {
                DefaultRuleResolver.Instance = previous;
            }
        }

        [Fact]
        public void XpLedger_TerminalSettlement_RejectsConflictingOutcome()
        {
            var ledger = new XpLedger();
            ledger.Record("Base", 10);
            var recorder = new SessionXpRecorder(ledger, GameDefinition.PinderDefaults);

            recorder.RecordEndOfGameXp(GameOutcome.DateSecured);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                recorder.RecordEndOfGameXp(GameOutcome.Unmatched));
            Assert.Contains("already settled", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void XpLedger_TerminalSettlement_MetadataSurvivesClone()
        {
            var ledger = new XpLedger();
            ledger.Record("Base", 10);
            var recorder = new SessionXpRecorder(ledger, GameDefinition.PinderDefaults);
            recorder.RecordEndOfGameXp(GameOutcome.DateSecured);

            var clone = ledger.Clone();
            var cloneRecorder = new SessionXpRecorder(clone, GameDefinition.PinderDefaults);
            cloneRecorder.RecordEndOfGameXp(GameOutcome.DateSecured);

            Assert.Equal(30, clone.TotalXp);
            Assert.Equal(GameOutcome.DateSecured, clone.TerminalSettlementOutcome);
            Assert.Single(clone.Events, e => e.Source == "OutcomeBonus_DateSecured");
        }

        [Fact]
        public void SettlementCalculator_MissingCurrencyRule_Throws()
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                ProgressionSettlementCalculator.CalculateTerminalSettlement(
                    10,
                    GameOutcome.DateSecured,
                    new MissingCurrencyResolver()));
            Assert.Contains("currency per XP", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        private sealed class MissingCurrencyResolver : IRuleResolver
        {
            public int? GetFailureInterestDelta(int missMargin, int naturalRoll) => null;
            public int? GetSuccessInterestDelta(int beatMargin, int naturalRoll) => null;
            public InterestState? GetInterestState(int interest) => null;
            public int? GetShadowThresholdLevel(int shadowValue) => null;
            public int? GetMomentumBonus(int streak) => null;
            public double? GetRiskTierXpMultiplier(Pinder.Core.Rolls.RiskTier riskTier) => null;
            public double? GetTerminalOutcomeMultiplier(GameOutcome outcome) => 1.0;
            public int? GetSuccessBaseXp(int dc) => null;
            public SuccessDcLabelThresholds? GetSuccessDcLabelThresholds() => null;
            public int? GetFlatXpAward(string awardType) => null;
            public int? GetXpThresholdForLevel(int level) => null;
            public int? GetLevelRollBonus(int level) => null;
            public int? GetBuildPointsForLevel(int level) => null;
            public int? GetItemSlotsForLevel(int level) => null;
            public int? GetFailurePoolTierMinLevel(string tierName) => null;
            public int? GetProgressionCurrencyPerXp() => null;
            public bool AllowDefaultFallback => false;
        }
    }
}
