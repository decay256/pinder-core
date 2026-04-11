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

        public HorninessCheckResult(int roll, int modifier, int total, int dc, bool isMiss, FailureTier tier, bool overlayApplied)
        {
            Roll = roll;
            Modifier = modifier;
            Total = total;
            DC = dc;
            IsMiss = isMiss;
            Tier = tier;
            OverlayApplied = overlayApplied;
        }

        /// <summary>A result for when no check was performed (sessionHorniness = 0 or no shadows).</summary>
        public static HorninessCheckResult NotPerformed { get; } =
            new HorninessCheckResult(0, 0, 0, 0, false, FailureTier.None, false);
    }
}
