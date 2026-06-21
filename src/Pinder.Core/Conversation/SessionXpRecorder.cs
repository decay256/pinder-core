using System;
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

        /// <summary>
        /// Records XP for a roll result following §10 precedence rules:
        /// Nat 20 → 25 XP (overrides DC-tier), Nat 1 → 10 XP (overrides failure),
        /// success → 5/10/15 by DC tier, failure → 2.
        /// </summary>
        public void RecordRollXp(RollResult rollResult)
        {
            if (rollResult.IsNatTwenty)
            {
                int award = _rules?.GetFlatXpAward("Nat20") ?? 25;
                _xpLedger.Record("Nat20", award);
            }
            else if (rollResult.IsNatOne)
            {
                int award = _rules?.GetFlatXpAward("Nat1") ?? 10;
                _xpLedger.Record("Nat1", award);
            }
            else if (rollResult.IsSuccess)
            {
                int baseXp;
                int? configBaseXp = _rules?.GetSuccessBaseXp(rollResult.DC);
                if (configBaseXp.HasValue)
                {
                    baseXp = configBaseXp.Value;
                }
                else
                {
                    if (rollResult.DC <= 16)
                        baseXp = 5;
                    else if (rollResult.DC <= 20)
                        baseXp = 10;
                    else
                        baseXp = 15;
                }

                int xp = ApplyRiskTierMultiplier(baseXp, rollResult.RiskTier);

                string label = rollResult.DC <= 16 ? "Success_DC_Low"
                    : rollResult.DC <= 20 ? "Success_DC_Mid"
                    : "Success_DC_High";
                _xpLedger.Record(label, xp);
            }
            else
            {
                int award = _rules?.GetFlatXpAward("Failure") ?? 2;
                _xpLedger.Record("Failure", award);
            }
        }

        /// <summary>
        /// Applies the risk-tier XP multiplier per risk-reward doc:
        /// Safe=1x, Medium=1.5x, Hard=2x, Bold=3x.
        /// </summary>
        public int ApplyRiskTierMultiplier(int baseXp, RiskTier riskTier)
        {
            if (_rules != null)
            {
                var resolved = _rules.GetRiskTierXpMultiplier(riskTier);
                if (resolved.HasValue)
                    return (int)Math.Round(baseXp * resolved.Value);
            }

            double multiplier;
            if (riskTier == RiskTier.Bold)
                multiplier = 3.0;
            else if (riskTier == RiskTier.Hard)
                multiplier = 2.0;
            else if (riskTier == RiskTier.Medium)
                multiplier = 1.5;
            else if (riskTier == RiskTier.Reckless)
                multiplier = 10.0;
            else
                multiplier = 1.0;

            return (int)Math.Round(baseXp * multiplier);
        }

        /// <summary>
        /// Records end-of-game XP based on the game outcome.
        /// Uses terminal outcome multipliers.
        /// </summary>
        public void RecordEndOfGameXp(GameOutcome outcome)
        {
            double multiplier = _rules?.GetTerminalOutcomeMultiplier(outcome) ?? (outcome == GameOutcome.DateSecured ? 3.0 : 1.0);
            int collected = _xpLedger.TotalXp;
            int delta = (int)Math.Round(collected * multiplier) - collected;
            if (delta > 0)
                _xpLedger.Record($"OutcomeBonus_{outcome}", delta);
        }
    }
}
