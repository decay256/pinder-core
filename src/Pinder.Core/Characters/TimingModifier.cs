namespace Pinder.Core.Characters
{
    /// <summary>
    /// Adjustment to a character's reply-timing profile contributed by a single item or anatomy tier.
    /// All modifiers are summed across the full build to produce the final TimingProfile.
    /// </summary>
    public sealed class TimingModifier
    {
        /// <summary>Flat minutes added to (or subtracted from) the base reply delay.</summary>
        public int BaseDelayDeltaMinutes { get; }

        /// <summary>Multiplier applied to the delay variance. Compounded multiplicatively.</summary>
        public float DelayVarianceMultiplier { get; }

        /// <summary>Probability added to the dry-spell chance (0–1 range).</summary>
        public float DrySpellProbabilityDelta { get; }

        /// <summary>"neutral" | "shows" | "hides"</summary>
        public string ReadReceipt { get; }

        public TimingModifier(int delayDelta, float varianceMult, float drySpellDelta, string readReceipt)
        {
            BaseDelayDeltaMinutes   = delayDelta;
            DelayVarianceMultiplier = varianceMult;
            DrySpellProbabilityDelta = drySpellDelta;
            ReadReceipt             = readReceipt ?? "neutral";
        }

        /// <summary>Identity element — no adjustment.</summary>
        public static TimingModifier Zero => new TimingModifier(0, 1.0f, 0f, "neutral");
    }
}
