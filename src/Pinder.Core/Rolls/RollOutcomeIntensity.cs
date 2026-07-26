using System;
using System.Collections.Generic;

namespace Pinder.Core.Rolls
{
    /// <summary>
    /// Canonical player-roll outcome intensity for downstream non-mechanical systems.
    /// This is intentionally smaller than <see cref="RollResult"/> so prompt
    /// compilers do not depend on dice totals, modifiers, or advantage state.
    /// </summary>
    public enum RollOutcomeIntensity
    {
        Clean,
        Strong,
        Critical,
        Exceptional,
        Nat20,
        Fumble,
        Misfire,
        TropeTrap,
        Catastrophe,
        Nat1
    }

    public static class RollOutcomeIntensityContract
    {
        private static readonly RollOutcomeIntensity[] OrderedValuesArray =
        {
            RollOutcomeIntensity.Clean,
            RollOutcomeIntensity.Strong,
            RollOutcomeIntensity.Critical,
            RollOutcomeIntensity.Exceptional,
            RollOutcomeIntensity.Nat20,
            RollOutcomeIntensity.Fumble,
            RollOutcomeIntensity.Misfire,
            RollOutcomeIntensity.TropeTrap,
            RollOutcomeIntensity.Catastrophe,
            RollOutcomeIntensity.Nat1,
        };

        private static readonly string[] OrderedKeysArray =
        {
            "clean",
            "strong",
            "critical",
            "exceptional",
            "nat20",
            "fumble",
            "misfire",
            "trope_trap",
            "catastrophe",
            "nat1",
        };

        public static IReadOnlyList<RollOutcomeIntensity> OrderedValues { get; } =
            Array.AsReadOnly(OrderedValuesArray);

        public static IReadOnlyList<string> OrderedKeys { get; } =
            Array.AsReadOnly(OrderedKeysArray);

        public static RollOutcomeIntensity FromRollResult(RollResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));

            return result.IsSuccess
                ? FromSuccessMargin(Math.Max(0, result.FinalTotal - result.DC), result.IsNatTwenty)
                : FromFailureTier(result.Tier);
        }

        public static RollOutcomeIntensity FromSuccessMargin(int beatDcBy, bool isNat20)
        {
            if (isNat20) return RollOutcomeIntensity.Nat20;
            if (beatDcBy >= 15) return RollOutcomeIntensity.Exceptional;
            if (beatDcBy >= 10) return RollOutcomeIntensity.Critical;
            if (beatDcBy >= 5) return RollOutcomeIntensity.Strong;
            return RollOutcomeIntensity.Clean;
        }

        public static RollOutcomeIntensity FromFailureTier(FailureTier tier)
        {
            switch (tier)
            {
                case FailureTier.Fumble:
                    return RollOutcomeIntensity.Fumble;
                case FailureTier.Misfire:
                    return RollOutcomeIntensity.Misfire;
                case FailureTier.TropeTrap:
                    return RollOutcomeIntensity.TropeTrap;
                case FailureTier.Catastrophe:
                    return RollOutcomeIntensity.Catastrophe;
                case FailureTier.Legendary:
                    return RollOutcomeIntensity.Nat1;
                default:
                    throw new ArgumentException(
                        "Failure tier must be a failure value, not success.",
                        nameof(tier));
            }
        }

        public static string ToKey(RollOutcomeIntensity intensity)
        {
            switch (intensity)
            {
                case RollOutcomeIntensity.Clean:
                    return "clean";
                case RollOutcomeIntensity.Strong:
                    return "strong";
                case RollOutcomeIntensity.Critical:
                    return "critical";
                case RollOutcomeIntensity.Exceptional:
                    return "exceptional";
                case RollOutcomeIntensity.Nat20:
                    return "nat20";
                case RollOutcomeIntensity.Fumble:
                    return "fumble";
                case RollOutcomeIntensity.Misfire:
                    return "misfire";
                case RollOutcomeIntensity.TropeTrap:
                    return "trope_trap";
                case RollOutcomeIntensity.Catastrophe:
                    return "catastrophe";
                case RollOutcomeIntensity.Nat1:
                    return "nat1";
                default:
                    throw new ArgumentException(
                        "Unknown roll outcome intensity.",
                        nameof(intensity));
            }
        }

        public static bool IsSuccess(RollOutcomeIntensity intensity)
        {
            switch (intensity)
            {
                case RollOutcomeIntensity.Clean:
                case RollOutcomeIntensity.Strong:
                case RollOutcomeIntensity.Critical:
                case RollOutcomeIntensity.Exceptional:
                case RollOutcomeIntensity.Nat20:
                    return true;
                case RollOutcomeIntensity.Fumble:
                case RollOutcomeIntensity.Misfire:
                case RollOutcomeIntensity.TropeTrap:
                case RollOutcomeIntensity.Catastrophe:
                case RollOutcomeIntensity.Nat1:
                    return false;
                default:
                    throw new ArgumentException(
                        "Unknown roll outcome intensity.",
                        nameof(intensity));
            }
        }
    }
}
