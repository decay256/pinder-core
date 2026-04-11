using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;

namespace Pinder.Core.Stats
{
    /// <summary>
    /// Input state for computing shadow hints on dialogue options.
    /// Populated by the session runner from tracked game state.
    /// </summary>
    public sealed class ShadowHintContext
    {
        /// <summary>Ordered list of stats used in previous turns (oldest first).</summary>
        public IReadOnlyList<StatType> StatsUsedHistory { get; set; }

        /// <summary>Ordered list of whether the highest-% option was picked each turn.</summary>
        public IReadOnlyList<bool> HighestPctHistory { get; set; }

        /// <summary>Current interest meter value.</summary>
        public int CurrentInterest { get; set; }

        /// <summary>Number of times CHARM has been used this session (before this turn).</summary>
        public int CharmUsageCount { get; set; }

        /// <summary>Whether the CHARM 3x Madness trigger has already fired.</summary>
        public bool CharmMadnessTriggered { get; set; }

        /// <summary>Number of times SA has been used this session (before this turn).</summary>
        public int SaUsageCount { get; set; }

        /// <summary>Whether the SA 3x Overthinking trigger has already fired.</summary>
        public bool SaOverthinkingTriggered { get; set; }

        /// <summary>Number of cumulative RIZZ failures this session.</summary>
        public int RizzCumulativeFailureCount { get; set; }

        /// <summary>All options available this turn (needed for highest-% detection).</summary>
        public DialogueOption[] CurrentOptions { get; set; }

        /// <summary>Player stat block for computing margins.</summary>
        public StatBlock PlayerStats { get; set; }

        /// <summary>Opponent stat block for computing DCs.</summary>
        public StatBlock OpponentStats { get; set; }

        /// <summary>Player level bonus.</summary>
        public int PlayerLevelBonus { get; set; }

        /// <summary>Whether HONESTY is available as an option this turn.</summary>
        public bool HonestyAvailable { get; set; }

        public ShadowHintContext()
        {
            StatsUsedHistory = Array.Empty<StatType>();
            HighestPctHistory = Array.Empty<bool>();
            CurrentOptions = Array.Empty<DialogueOption>();
        }
    }

    /// <summary>
    /// Pure computation of shadow growth warnings and reduction hints for dialogue option display.
    /// No side effects — reads state, returns formatted hint strings.
    /// </summary>
    public static class ShadowHintComputer
    {
        /// <summary>
        /// Compute shadow hint badges for a single dialogue option.
        /// Returns a list of formatted hint strings (e.g. "⚠️ Madness +1", "✨ Denial -1 (on success)").
        /// </summary>
        public static List<string> ComputeShadowHints(DialogueOption option, ShadowHintContext ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (option == null) throw new ArgumentNullException(nameof(option));

            var hints = new List<string>();

            // ── Growth warnings ──────────────────────────────────────

            // 1. Same stat 3rd turn in a row → Fixation +1
            if (ctx.StatsUsedHistory.Count >= 2)
            {
                int tail = ctx.StatsUsedHistory.Count;
                if (ctx.StatsUsedHistory[tail - 1] == option.Stat
                    && ctx.StatsUsedHistory[tail - 2] == option.Stat)
                {
                    hints.Add("\u26a0\ufe0f Fixation +1");
                }
            }

            // 2. Highest-% option 3rd turn in a row → Fixation +1
            if (ctx.HighestPctHistory.Count >= 2
                && ctx.HighestPctHistory[ctx.HighestPctHistory.Count - 1]
                && ctx.HighestPctHistory[ctx.HighestPctHistory.Count - 2]
                && IsHighestProbabilityOption(option, ctx))
            {
                hints.Add("\u26a0\ufe0f Fixation +1 (safe pick)");
            }

            // 3. CHARM 3rd use → Madness +1 (only if not already triggered)
            if (option.Stat == StatType.Charm
                && ctx.CharmUsageCount == 2
                && !ctx.CharmMadnessTriggered)
            {
                hints.Add("\u26a0\ufe0f Madness +1");
            }

            // 4. SA 3rd use → Overthinking +1 (only if not already triggered)
            if (option.Stat == StatType.SelfAwareness
                && ctx.SaUsageCount == 2
                && !ctx.SaOverthinkingTriggered)
            {
                hints.Add("\u26a0\ufe0f Overthinking +1");
            }

            // 5. RIZZ option → Despair warning if cumulative failures near threshold
            if (option.Stat == StatType.Rizz)
            {
                int failsNeeded = 3 - (ctx.RizzCumulativeFailureCount % 3);
                if (failsNeeded == 1)
                {
                    hints.Add("\u26a0\ufe0f Despair +1 (on fail)");
                }
            }

            // ── Reduction opportunities ──────────────────────────────

            // 6. HONESTY at interest ≥15 → Denial -1 (on success)
            if (option.Stat == StatType.Honesty && ctx.CurrentInterest >= 15)
            {
                hints.Add("\u2728 Denial -1 (on success)");
            }

            // 7. SA or HONESTY at interest >18 → Despair -1 (on success)
            if ((option.Stat == StatType.SelfAwareness || option.Stat == StatType.Honesty)
                && ctx.CurrentInterest > 18)
            {
                hints.Add("\u2728 Despair -1 (on success)");
            }

            // 8. CHAOS with combo possible → Fixation -1 (on combo)
            if (option.Stat == StatType.Chaos && option.ComboName != null)
            {
                hints.Add("\u2728 Fixation -1 (on combo)");
            }

            // 9. Nat 20 always possible → Dread -1 hint (shown for all risky options)
            //    Only show for options where DC is high enough that nat 20 matters
            if (ctx.PlayerStats != null && ctx.OpponentStats != null)
            {
                int mod = ctx.PlayerStats.GetEffective(option.Stat);
                int dc = ctx.OpponentStats.GetDefenceDC(option.Stat);
                int need = dc - (mod + ctx.PlayerLevelBonus);
                if (need >= 16)
                {
                    hints.Add("\u2728 Dread -1 (nat 20)");
                }
            }

            return hints;
        }

        /// <summary>
        /// Format hints as a single display string for appending to option lines.
        /// Returns empty string if no hints.
        /// </summary>
        public static string FormatHints(List<string> hints)
        {
            if (hints == null || hints.Count == 0)
                return "";
            return string.Join(", ", hints);
        }

        private static bool IsHighestProbabilityOption(DialogueOption option, ShadowHintContext ctx)
        {
            if (ctx.PlayerStats == null || ctx.OpponentStats == null || ctx.CurrentOptions == null)
                return false;

            int optionMargin = ctx.PlayerStats.GetEffective(option.Stat) + ctx.PlayerLevelBonus
                               - ctx.OpponentStats.GetDefenceDC(option.Stat);

            for (int i = 0; i < ctx.CurrentOptions.Length; i++)
            {
                int margin = ctx.PlayerStats.GetEffective(ctx.CurrentOptions[i].Stat) + ctx.PlayerLevelBonus
                             - ctx.OpponentStats.GetDefenceDC(ctx.CurrentOptions[i].Stat);
                if (margin > optionMargin)
                    return false;
            }
            return true;
        }
    }
}
