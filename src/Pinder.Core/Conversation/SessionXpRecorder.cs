using System;
using System.Collections.Generic;
using Pinder.Core.Interfaces;
using Pinder.Core.Progression;
using Pinder.Core.Rolls;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Records XP events during a game session.
    /// Owns the XP ledger and applies risk-tier multipliers.
    /// </summary>
    internal sealed class SessionXpRecorder
    {
        private readonly XpLedger _xpLedger;
        private readonly IRuleResolver? _rules;

        public SessionXpRecorder(XpLedger xpLedger, IRuleResolver? rules)
        {
            _xpLedger = xpLedger ?? throw new ArgumentNullException(nameof(xpLedger));
            _rules = rules;
        }

        private IRuleResolver GetRules()
        {
            return _rules ?? DefaultRuleResolver.Instance ?? throw new InvalidOperationException("Default rule resolver is not registered.");
        }

        private int GetFlatXpAwardVal(string awardType)
        {
            var rules = GetRules();
            var val = rules.GetFlatXpAward(awardType);
            if (val.HasValue) return val.Value;

            if (rules != DefaultRuleResolver.Instance && DefaultRuleResolver.Instance != null && rules.AllowDefaultFallback)
            {
                var oDefault = DefaultRuleResolver.Instance.GetFlatXpAward(awardType);
                if (oDefault.HasValue) return oDefault.Value;
            }

            throw new InvalidOperationException($"Flat award '{awardType}' is missing in configuration.");
        }

        private double GetRiskTierMultiplierVal(RiskTier riskTier)
        {
            var rules = GetRules();
            var val = rules.GetRiskTierXpMultiplier(riskTier);
            if (val.HasValue) return val.Value;

            if (rules != DefaultRuleResolver.Instance && DefaultRuleResolver.Instance != null && rules.AllowDefaultFallback)
            {
                var oDefault = DefaultRuleResolver.Instance.GetRiskTierXpMultiplier(riskTier);
                if (oDefault.HasValue) return oDefault.Value;
            }

            throw new InvalidOperationException($"Risk tier multiplier for {riskTier} is missing in configuration.");
        }

        private double GetTerminalOutcomeMultiplierVal(GameOutcome outcome)
        {
            var rules = GetRules();
            var val = rules.GetTerminalOutcomeMultiplier(outcome);
            if (val.HasValue) return val.Value;

            if (rules != DefaultRuleResolver.Instance && DefaultRuleResolver.Instance != null && rules.AllowDefaultFallback)
            {
                var oDefault = DefaultRuleResolver.Instance.GetTerminalOutcomeMultiplier(outcome);
                if (oDefault.HasValue) return oDefault.Value;
            }

            throw new InvalidOperationException($"Terminal outcome multiplier for {outcome} is missing in configuration.");
        }

        private int GetSuccessBaseXpVal(int dc)
        {
            var rules = GetRules();
            var val = rules.GetSuccessBaseXp(dc);
            if (val.HasValue) return val.Value;

            if (rules != DefaultRuleResolver.Instance && DefaultRuleResolver.Instance != null && rules.AllowDefaultFallback)
            {
                var oDefault = DefaultRuleResolver.Instance.GetSuccessBaseXp(dc);
                if (oDefault.HasValue) return oDefault.Value;
            }

            throw new InvalidOperationException($"Success base XP for DC {dc} is missing in configuration.");
        }

        private SuccessDcLabelThresholds GetSuccessDcLabelThresholdsVal()
        {
            var rules = GetRules();
            var val = rules.GetSuccessDcLabelThresholds();
            if (val.HasValue) return val.Value;

            if (rules != DefaultRuleResolver.Instance && DefaultRuleResolver.Instance != null && rules.AllowDefaultFallback)
            {
                var oDefault = DefaultRuleResolver.Instance.GetSuccessDcLabelThresholds();
                if (oDefault.HasValue) return oDefault.Value;
            }

            throw new InvalidOperationException("Success DC label thresholds are missing in configuration.");
        }

        /// <summary>
        /// Records XP for a roll result following §10 precedence rules:
        /// Nat 20 → 25 XP (overrides DC-tier), Nat 1 → 10 XP (overrides failure),
        /// success → 5/10/15 by DC tier, failure → 2.
        /// </summary>
        public void RecordRollXp(RollResult rollResult)
        {
            if (rollResult.IsNatTwenty)
            {
                int award = GetFlatXpAwardVal("Nat20");
                _xpLedger.Record("Nat20", award);
            }
            else if (rollResult.IsNatOne)
            {
                int award = GetFlatXpAwardVal("Nat1");
                _xpLedger.Record("Nat1", award);
            }
            else if (rollResult.IsSuccess)
            {
                int baseXp = GetSuccessBaseXpVal(rollResult.DC);
                int xp = ApplyRiskTierMultiplier(baseXp, rollResult.RiskTier);
                var thresholds = GetSuccessDcLabelThresholdsVal();

                string label = rollResult.DC <= thresholds.LowMax ? "Success_DC_Low"
                    : rollResult.DC <= thresholds.MidMax ? "Success_DC_Mid"
                    : "Success_DC_High";
                _xpLedger.Record(label, xp);
            }
            else
            {
                int award = GetFlatXpAwardVal("Failure");
                _xpLedger.Record("Failure", award);
            }
        }

        /// <summary>
        /// Applies the risk-tier XP multiplier per risk-reward doc:
        /// Safe=1x, Medium=1.5x, Hard=2x, Bold=3x.
        /// </summary>
        public int ApplyRiskTierMultiplier(int baseXp, RiskTier riskTier)
        {
            double resolved = GetRiskTierMultiplierVal(riskTier);
            return (int)Math.Round(baseXp * resolved);
        }

        /// <summary>
        /// Records end-of-game XP based on the game outcome.
        /// Uses terminal outcome multipliers.
        /// </summary>
        public void RecordEndOfGameXp(GameOutcome outcome)
        {
            double multiplier = GetTerminalOutcomeMultiplierVal(outcome);
            int collected = _xpLedger.TotalXp;
            int delta = (int)Math.Round(collected * multiplier) - collected;
            if (delta > 0)
                _xpLedger.Record($"OutcomeBonus_{outcome}", delta);
        }
    }
}
