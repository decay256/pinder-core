using System;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;

namespace Pinder.Core.I18n
{
    /// <summary>
    /// Stable key builders for <see cref="IConsequenceCatalog"/> lookups.
    /// The key format is locked by <c>data/i18n/en/consequences.yaml</c>:
    /// <c>consequence.&lt;kind&gt;.&lt;outcome&gt;[.&lt;detail&gt;]</c>.
    /// </summary>
    /// <remarks>
    /// Sprint #976. Do not introduce new key-computation paths outside this class
    /// — if the yaml key scheme changes, this is the single point of update.
    /// </remarks>
    public static class ConsequenceKeys
    {
        /// <summary>
        /// <c>consequence.roll.pass</c> or
        /// <c>consequence.roll.miss.&lt;tier&gt;</c>.
        /// </summary>
        public static string ForRoll(bool isSuccess, FailureTier tier)
        {
            if (isSuccess)
                return "consequence.roll.pass";
            return $"consequence.roll.miss.{tier.ToString().ToLowerInvariant()}";
        }

        /// <summary>
        /// <c>consequence.shadow.miss.&lt;shadow-lowercase&gt;</c>.
        /// Only called when <c>IsMiss</c> is true.
        /// </summary>
        public static string ForShadowMiss(ShadowStatType shadow)
        {
            return $"consequence.shadow.miss.{shadow.ToString().ToLowerInvariant()}";
        }

        /// <summary>
        /// <c>consequence.horniness.miss.&lt;tier-lowercase&gt;</c>.
        /// Only called when <c>IsMiss</c> is true.
        /// </summary>
        public static string ForHorninessMiss(FailureTier tier)
        {
            return $"consequence.horniness.miss.{tier.ToString().ToLowerInvariant()}";
        }

        /// <summary>
        /// Apply slot substitutions to a consequence template.
        /// Supported slots: <c>{stat}</c>.
        /// <c>{trap_name}</c> is reserved for future use (#976 scope does
        /// not include trap-consequence population).
        /// </summary>
        public static string ApplySlots(string template, string? statName = null)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (statName != null)
                template = template.Replace("{stat}", statName);
            return template;
        }
    }
}
