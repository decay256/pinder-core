using System;
using Pinder.Core.I18n;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Handles the per-turn paired-shadow d20 check.
    /// Extracted from the inline logic in <c>GameSession.cs</c> as part of #901.
    /// Uses the same RNG instance as <see cref="SteeringEngine"/> and
    /// <see cref="HorninessEngine"/> so dice consumption is unchanged.
    /// </summary>
    internal sealed class ShadowCheckEngine
    {
        private readonly Random _rng;
        private readonly IConsequenceCatalog? _consequenceCatalog;
        private readonly int _shadowDcBias;

        public ShadowCheckEngine(Random rng, IConsequenceCatalog? consequenceCatalog = null, int shadowDcBias = 0)
        {
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            _consequenceCatalog = consequenceCatalog;
            _shadowDcBias = shadowDcBias;
        }

        /// <summary>
        /// Performs the shadow d20 check for a given shadow type and value.
        /// Returns a <see cref="ShadowCheckResult"/> with <c>OverlayApplied = false</c>
        /// (the caller is responsible for applying the LLM overlay and updating that flag
        /// by constructing a new result if needed).
        /// Returns <see cref="ShadowCheckResult.NotPerformed"/> when <paramref name="shadowValue"/> &lt;= 0.
        /// </summary>
        public ShadowCheckResult Check(ShadowStatType shadow, int shadowValue)
        {
            if (shadowValue <= 0)
                return ShadowCheckResult.NotPerformed;

            int shadowDC = RollEngine.ApplyDcBias(shadowValue, _shadowDcBias);

            // #901: route through single entry point.
            // Dice consumption: one Roll(20) — identical to old _steeringEngine.RollD20().
            var check = RollEngine.ResolveCheck(
                RollCheckKind.Shadow,
                new RandomDiceRollerAdapter(_rng),
                System.Array.Empty<NamedModifier>(),
                shadowDC);

            bool isMiss = !check.IsSuccess;
            FailureTier tier = isMiss ? check.Tier : FailureTier.Success;

            var result = new ShadowCheckResult(
                checkPerformed: true,
                shadow:         shadow,
                roll:           check.DieRoll,
                dc:             shadowDC,
                isMiss:         isMiss,
                tier:           tier,
                overlayApplied: false,
                check:          check);

            // #976: populate Consequence from i18n catalogue.
            if (isMiss && _consequenceCatalog != null)
            {
                string key = ConsequenceKeys.ForShadowMiss(shadow);
                string? template = _consequenceCatalog.Lookup(key);
                if (template != null)
                {
                    result.ApplyConsequence(ConsequenceKeys.ApplySlots(template));
                }
            }

            return result;
        }
    }
}
