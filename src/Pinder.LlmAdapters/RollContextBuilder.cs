using System;
using System.Collections.Generic;
using Pinder.Core.Rolls;

namespace Pinder.LlmAdapters
{
    /// <summary>
    /// Builds narrative roll context strings for [ENGINE — DELIVERY] blocks.
    /// Reads flavor text from enriched YAML rule entries when available,
    /// falling back to hardcoded defaults.
    /// </summary>
    public sealed class RollContextBuilder
    {
        private readonly Dictionary<string, string> _flavorById;

        // ── Hardcoded fallback narratives ──

        internal static readonly string FallbackCleanSuccess =
            "The message landed. Deliver it as intended.";

        internal static readonly string FallbackStrongSuccess =
            "The message landed well. Sharpen the phrasing — it hits harder than intended.";

        internal static readonly string FallbackCriticalSuccess =
            "The best version of this message. It lands perfectly.";

        internal static readonly string FallbackNat20 =
            "Legendary success. The best thing anyone has ever said on this app.";

        internal static readonly string FallbackFumble =
            "Miss by 1-2. Slight fumble — right words, flat delivery.";

        internal static readonly string FallbackMisfire =
            "Miss by 3-5. The message went sideways — intent guessable but execution off.";

        internal static readonly string FallbackTropeTrap =
            "Miss by 6-9. Trope trap activated — the smoothness cracked, trying too hard showed.";

        internal static readonly string FallbackCatastrophe =
            "Miss by 10+. Spectacular disaster — the worst version of this message came out.";

        internal static readonly string FallbackLegendary =
            "Nat 1. Maximum humiliation. Something that has never happened on this app before.";

        /// <summary>
        /// Creates a RollContextBuilder with no YAML data (uses only hardcoded fallbacks).
        /// </summary>
        public RollContextBuilder()
        {
            _flavorById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Creates a RollContextBuilder that extracts flavor text from enriched YAML entries.
        /// Entries are expected to have table rows with "What Happens To Your Message" or "What Happens" columns.
        /// Falls back to hardcoded defaults for any missing entries.
        /// </summary>
        /// <param name="yamlEntries">
        /// Parsed rule entries indexed by id. Expects ids like "§7.fail-tier.fumble", "§7.success-scale.1-4", etc.
        /// </param>
        public RollContextBuilder(IReadOnlyDictionary<string, string> yamlEntries)
        {
            _flavorById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (yamlEntries != null)
            {
                foreach (var kvp in yamlEntries)
                {
                    _flavorById[kvp.Key] = kvp.Value;
                }
            }
        }

        /// <summary>
        /// Creates a RollContextBuilder by extracting flavor text from a RuleBook.
        /// Looks for table rows in §7 failure/success entries with descriptive text.
        /// </summary>
        /// <param name="ruleBook">A loaded RuleBook from rules-v3-enriched.yaml.</param>
        /// <returns>A configured RollContextBuilder instance.</returns>
        public static RollContextBuilder FromRuleBook(Pinder.Rules.RuleBook ruleBook)
        {
            if (ruleBook == null) throw new ArgumentNullException(nameof(ruleBook));

            var flavors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Extract description text from enriched YAML entries
            var failureIds = new[]
            {
                "§7.fail-tier.fumble",
                "§7.fail-tier.misfire",
                "§7.fail-tier.trope-trap",
                "§7.fail-tier.catastrophe",
                "§7.fail-tier.legendary-fail"
            };

            var successIds = new[]
            {
                "§7.success-scale.1-4",
                "§7.success-scale.5-9",
                "§7.success-scale.10plus",
                "§7.success-scale.nat-20"
            };

            foreach (var id in failureIds)
            {
                var entry = ruleBook.GetById(id);
                if (entry != null && !string.IsNullOrEmpty(entry.Description))
                {
                    flavors[id] = entry.Description;
                }
            }

            foreach (var id in successIds)
            {
                var entry = ruleBook.GetById(id);
                if (entry != null && !string.IsNullOrEmpty(entry.Description))
                {
                    flavors[id] = entry.Description;
                }
            }

            return new RollContextBuilder(flavors);
        }

        /// <summary>
        /// Gets the narrative roll context string for a successful roll.
        /// </summary>
        /// <param name="beatDcBy">How much the roll beat the DC by (positive).</param>
        /// <param name="isNat20">Whether the roll was a natural 20.</param>
        /// <returns>A narrative string describing how the message should land.</returns>
        public string GetSuccessContext(int beatDcBy, bool isNat20)
        {
            if (isNat20)
            {
                return _flavorById.TryGetValue("§7.success-scale.nat-20", out var nat20)
                    ? nat20 : FallbackNat20;
            }

            if (beatDcBy >= 10)
            {
                return _flavorById.TryGetValue("§7.success-scale.10plus", out var crit)
                    ? crit : FallbackCriticalSuccess;
            }

            if (beatDcBy >= 5)
            {
                return _flavorById.TryGetValue("§7.success-scale.5-9", out var strong)
                    ? strong : FallbackStrongSuccess;
            }

            return _flavorById.TryGetValue("§7.success-scale.1-4", out var clean)
                ? clean : FallbackCleanSuccess;
        }

        /// <summary>
        /// Gets the narrative roll context string for a failed roll.
        /// </summary>
        /// <param name="tier">The failure tier from the roll result.</param>
        /// <returns>A narrative string describing what went wrong.</returns>
        public string GetFailureContext(FailureTier tier)
        {
            switch (tier)
            {
                case FailureTier.Fumble:
                    return _flavorById.TryGetValue("§7.fail-tier.fumble", out var fumble)
                        ? fumble : FallbackFumble;
                case FailureTier.Misfire:
                    return _flavorById.TryGetValue("§7.fail-tier.misfire", out var misfire)
                        ? misfire : FallbackMisfire;
                case FailureTier.TropeTrap:
                    return _flavorById.TryGetValue("§7.fail-tier.trope-trap", out var trap)
                        ? trap : FallbackTropeTrap;
                case FailureTier.Catastrophe:
                    return _flavorById.TryGetValue("§7.fail-tier.catastrophe", out var cat)
                        ? cat : FallbackCatastrophe;
                case FailureTier.Legendary:
                    return _flavorById.TryGetValue("§7.fail-tier.legendary-fail", out var leg)
                        ? leg : FallbackLegendary;
                default:
                    return "A failure has occurred.";
            }
        }
    }
}
