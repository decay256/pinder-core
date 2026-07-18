using System;
using System.Collections.Generic;
using System.Linq;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Pinder.Core.Prompts
{
    /// <summary>
    /// Loads and exposes the texting-style conflict matrix from
    /// <c>data/persona/texting-style-conflicts.yaml</c>.
    ///
    /// Each entry encodes an unordered pair of <c>(axis, value)</c> tuples
    /// that MUST NOT co-occur in the same aggregated style profile. The
    /// constraint is bidirectional — <see cref="AreConflicting"/> checks
    /// both orderings.
    ///
    /// Usage: construct via <see cref="LoadFrom(string)"/>, then inject
    /// into <see cref="TextingStyleAggregator"/>. For tests, a no-op
    /// empty catalog is available via <see cref="Empty"/>.
    ///
    /// See <c>docs/persona/texting-style-aggregation.md</c> and
    /// <see href="https://github.com/decay256/pinder-core/issues/907"/>.
    /// </summary>
    public sealed class TextingStyleConflicts
    {
        /// <summary>A single bidirectional conflict entry.</summary>
        public sealed class ConflictEntry
        {
            public string AxisA  { get; }
            public string ValueA { get; }
            public string AxisB  { get; }
            public string ValueB { get; }
            public string Reason { get; }

            public ConflictEntry(
                string axisA, string valueA,
                string axisB, string valueB,
                string reason)
            {
                AxisA  = axisA;
                ValueA = valueA;
                AxisB  = axisB;
                ValueB = valueB;
                Reason = reason;
            }
        }

        private readonly IReadOnlyList<ConflictEntry> _entries;

        private TextingStyleConflicts(IReadOnlyList<ConflictEntry> entries)
        {
            _entries = entries;
        }

        /// <summary>An empty catalog — no conflicts. Useful in tests.</summary>
        public static TextingStyleConflicts Empty { get; } =
            new TextingStyleConflicts(Array.Empty<ConflictEntry>());

        /// <summary>All loaded conflict entries.</summary>
        public IReadOnlyList<ConflictEntry> Entries => _entries;

        // ------------------------------------------------------------------
        // Public query surface
        // ------------------------------------------------------------------

        /// <summary>
        /// Returns <c>true</c> if the two <c>(axis, value)</c> pairs conflict
        /// under the loaded matrix. The check is bidirectional (order of a/b
        /// does not matter). Value matching is case-insensitive.
        /// </summary>
        public bool AreConflicting(
            (string axis, string value) a,
            (string axis, string value) b)
        {
            foreach (var entry in _entries)
            {
                bool matchForward =
                    AxisValueEq(entry.AxisA, entry.ValueA, a.axis, a.value) &&
                    AxisValueEq(entry.AxisB, entry.ValueB, b.axis, b.value);

                bool matchReverse =
                    AxisValueEq(entry.AxisA, entry.ValueA, b.axis, b.value) &&
                    AxisValueEq(entry.AxisB, entry.ValueB, a.axis, a.value);

                if (matchForward || matchReverse) return true;
            }
            return false;
        }

        /// <summary>
        /// Returns the conflict reason if the two pairs conflict, or
        /// <c>null</c> if they don't.
        /// </summary>
        public string? GetReason(
            (string axis, string value) a,
            (string axis, string value) b)
        {
            foreach (var entry in _entries)
            {
                bool matchForward =
                    AxisValueEq(entry.AxisA, entry.ValueA, a.axis, a.value) &&
                    AxisValueEq(entry.AxisB, entry.ValueB, b.axis, b.value);

                bool matchReverse =
                    AxisValueEq(entry.AxisA, entry.ValueA, b.axis, b.value) &&
                    AxisValueEq(entry.AxisB, entry.ValueB, a.axis, a.value);

                if (matchForward || matchReverse) return entry.Reason;
            }
            return null;
        }

        private static bool AxisValueEq(
            string entryAxis, string entryValue,
            string queryAxis, string queryValue)
        {
            return string.Equals(entryAxis, queryAxis, StringComparison.OrdinalIgnoreCase)
                && string.Equals(entryValue, queryValue, StringComparison.OrdinalIgnoreCase);
        }

        // ------------------------------------------------------------------
        // Factory - parse the YAML file content with the solution-standard
        // YamlDotNet stack used by rule and prompt content loaders.
        // ------------------------------------------------------------------

        /// <summary>
        /// Parses <paramref name="yamlContent"/> and returns a loaded
        /// <see cref="TextingStyleConflicts"/> catalog.
        /// </summary>
        /// <exception cref="FormatException">
        /// If any entry is malformed, has an empty reason, or references an
        /// unrecognised axis name.
        /// </exception>
        public static TextingStyleConflicts LoadFrom(string yamlContent)
        {
            if (string.IsNullOrWhiteSpace(yamlContent))
                return Empty;

            var entries = new List<ConflictEntry>();
            ConflictCatalogDto? catalog;
            try
            {
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(UnderscoredNamingConvention.Instance)
                    .IgnoreUnmatchedProperties()
                    .Build();
                catalog = deserializer.Deserialize<ConflictCatalogDto>(yamlContent);
            }
            catch (YamlException ex)
            {
                throw new FormatException("Texting-style conflict YAML is malformed.", ex);
            }

            var loaded = catalog?.Conflicts ?? new List<ConflictEntryDto>();
            for (int i = 0; i < loaded.Count; i++)
            {
                var dto = loaded[i];
                var axisA = ValidateAxisValue(dto.AxisA, i, "axis_a");
                var axisB = ValidateAxisValue(dto.AxisB, i, "axis_b");
                if (string.IsNullOrWhiteSpace(dto.Reason))
                    throw new FormatException(
                        $"Conflict entry #{i + 1} has an empty reason. " +
                        "All conflict matrix entries must include a reason string.");

                entries.Add(new ConflictEntry(
                    axisA.axis,
                    axisA.value,
                    axisB.axis,
                    axisB.value,
                    dto.Reason.Trim()));
            }

            return new TextingStyleConflicts(entries);
        }

        private static (string axis, string value) ValidateAxisValue(
            AxisValueDto? dto,
            int entryIndex,
            string fieldName)
        {
            if (dto == null)
                throw new FormatException($"Conflict entry #{entryIndex + 1} is missing {fieldName}.");

            string axis = dto.Axis?.Trim() ?? string.Empty;
            string value = dto.Value?.Trim() ?? string.Empty;
            if (axis.Length == 0 || value.Length == 0)
                throw new FormatException(
                    $"Conflict entry #{entryIndex + 1} has an incomplete {fieldName}; both axis and value are required.");

            if (!KnownAxes.Contains(axis))
                throw new FormatException(
                    $"Conflict entry #{entryIndex + 1} references unknown texting-style axis '{axis}'.");

            return (axis, value);
        }

        private static readonly IReadOnlyCollection<string> KnownAxes =
            new HashSet<string>(TextingStyleAggregator.CanonicalAxisOrder, StringComparer.OrdinalIgnoreCase);

        private sealed class ConflictCatalogDto
        {
            public List<ConflictEntryDto>? Conflicts { get; set; }
        }

        private sealed class ConflictEntryDto
        {
            public AxisValueDto? AxisA { get; set; }
            public AxisValueDto? AxisB { get; set; }
            public string? Reason { get; set; }
        }

        private sealed class AxisValueDto
        {
            public string? Axis { get; set; }
            public string? Value { get; set; }
        }
    }
}
