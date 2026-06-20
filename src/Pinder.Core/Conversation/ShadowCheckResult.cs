using System.Text.Json.Serialization;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Result of the per-turn shadow check. Fires when the player uses a stat
    /// whose paired shadow value is > 0. On a miss, the corruption instruction
    /// is applied and (if the main roll succeeded) the outcome is forced to fail.
    /// </summary>
    public sealed class ShadowCheckResult
    {
        /// <summary>True if a shadow check was actually performed this turn.</summary>
        public bool CheckPerformed { get; }

        /// <summary>The shadow stat that was checked.</summary>
        public ShadowStatType Shadow { get; }

        /// <summary>The d20 roll value (1–20).</summary>
        public int Roll { get; }

        /// <summary>The DC the roll had to meet or exceed (shadowValue, with bias applied).</summary>
        public int DC { get; }

        /// <summary>True if the roll missed the DC, meaning corruption may fire.</summary>
        public bool IsMiss { get; }

        /// <summary>The failure tier if IsMiss is true, otherwise None.</summary>
        public FailureTier Tier { get; }

        /// <summary>
        /// True if the corruption instruction was found and applied
        /// (i.e. IsMiss = true AND instruction existed AND main roll was a success that got overridden).
        /// </summary>
        public bool OverlayApplied { get; }

        /// <summary>
        /// Canonical check result from <see cref="RollEngine.ResolveCheck"/>.
        /// Phase 1 (additive): attached alongside existing bespoke fields.
        /// Null only for the <see cref="NotPerformed"/> sentinel.
        /// </summary>
        public RollCheckResult? Check { get; }

        /// <summary>
        /// Human-readable consequence text for this shadow check (#964).
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

        public ShadowCheckResult(
            bool checkPerformed,
            ShadowStatType shadow,
            int roll,
            int dc,
            bool isMiss,
            FailureTier tier,
            bool overlayApplied,
            RollCheckResult? check = null)
        {
            CheckPerformed = checkPerformed;
            Shadow = shadow;
            Roll = roll;
            DC = dc;
            IsMiss = isMiss;
            Tier = tier;
            OverlayApplied = overlayApplied;
            Check = check;
        }

        /// <summary>A sentinel value representing "no shadow check was performed this turn".</summary>
        public static readonly ShadowCheckResult NotPerformed =
            new ShadowCheckResult(false, ShadowStatType.Madness, 0, 0, false, FailureTier.Success, false);
    }
}
