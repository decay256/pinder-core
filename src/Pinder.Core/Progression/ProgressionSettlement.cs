using System;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;

namespace Pinder.Core.Progression
{
    /// <summary>
    /// Immutable result of settling a terminal game outcome against accumulated session XP.
    /// </summary>
    public sealed class ProgressionSettlement
    {
        public GameOutcome Outcome { get; }
        public int BaseXp { get; }
        public double OutcomeMultiplier { get; }
        public int BonusXp { get; }
        public int TotalXpAfterOutcome { get; }
        public int CurrencyPerXp { get; }
        public int CurrencyEarned { get; }

        public ProgressionSettlement(
            GameOutcome outcome,
            int baseXp,
            double outcomeMultiplier,
            int bonusXp,
            int totalXpAfterOutcome,
            int currencyPerXp,
            int currencyEarned)
        {
            if (baseXp < 0) throw new ArgumentOutOfRangeException(nameof(baseXp));
            if (bonusXp < 0) throw new ArgumentOutOfRangeException(nameof(bonusXp));
            if (totalXpAfterOutcome < baseXp) throw new ArgumentOutOfRangeException(nameof(totalXpAfterOutcome));
            if (currencyPerXp < 0) throw new ArgumentOutOfRangeException(nameof(currencyPerXp));
            if (currencyEarned < 0) throw new ArgumentOutOfRangeException(nameof(currencyEarned));
            if (double.IsNaN(outcomeMultiplier) || double.IsInfinity(outcomeMultiplier) || outcomeMultiplier < 0)
                throw new ArgumentOutOfRangeException(nameof(outcomeMultiplier));

            Outcome = outcome;
            BaseXp = baseXp;
            OutcomeMultiplier = outcomeMultiplier;
            BonusXp = bonusXp;
            TotalXpAfterOutcome = totalXpAfterOutcome;
            CurrencyPerXp = currencyPerXp;
            CurrencyEarned = currencyEarned;
        }
    }

    /// <summary>
    /// Pure progression settlement math shared by hosts. Hosts persist the returned
    /// XP and currency deltas atomically with their user/session records.
    /// </summary>
    public static class ProgressionSettlementCalculator
    {
        public static ProgressionSettlement CalculateTerminalSettlement(
            int collectedXp,
            GameOutcome outcome,
            IRuleResolver rules)
        {
            if (collectedXp < 0) throw new ArgumentOutOfRangeException(nameof(collectedXp));
            if (rules == null) throw new ArgumentNullException(nameof(rules));

            var multiplier = rules.GetTerminalOutcomeMultiplier(outcome);
            if (!multiplier.HasValue)
                throw new InvalidOperationException($"Terminal outcome multiplier for {outcome} is missing in configuration.");
            if (double.IsNaN(multiplier.Value) || double.IsInfinity(multiplier.Value) || multiplier.Value < 0)
                throw new InvalidOperationException($"Terminal outcome multiplier for {outcome} must be a non-negative finite number.");

            var currencyPerXp = rules.GetProgressionCurrencyPerXp();
            if (!currencyPerXp.HasValue)
                throw new InvalidOperationException("Progression currency per XP is missing in configuration.");
            if (currencyPerXp.Value < 0)
                throw new InvalidOperationException("Progression currency per XP must be non-negative.");

            int targetTotal = (int)Math.Round(collectedXp * multiplier.Value);
            int bonusXp = Math.Max(0, targetTotal - collectedXp);
            int totalXp = collectedXp + bonusXp;
            int currencyEarned = checked(totalXp * currencyPerXp.Value);

            return new ProgressionSettlement(
                outcome,
                collectedXp,
                multiplier.Value,
                bonusXp,
                totalXp,
                currencyPerXp.Value,
                currencyEarned);
        }
    }
}
