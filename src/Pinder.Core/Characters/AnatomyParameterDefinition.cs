using System.Collections.Generic;

namespace Pinder.Core.Characters
{
    /// <summary>
    /// One of the nine anatomy parameters (e.g. Length, Girth, Eye Style).
    /// Contains all selectable tiers for that parameter.
    /// </summary>
    /// <remarks>
    /// As of #551 (admin-content-editor sprint Phase 2a), parameters carry
    /// a <see cref="ScaleType"/> distinguishing numeric scales (length, girth,
    /// ball_size) from categorical ones (eye style, tattoos, …). Numeric
    /// parameters expose a <see cref="NumericRange"/> with min/max/unit; their
    /// tiers carry <see cref="AnatomyTierDefinition.NumericBreakpoint"/> values
    /// indicating position on the scale. The default value of
    /// <see cref="ScaleType"/> is <c>"categorical"</c> so files authored before
    /// this change still parse with their original semantics.
    /// </remarks>
    public sealed class AnatomyParameterDefinition
    {
        /// <summary>String constant for the categorical scale type. Default.</summary>
        public const string ScaleTypeCategorical = "categorical";

        /// <summary>String constant for the numeric scale type.</summary>
        public const string ScaleTypeNumeric     = "numeric";

        public string Id   { get; }
        public string Name { get; }
        public IReadOnlyList<AnatomyTierDefinition> Tiers { get; }

        /// <summary>
        /// Either <see cref="ScaleTypeCategorical"/> or <see cref="ScaleTypeNumeric"/>.
        /// Default is categorical; files without a <c>scale_type</c> field parse
        /// as categorical.
        /// </summary>
        public string ScaleType { get; }

        /// <summary>
        /// Min/max/unit for the scale. Non-null if and only if
        /// <see cref="ScaleType"/> equals <see cref="ScaleTypeNumeric"/>.
        /// </summary>
        public NumericRangeSpec? NumericRange { get; }

        private readonly Dictionary<string, AnatomyTierDefinition> _tierIndex;

        public AnatomyParameterDefinition(string id, string name,
            IReadOnlyList<AnatomyTierDefinition> tiers,
            string? scaleType = null,
            NumericRangeSpec? numericRange = null)
        {
            Id           = id;
            Name         = name;
            Tiers        = tiers;
            ScaleType    = scaleType ?? ScaleTypeCategorical;
            NumericRange = numericRange;

            _tierIndex = new Dictionary<string, AnatomyTierDefinition>(
                System.StringComparer.OrdinalIgnoreCase);
            foreach (var t in tiers)
                _tierIndex[t.TierId] = t;
        }

        /// <summary>Returns the tier with the given id, or null if not found.</summary>
        public AnatomyTierDefinition? GetTier(string tierId)
        {
            _tierIndex.TryGetValue(tierId, out var tier);
            return tier;
        }
    }
}
