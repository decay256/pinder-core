using System;
using System.Collections.Generic;
using System.Linq;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Handles T3 shadow option filtering and random stat drawing for dialogue options.
    /// Stateless — all state is passed as parameters.
    /// </summary>
    internal static class OptionFilterEngine
    {
        /// <summary>
        /// Draws N random stats from the pool, applying shadow-based exclusions.
        /// Denial T3 (≥12): removes HONESTY from pool.
        ///
        /// <para>
        /// <paramref name="rng"/> is used to shuffle the eligible pool. When null a
        /// fresh <see cref="Random"/> is created (legacy non-deterministic behaviour).
        /// Pass an injected/seeded Random — typically a dedicated stat-draw RNG owned
        /// by the GameSession — to make stat draws reproducible across capture/replay
        /// runs (see issue #130).
        /// </para>
        /// </summary>
        public static StatType[] DrawRandomStats(
            StatType[] pool,
            int count,
            Dictionary<ShadowStatType, int>? shadowThresholds,
            Random? rng = null)
        {
            var eligible = new List<StatType>(pool);

            // Denial T3 (≥12): remove HONESTY from pool
            if (shadowThresholds != null
                && shadowThresholds.TryGetValue(ShadowStatType.Denial, out int denVal)
                && denVal >= 12)
            {
                eligible.Remove(StatType.Honesty);
            }

            // Shuffle using the provided RNG (or a fresh non-deterministic one if none
            // was supplied). Stat selection is UI randomness, not game mechanics, but
            // tests inject a seeded RNG so prompt fixtures stay stable (#130).
            var shuffler = rng ?? new Random();
            for (int i = eligible.Count - 1; i > 0; i--)
            {
                int j = shuffler.Next(0, i + 1);
                var tmp = eligible[i];
                eligible[i] = eligible[j];
                eligible[j] = tmp;
            }

            var drawn = new List<StatType>();
            for (int i = 0; i < Math.Min(count, eligible.Count); i++)
                drawn.Add(eligible[i]);

            return drawn.ToArray();
        }

        /// <summary>
        /// Applies T3 shadow option filtering rules:
        /// - Fixation T3 (≥18): force all options to last stat used
        /// - Denial T3 (≥18): remove Honesty options
        /// - Madness T3 (≥18): replace one random option with unhinged marker
        /// </summary>
        public static DialogueOption[] ApplyT3Filters(
            DialogueOption[] options,
            Dictionary<ShadowStatType, int> shadowThresholds,
            StatType? lastStatUsed,
            IDiceRoller dice)
        {
            // Fixation T3: force all options to use the same stat as last turn
            if (shadowThresholds.TryGetValue(ShadowStatType.Fixation, out int fixRaw)
                && fixRaw >= 18 && lastStatUsed.HasValue)
            {
                var forcedStat = lastStatUsed.Value;
                for (int i = 0; i < options.Length; i++)
                {
                    var o = options[i];
                    options[i] = new DialogueOption(
                        forcedStat, o.IntendedText, o.CallbackTurnNumber,
                        o.ComboName, o.HasTellBonus, o.HasWeaknessWindow);
                }
            }

            // Denial T3: remove Honesty options
            if (shadowThresholds.TryGetValue(ShadowStatType.Denial, out int denRaw)
                && denRaw >= 18)
            {
                var filtered = options.Where(o => o.Stat != StatType.Honesty).ToArray();
                if (filtered.Length == 0)
                {
                    var chaos = options.FirstOrDefault(o => o.Stat == StatType.Chaos);
                    filtered = new[] { chaos ?? options[0] };
                }
                options = filtered;
            }

            // Madness T3: replace one random option with unhinged replacement marker
            if (shadowThresholds.TryGetValue(ShadowStatType.Madness, out int madRaw)
                && madRaw >= 18
                && options.Length > 0)
            {
                int unhingedIdx = dice.Roll(options.Length) - 1;
                var o = options[unhingedIdx];
                options[unhingedIdx] = new DialogueOption(
                    o.Stat, o.IntendedText, o.CallbackTurnNumber,
                    o.ComboName, o.HasTellBonus, o.HasWeaknessWindow,
                    isUnhingedReplacement: true);
            }

            return options;
        }
    }
}
