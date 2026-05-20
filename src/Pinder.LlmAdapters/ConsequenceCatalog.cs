using System.Collections.Generic;
using Pinder.Core.I18n;

namespace Pinder.LlmAdapters
{
    /// <summary>
    /// Thin adapter that exposes <see cref="I18nCatalog"/>'s flat
    /// <c>Strings</c> dictionary as an <see cref="IConsequenceCatalog"/>
    /// for engine-side consequence population (#976).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Construction is zero-allocation beyond the reference copy —
    /// the underlying <see cref="I18nCatalog"/> is already loaded
    /// and frozen. <see cref="Lookup"/> is O(1) dictionary read.
    /// </para>
    /// <para>
    /// Per the ticket spec, a missing key returns null (not throws).
    /// Engines leave <c>Consequence</c> null in that case — documented
    /// behaviour; file a follow-up ticket for the missing entry.
    /// </para>
    /// </remarks>
    public sealed class ConsequenceCatalog : IConsequenceCatalog
    {
        private readonly IReadOnlyDictionary<string, string> _strings;

        /// <summary>
        /// Wrap an existing <see cref="I18nCatalog"/>'s flat strings.
        /// </summary>
        public ConsequenceCatalog(I18nCatalog catalog)
        {
            _strings = catalog.Strings;
        }

        /// <inheritdoc />
        public string? Lookup(string key)
        {
            _strings.TryGetValue(key, out var value);
            return value;
        }
    }
}
