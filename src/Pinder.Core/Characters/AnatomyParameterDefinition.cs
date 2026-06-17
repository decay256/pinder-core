using System.Collections.Generic;

namespace Pinder.Core.Characters
{
    /// <summary>
    /// One scalar anatomy parameter (e.g. <c>trunkLengthBase</c>, <c>trunkGirth</c>).
    /// Contains an ordered list of <see cref="AnatomyBandDefinition"/> instances whose
    /// [Lower, Upper) ranges tile the normalised [0..1] scale.
    ///
    /// As of issue #1175, this class replaces the old tier/ScaleType/NumericRange
    /// model. All values arriving from Unity are normalised to [0..1] before
    /// band resolution; the old categorical tier look-up (<c>GetTier</c>) is
    /// replaced by <see cref="ResolveBand"/>.
    ///
    /// Fixed standard thresholds: [0.00, 0.05, 0.20, 0.50, 0.70, 0.95, 1.00]
    /// → 6 bands per scalar parameter. <c>isCircumcised</c> uses 2 bands split
    /// at 0.5; bipolar <c>trunkCurvature</c> uses the same 6 bands on its
    /// normalised [0..1] value (midpoint band = neutral).
    /// </summary>
    public sealed class AnatomyParameterDefinition
    {
        /// <summary>Stable string id matching the Unity <c>CharacterData</c> field name.</summary>
        public string Id   { get; }

        /// <summary>Human-readable display name (e.g. "Trunk Length Base").</summary>
        public string Name { get; }

        /// <summary>
        /// Bands in ascending <see cref="AnatomyBandDefinition.Lower"/> order.
        /// Use <see cref="ResolveBand"/> for value look-up.
        /// </summary>
        public IReadOnlyList<AnatomyBandDefinition> Bands { get; }

        public AnatomyParameterDefinition(string id, string name,
            IReadOnlyList<AnatomyBandDefinition> bands)
        {
            Id    = id;
            Name  = name;
            Bands = bands ?? new List<AnatomyBandDefinition>();
        }

        /// <summary>
        /// Resolves the band whose [Lower, Upper) interval contains
        /// <paramref name="value"/>. The last band's upper bound is inclusive
        /// of 1.0. Values below 0 are clamped to band 0; values above 1 are
        /// clamped to the last band.
        ///
        /// Returns null if <see cref="Bands"/> is empty.
        /// </summary>
        public AnatomyBandDefinition? ResolveBand(float value)
        {
            if (Bands.Count == 0) return null;

            for (int i = 0; i < Bands.Count; i++)
            {
                var band   = Bands[i];
                bool isLast = (i == Bands.Count - 1);

                // Last band: inclusive of upper (handles exactly 1.0).
                if (isLast)
                    return value <= band.Upper ? band : Bands[Bands.Count - 1];

                if (value < band.Upper)
                    return band;
            }

            // Fallback for values > 1.0 (clamp to last band).
            return Bands[Bands.Count - 1];
        }
    }
}
