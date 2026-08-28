using System;
using System.Collections.Generic;
using System.Text;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;

namespace Pinder.LlmAdapters
{
    public static partial class SessionDocumentBuilder
    {
        public const int HorninessWarmthThreshold = 18;

        /// <summary>
        /// Computes the datee response length ceiling from the player's message length.
        /// Formula: ceiling = min(600, max(playerLen × 2, 80)).
        /// #866: reciprocal length budget — datee response shouldn't be wildly longer
        /// than the player's message.
        /// </summary>
        public static int ComputeResponseCeiling(int playerMessageLength)
        {
            return Math.Min(600, Math.Max(playerMessageLength * 2, 80));
        }

        /// <summary>
        /// Returns a stat-specific note on what success with that stat sounds and feels like.
        /// Used to guide the delivery LLM toward the right quality of improvement.
        /// </summary>
        private static string GetStatSuccessVoice(Pinder.Core.Stats.StatType stat)
        {
            switch (stat)
            {
                case Pinder.Core.Stats.StatType.Charm:
                    return "CHARM success: the warmth came through more genuinely than planned. The message feels more likeable, more disarming — less performed, more real.";
                case Pinder.Core.Stats.StatType.Rizz:
                    return "RIZZ success: the attraction landed. Something in the phrasing became more undeniably magnetic. The message has a pull to it now.";
                case Pinder.Core.Stats.StatType.Honesty:
                    return "HONESTY success: more vulnerability came through than intended — more specifically true, more unguarded. The message reveals something real.";
                case Pinder.Core.Stats.StatType.Chaos:
                    return "CHAOS success: the energy landed wilder and more alive than planned. The message is more surprising, more unexpected, more itself.";
                case Pinder.Core.Stats.StatType.Wit:
                    return "WIT success: the timing or sharpness clicked. The joke lands cleaner, the observation is more precise, the intelligence shows without trying.";
                case Pinder.Core.Stats.StatType.SelfAwareness:
                    return "SELF-AWARENESS success: the self-knowledge came through more clearly than planned — the character sees themselves more honestly and it shows in how they speak.";
                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// Returns a compact interest state label for the game state summary.
        /// </summary>
        private static string GetInterestLabel(int interest)
        {
            if (interest >= 21) return "Almost There \U0001f525";
            if (interest >= 16) return "Very Into It \U0001f60d (advantage)";
            if (interest >= 10) return "Interested \U0001f60a";
            if (interest >= 5) return "Lukewarm \U0001f914";
            if (interest >= 1) return "Bored \U0001f610 (disadvantage)";
            return "Unmatched \U0001f480";
        }

        internal static string GetInterestBeatThresholdPromptKey(
            int before,
            int after,
            InterestState newState)
        {
            if (newState == InterestState.Unmatched) return "interest-beat-unmatched";
            if (newState == InterestState.DateSecured) return "interest-beat-date-secured";
            if (after > before && after > 15 && before <= 15) return "interest-beat-above15";
            if (after < before && after < 8 && before >= 8) return "interest-beat-below8";
            return "interest-beat-generic";
        }

        private static string GetThresholdInstruction(
            int before,
            int after,
            InterestState newState,
            string dateeName,
            PromptCatalog? promptCatalog)
        {
            string key = GetInterestBeatThresholdPromptKey(before, after, newState);
            return PromptTemplates.GetCatalogString(promptCatalog, key)
                .Replace("{datee_name}", dateeName);
        }

        private static string BuildShadowTaintBlock(Dictionary<ShadowStatType, int>? thresholds)
        {
            if (thresholds == null || thresholds.Count == 0) return string.Empty;
            var sb = new StringBuilder();
            if (thresholds.TryGetValue(ShadowStatType.Madness, out int madness) && madness > 5)
                sb.AppendLine(PromptTemplates.ShadowTaintMadness);
            if (thresholds.TryGetValue(ShadowStatType.Despair, out int despair) && despair > 6)
                sb.AppendLine(PromptTemplates.ShadowTaintDespair);
            if (thresholds.TryGetValue(ShadowStatType.Denial, out int denial) && denial > 5)
                sb.AppendLine(PromptTemplates.ShadowTaintDenial);
            if (thresholds.TryGetValue(ShadowStatType.Fixation, out int fixation) && fixation > 5)
                sb.AppendLine(PromptTemplates.ShadowTaintFixation);
            if (thresholds.TryGetValue(ShadowStatType.Dread, out int dread) && dread > 5)
                sb.AppendLine(PromptTemplates.ShadowTaintDread);
            if (thresholds.TryGetValue(ShadowStatType.Overthinking, out int overthinking) && overthinking > 5)
                sb.AppendLine(PromptTemplates.ShadowTaintOverthinking);
            return sb.ToString().Trim();
        }

        private static string GetFailureTierName(FailureTier tier)
        {
            switch (tier)
            {
                case FailureTier.Fumble: return "FUMBLE";
                case FailureTier.Misfire: return "MISFIRE";
                case FailureTier.TropeTrap: return "TROPE_TRAP";
                case FailureTier.Catastrophe: return "CATASTROPHE";
                case FailureTier.Legendary: return "LEGENDARY";
                default: return "UNKNOWN";
            }
        }

        private static string GetTierInstruction(FailureTier tier)
        {
            switch (tier)
            {
                case FailureTier.Fumble:
                    return "Slight fumble. The intended message mostly gets through but with one awkward word choice, an unnecessary hedge, or a small detail that undermines it. Still readable.";
                case FailureTier.Misfire:
                    return "The message goes sideways. Key information gets garbled, tone shifts unexpectedly, or a strange tangent appears mid-sentence. The intent is still guessable but the execution is off.";
                case FailureTier.TropeTrap:
                    return "A stat-specific social trope failure activates. The message transforms into a recognisable bad-texting archetype (oversharing, going unhinged, being pretentious, spiraling, etc.). The trap is now active.";
                case FailureTier.Catastrophe:
                    return "Spectacular disaster. The intended message has been completely hijacked by the character's worst impulse. What comes out is the thing they would NEVER want to send. Still sounds like them — their disaster is their own.";
                case FailureTier.Legendary:
                    return "Maximum humiliation. The character's deepest embarrassing quality surfaces fully. This should be funny, specific, and feel earned by the build.";
                default:
                    return "A failure has occurred. Degrade the message accordingly.";
            }
        }

        /// <summary>
        /// Returns a resistance descriptor block based on current interest level.
        /// Below 25, the datee always maintains some form of resistance.
        /// </summary>
        internal static string GetResistanceBlock(
            int interest,
            InterestState interestState,
            PromptCatalog? promptCatalog = null)
        {
            string descriptor = PromptTemplates.GetCatalogString(
                promptCatalog,
                PromptTemplates.GetResistanceKey(interestState));

            return $"Current interest: {interest}/25. Resistance level: {descriptor}";
        }

        internal static string GetResistanceBlock(int interest)
        {
            return GetResistanceBlock(interest, new InterestMeter(interest).GetState());
        }

        /// <summary>
        /// Returns per-tier datee reaction guidance for failure degradation (#493).
        /// </summary>
        internal static string GetDateeReactionGuidance(
            FailureTier tier,
            PromptCatalog? promptCatalog = null)
        {
            switch (tier)
            {
                case FailureTier.Fumble: return PromptTemplates.GetCatalogString(promptCatalog, "datee-reaction-fumble");
                case FailureTier.Misfire: return PromptTemplates.GetCatalogString(promptCatalog, "datee-reaction-misfire");
                case FailureTier.TropeTrap: return PromptTemplates.GetCatalogString(promptCatalog, "datee-reaction-trope-trap");
                case FailureTier.Catastrophe: return PromptTemplates.GetCatalogString(promptCatalog, "datee-reaction-catastrophe");
                case FailureTier.Legendary: return PromptTemplates.GetCatalogString(promptCatalog, "datee-reaction-legendary");
                default: return string.Empty;
            }
        }

        private static string GetHorninessTierIntensity(
            Pinder.Core.Rolls.FailureTier tier,
            PromptCatalog? promptCatalog)
        {
            switch (tier)
            {
                case FailureTier.Fumble: return PromptTemplates.GetCatalogString(promptCatalog, "datee-horniness-tier-intensity-fumble");
                case FailureTier.Misfire: return PromptTemplates.GetCatalogString(promptCatalog, "datee-horniness-tier-intensity-misfire");
                case FailureTier.TropeTrap: return PromptTemplates.GetCatalogString(promptCatalog, "datee-horniness-tier-intensity-trope-trap");
                case FailureTier.Catastrophe:
                case FailureTier.Legendary:
                    return PromptTemplates.GetCatalogString(promptCatalog, "datee-horniness-tier-intensity-catastrophe");
                default:
                    return string.Empty;
            }
        }

        internal static string GetHorninessReactionGuidance(
            int interest,
            bool overlayApplied,
            Pinder.Core.Rolls.FailureTier tier,
            PromptCatalog? promptCatalog = null)
        {
            if (!overlayApplied) return string.Empty;

            string band = interest < HorninessWarmthThreshold
                ? PromptTemplates.GetCatalogString(promptCatalog, "datee-horniness-reaction-below-threshold")
                : PromptTemplates.GetCatalogString(promptCatalog, "datee-horniness-reaction-high-interest");
            string tierIntensity = GetHorninessTierIntensity(tier, promptCatalog);
            string composed = $"Current interest: {interest}/25. {band}";
            if (!string.IsNullOrWhiteSpace(tierIntensity))
                composed += " " + tierIntensity;
            return composed;
        }
    }
}
