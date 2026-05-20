using System;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.I18n;
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
        private readonly IConsequenceCatalog? _consequenceCatalog;

        public HorninessEngine(Random rng, IConsequenceCatalog? consequenceCatalog = null)
        {
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            _consequenceCatalog = consequenceCatalog;
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
            Func<string, Task> applyOverlay,
            CancellationToken ct = default)
        {
            HorninessCheckResult result;
            string? instruction;
            (result, instruction) = PeekAsync(sessionHorniness, playerShadows, statDeliveryInstructions, ct);
            if (instruction != null)
                await applyOverlay(instruction).ConfigureAwait(false);
            return result;
        }

        /// <summary>
        /// Performs the per-turn horniness overlay check and returns both the result
        /// and the overlay instruction (if any), WITHOUT applying the text overlay.
        /// Use when the text rewrite must be deferred to a later point in the pipeline
        /// (e.g. after shadow corruption — see #899).
        /// The returned <see cref="HorninessCheckResult.OverlayApplied"/> is true when
        /// an instruction was found; the caller is responsible for applying the overlay.
        /// Returns (NotPerformed, null) when no check should be run.
        /// </summary>
        public (HorninessCheckResult Result, string? OverlayInstruction) PeekAsync(
            int sessionHorniness,
            SessionShadowTracker? playerShadows,
            object? statDeliveryInstructions,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (sessionHorniness <= 0 || playerShadows == null)
                return (HorninessCheckResult.NotPerformed, null);

            int horninessDC = 20 - sessionHorniness;
            // #901: route through single entry point — dice consumption is identical (one Roll(20))
            var check = RollEngine.ResolveCheck(
                RollCheckKind.Horniness,
                new RandomDiceRollerAdapter(_rng),
                System.Array.Empty<NamedModifier>(),
                horninessDC);

            bool horninessMiss = !check.IsSuccess;

            if (!horninessMiss)
            {
                return (new HorninessCheckResult(
                    check.DieRoll, 0, check.Total, horninessDC,
                    false, FailureTier.Success, false, check), null);
            }

            FailureTier horninessTier = check.Tier;
            string? overlayInstruction = GetHorninessOverlayInstruction(statDeliveryInstructions, horninessTier);

            bool overlayApplied = overlayInstruction != null;
            var result = new HorninessCheckResult(
                check.DieRoll, 0, check.Total, horninessDC,
                true, horninessTier, overlayApplied, check);

            // #976: populate Consequence from i18n catalogue.
            if (_consequenceCatalog != null)
            {
                string key = ConsequenceKeys.ForHorninessMiss(horninessTier);
                string? template = _consequenceCatalog.Lookup(key);
                if (template != null)
                {
                    result.ApplyConsequence(ConsequenceKeys.ApplySlots(template));
                }
            }

            return (result, overlayInstruction);
        }
        // DetermineHorninessTier deleted — #901: use FailureTierLadder.FromMissMargin instead.

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
