using System.Text.Json.Serialization;
using Pinder.Core.Rolls;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Result of the per-turn horniness overlay check.
    /// </summary>
    public sealed class HorninessCheckResult
    {
        /// <summary>The d20 roll value.</summary>
        public int Roll { get; }

        /// <summary>The modifier applied to the roll (player's base horniness or 0).</summary>
        public int Modifier { get; }

        /// <summary>The total (Roll + Modifier).</summary>
        public int Total { get; }

        /// <summary>The DC (20 - sessionHorniness).</summary>
        public int DC { get; }

        /// <summary>True if the check was a miss (total &lt; DC), meaning corruption fires.</summary>
        public bool IsMiss { get; }

        /// <summary>The failure tier if IsMiss is true, otherwise None.</summary>
        public FailureTier Tier { get; }

        /// <summary>Whether an overlay was actually applied (miss + instruction found + shadows present).</summary>
        public bool OverlayApplied { get; }

        /// <summary>
        /// Canonical check result from <see cref="RollEngine.ResolveCheck"/>.
        /// Phase 1 (additive): attached alongside existing bespoke fields.
        /// Phase 2 will replace the bespoke duplicate fields with <c>Check.*</c> projections.
        /// Null only for the <see cref="NotPerformed"/> sentinel.
        /// </summary>
        public RollCheckResult? Check { get; }

        /// <summary>
        /// Human-readable consequence text for this horniness check (#964).
        /// Population deferred to follow-up. SPA falls back to client-side i18n catalogue when null.
        /// </summary>
        [JsonPropertyName("consequence")]
        public string? Consequence { get; private set; }

        /// <summary>
        /// Apply an engine-populated consequence string. Idempotent — throws
        /// <see cref="System.InvalidOperationException"/> if already set (#964).
        /// </summary>
        public void ApplyConsequence(string consequence)
        {
            if (Consequence != null)
                throw new System.InvalidOperationException("Consequence already applied");
            Consequence = consequence;
        }

        public HorninessCheckResult(
            int roll, int modifier, int total, int dc,
            bool isMiss, FailureTier tier, bool overlayApplied,
            RollCheckResult? check = null)
        {
            Roll = roll;
            Modifier = modifier;
            Total = total;
            DC = dc;
            IsMiss = isMiss;
            Tier = tier;
            OverlayApplied = overlayApplied;
            Check = check;
        }

        /// <summary>A result for when no check was performed (sessionHorniness = 0 or no shadows).</summary>
        public static HorninessCheckResult NotPerformed { get; } =
            new HorninessCheckResult(0, 0, 0, 0, false, FailureTier.Success, false);
    }
}
