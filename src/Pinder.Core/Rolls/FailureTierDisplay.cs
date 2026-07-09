namespace Pinder.Core.Rolls
{
    /// <summary>
    /// Maps a failure tier and roll kind to the i18n display-name key.
    /// </summary>
    public static class FailureTierDisplay
    {
        /// <summary>
        /// Returns the display-name key for a <see cref="FailureTier"/> in the context of a
        /// specific <see cref="RollCheckKind"/>.
        /// </summary>
        public static string DisplayNameKey(FailureTier tier, RollCheckKind kind)
        {
            if (tier == FailureTier.TropeTrap && kind != RollCheckKind.OptionRoll)
                return "display_names.failure_tier.severe";

            switch (tier)
            {
                case FailureTier.Success:
                    return "display_names.failure_tier.none";
                case FailureTier.Fumble:
                    return "display_names.failure_tier.fumble";
                case FailureTier.Misfire:
                    return "display_names.failure_tier.misfire";
                case FailureTier.TropeTrap:
                    return "display_names.failure_tier.trope_trap";
                case FailureTier.Catastrophe:
                    return "display_names.failure_tier.catastrophe";
                case FailureTier.Legendary:
                    return "display_names.failure_tier.legendary";
                default:
                    return "display_names.failure_tier.fallback";
            }
        }
    }
}
