using System;
using System.Threading.Tasks;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Handles per-turn horniness overlay checks.
    /// Uses a separate RNG (like steering) so it doesn't consume game dice values.
    /// </summary>
    internal sealed class HorninessEngine
    {
        private readonly Random _rng;

        public HorninessEngine(Random rng)
        {
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
        }

        /// <summary>
        /// Performs the per-turn horniness overlay check.
        /// Returns NotPerformed if sessionHorniness is 0 or playerShadows is null.
        /// </summary>
        public async Task<HorninessCheckResult> CheckAsync(
            int sessionHorniness,
            SessionShadowTracker? playerShadows,
            string deliveredMessage,
            ILlmAdapter llm,
            object? statDeliveryInstructions,
            Func<string, Task> applyOverlay)
        {
            if (sessionHorniness <= 0 || playerShadows == null)
                return HorninessCheckResult.NotPerformed;

            int horninessRoll = _rng.Next(1, 21);
            int horninessModifier = 0;
            int horninessTotal = horninessRoll + horninessModifier;
            int horninessDC = 20 - sessionHorniness;
            bool horninessMiss = horninessTotal < horninessDC;

            if (!horninessMiss)
            {
                return new HorninessCheckResult(
                    horninessRoll, horninessModifier, horninessTotal, horninessDC,
                    false, FailureTier.None, false);
            }

            int missMargin = horninessDC - horninessTotal;
            FailureTier horninessTier = DetermineHorninessTier(missMargin);
            string? overlayInstruction = GetHorninessOverlayInstruction(statDeliveryInstructions, horninessTier);

            if (overlayInstruction != null)
            {
                // Caller applies the overlay via the LLM
                await applyOverlay(overlayInstruction).ConfigureAwait(false);
                return new HorninessCheckResult(
                    horninessRoll, horninessModifier, horninessTotal, horninessDC,
                    true, horninessTier, true);
            }

            return new HorninessCheckResult(
                horninessRoll, horninessModifier, horninessTotal, horninessDC,
                true, horninessTier, false);
        }

        /// <summary>
        /// Determines the horniness overlay failure tier from miss margin.
        /// Uses same thresholds as normal failure tiers.
        /// </summary>
        internal static FailureTier DetermineHorninessTier(int missMargin)
        {
            if (missMargin <= 2) return FailureTier.Fumble;
            if (missMargin <= 5) return FailureTier.Misfire;
            if (missMargin <= 9) return FailureTier.TropeTrap;
            return FailureTier.Catastrophe;
        }

        /// <summary>
        /// Retrieves the horniness overlay instruction from the stat delivery instructions.
        /// Uses reflection to call GetHorninessOverlayInstruction on the injected object
        /// (which is StatDeliveryInstructions from the LlmAdapters layer).
        /// Returns null if instructions are not available.
        /// </summary>
        internal static string? GetHorninessOverlayInstruction(object? statDeliveryInstructions, FailureTier tier)
        {
            if (statDeliveryInstructions == null)
                return null;

            var method = statDeliveryInstructions.GetType().GetMethod("GetHorninessOverlayInstruction");
            if (method == null)
                return null;

            return method.Invoke(statDeliveryInstructions, new object[] { tier }) as string;
        }

        /// <summary>
        /// Retrieves the stat-specific failure instruction from the stat delivery instructions.
        /// Uses reflection to avoid a direct dependency on Pinder.LlmAdapters from Pinder.Core.
        /// Returns null if instructions are not available.
        /// </summary>
        internal static string? GetStatFailureInstruction(object? statDeliveryInstructions, StatType stat, FailureTier tier)
        {
            if (statDeliveryInstructions == null)
                return null;

            var method = statDeliveryInstructions.GetType().GetMethod("GetStatFailureInstruction");
            if (method == null)
                return null;

            return method.Invoke(statDeliveryInstructions, new object[] { stat, tier }) as string;
        }

        /// <summary>
        /// Retrieves the shadow corruption instruction from the stat delivery instructions.
        /// Uses reflection to avoid a direct dependency on Pinder.LlmAdapters from Pinder.Core.
        /// Returns null if instructions are not available.
        /// </summary>
        internal static string? GetShadowCorruptionInstruction(object? statDeliveryInstructions, ShadowStatType shadow, FailureTier tier)
        {
            if (statDeliveryInstructions == null)
                return null;

            var method = statDeliveryInstructions.GetType().GetMethod("GetShadowCorruptionInstruction");
            if (method == null)
                return null;

            return method.Invoke(statDeliveryInstructions, new object[] { shadow, tier }) as string;
        }
    }
}
