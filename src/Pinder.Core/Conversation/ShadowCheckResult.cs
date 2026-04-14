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

        /// <summary>The DC the roll had to meet or exceed (20 − shadowValue).</summary>
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

        public ShadowCheckResult(
            bool checkPerformed,
            ShadowStatType shadow,
            int roll,
            int dc,
            bool isMiss,
            FailureTier tier,
            bool overlayApplied)
        {
            CheckPerformed = checkPerformed;
            Shadow = shadow;
            Roll = roll;
            DC = dc;
            IsMiss = isMiss;
            Tier = tier;
            OverlayApplied = overlayApplied;
        }

        /// <summary>A sentinel value representing "no shadow check was performed this turn".</summary>
        public static readonly ShadowCheckResult NotPerformed =
            new ShadowCheckResult(false, ShadowStatType.Madness, 0, 0, false, FailureTier.None, false);
    }
}
