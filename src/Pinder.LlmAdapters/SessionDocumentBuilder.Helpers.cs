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

        private static string GetThresholdInstruction(int before, int after, InterestState newState, string dateeName)
        {
            if (newState == InterestState.Unmatched)
                return PromptTemplates.InterestBeatUnmatched.Replace("{datee_name}", dateeName);
            if (newState == InterestState.DateSecured)
                return PromptTemplates.InterestBeatDateSecured.Replace("{datee_name}", dateeName);
            if (after > before && after > 15 && before <= 15)
                return PromptTemplates.InterestBeatAbove15.Replace("{datee_name}", dateeName);
            if (after < before && after < 8 && before >= 8)
                return PromptTemplates.InterestBeatBelow8.Replace("{datee_name}", dateeName);

            return PromptTemplates.InterestBeatGeneric.Replace("{datee_name}", dateeName);
        }

        private static string BuildShadowTaintBlock(Dictionary<ShadowStatType, int>? thresholds)
        {
            if (thresholds == null || thresholds.Count == 0) return string.Empty;
            var sb = new StringBuilder();
            if (thresholds.TryGetValue(ShadowStatType.Madness, out int madness) && madness > 5)
                sb.AppendLine("Your Madness is elevated. Your charm has an uncanny quality — warmth that somehow feels slightly off. Smooth words that land wrong. People can't identify why they feel uneasy. You don't notice.");
            if (thresholds.TryGetValue(ShadowStatType.Despair, out int despair) && despair > 6)
                sb.AppendLine("Your Despair is elevated. You want to be desired and it shows. Neediness leaks through even confident-sounding Rizz options. You are performing magnetism rather than feeling it.");
            if (thresholds.TryGetValue(ShadowStatType.Denial, out int denial) && denial > 5)
                sb.AppendLine("Your Denial is elevated. Your honest options sound rehearsed. You tell truths that are technically true but curated. Emotional availability is performed rather than felt.");
            if (thresholds.TryGetValue(ShadowStatType.Fixation, out int fixation) && fixation > 5)
                sb.AppendLine("Your Fixation is elevated. Chaos options feel forced — spontaneity that sounds calculated. You're trying to seem unpredictable. It shows.");
            if (thresholds.TryGetValue(ShadowStatType.Dread, out int dread) && dread > 5)
                sb.AppendLine("Your Dread is elevated. Even successful Wit options have melancholy undertones. Jokes land but leave a slightly hollow aftertaste. You're funny because it's easier than being vulnerable.");
            if (thresholds.TryGetValue(ShadowStatType.Overthinking, out int overthinking) && overthinking > 5)
                sb.AppendLine("Your Overthinking is elevated. Self-Awareness options are too clinical. You explain your feelings rather than expressing them. You know this. You're doing it anyway.");
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
        internal static string GetResistanceBlock(int interest)
        {
            string descriptor;
            if (interest >= 25)
                descriptor = PromptTemplates.ResistanceDissolved;
            else if (interest >= 21)
                descriptor = PromptTemplates.ResistanceAlmostConvinced;
            else if (interest >= 15)
                descriptor = PromptTemplates.ResistanceDeliberateApproach;
            else if (interest >= 10)
                descriptor = PromptTemplates.ResistanceUnstableAgreement;
            else if (interest >= 5)
                descriptor = PromptTemplates.ResistanceSkepticalInterest;
            else if (interest >= 1)
                descriptor = PromptTemplates.ResistanceActiveDisengagement;
            else
                descriptor = PromptTemplates.ResistanceActiveDisengagement;

            return $"Current interest: {interest}/25. Resistance level: {descriptor}";
        }

        /// <summary>
        /// Returns per-tier datee reaction guidance for failure degradation (#493).
        /// </summary>
        internal static string GetDateeReactionGuidance(FailureTier tier)
        {
            switch (tier)
            {
                case FailureTier.Fumble: return PromptTemplates.DateeReactionFumble;
                case FailureTier.Misfire: return PromptTemplates.DateeReactionMisfire;
                case FailureTier.TropeTrap: return PromptTemplates.DateeReactionTropeTrap;
                case FailureTier.Catastrophe: return PromptTemplates.DateeReactionCatastrophe;
                case FailureTier.Legendary: return PromptTemplates.DateeReactionLegendary;
                default: return string.Empty;
            }
        }

        private static string GetHorninessTierIntensity(Pinder.Core.Rolls.FailureTier tier)
        {
            switch (tier)
            {
                case FailureTier.Fumble: return PromptTemplates.DateeHorninessTierIntensityFumble;
                case FailureTier.Misfire: return PromptTemplates.DateeHorninessTierIntensityMisfire;
                case FailureTier.TropeTrap: return PromptTemplates.DateeHorninessTierIntensityTropeTrap;
                case FailureTier.Catastrophe:
                case FailureTier.Legendary:
                    return PromptTemplates.DateeHorninessTierIntensityCatastrophe;
                default:
                    return string.Empty;
            }
        }

        internal static string GetHorninessReactionGuidance(int interest, bool overlayApplied, Pinder.Core.Rolls.FailureTier tier)
        {
            if (!overlayApplied) return string.Empty;

            string band = interest < HorninessWarmthThreshold
                ? PromptTemplates.DateeHorninessReactionBelowThreshold
                : PromptTemplates.DateeHorninessReactionHighInterest;
            string tierIntensity = GetHorninessTierIntensity(tier);
            string composed = $"Current interest: {interest}/25. {band}";
            if (!string.IsNullOrWhiteSpace(tierIntensity))
                composed += " " + tierIntensity;
            return composed;
        }
    }
}
