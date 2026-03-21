using System.Collections.Generic;

namespace Pinder.Core.Characters
{
    /// <summary>
    /// One of the nine anatomy parameters (e.g. Length, Girth, Eye Style).
    /// Contains all selectable tiers for that parameter.
    /// </summary>
    public sealed class AnatomyParameterDefinition
    {
        public string Id   { get; }
        public string Name { get; }
        public IReadOnlyList<AnatomyTierDefinition> Tiers { get; }

        private readonly Dictionary<string, AnatomyTierDefinition> _tierIndex;

        public AnatomyParameterDefinition(string id, string name,
            IReadOnlyList<AnatomyTierDefinition> tiers)
        {
            Id    = id;
            Name  = name;
            Tiers = tiers;

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
