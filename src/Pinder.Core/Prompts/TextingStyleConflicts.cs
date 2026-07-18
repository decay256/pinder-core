using System;
using System.Collections.Generic;
using System.Linq;

namespace Pinder.Core.Prompts
{
    /// <summary>
    /// Exposes the validated texting-style conflict matrix loaded by an
    /// outer assembly from <c>data/persona/texting-style-conflicts.yaml</c>.
    ///
    /// Each entry encodes an unordered pair of <c>(axis, value)</c> tuples
    /// that MUST NOT co-occur in the same aggregated style profile. The
    /// constraint is bidirectional — <see cref="AreConflicting"/> checks
    /// both orderings.
    ///
    /// Usage: construct via <see cref="FromEntries"/>, then inject into
    /// <see cref="TextingStyleAggregator"/>. For tests, a no-op empty
    /// catalog is available via <see cref="Empty"/>.
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
                AxisA  = RequireAxis(axisA, nameof(axisA));
                ValueA = RequireValue(valueA, nameof(valueA));
                AxisB  = RequireAxis(axisB, nameof(axisB));
                ValueB = RequireValue(valueB, nameof(valueB));
                Reason = RequireValue(reason, nameof(reason));
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

        /// <summary>
        /// Creates a loaded <see cref="TextingStyleConflicts"/> catalog from
        /// already-parsed conflict rows. Parsing formats such as YAML belong in
        /// outer assemblies; Core owns the domain object and validation.
        /// </summary>
        /// <exception cref="FormatException">
        /// If any entry is malformed, has an empty reason, or references an
        /// unrecognised axis name.
        /// </exception>
        public static TextingStyleConflicts FromEntries(
            IEnumerable<(string AxisA, string ValueA, string AxisB, string ValueB, string Reason)> entries)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));

            var loaded = new List<ConflictEntry>();
            int i = 0;
            foreach (var row in entries)
            {
                var axisA = ValidateAxisValue(row.AxisA, row.ValueA, i, "axis_a");
                var axisB = ValidateAxisValue(row.AxisB, row.ValueB, i, "axis_b");
                if (string.IsNullOrWhiteSpace(row.Reason))
                    throw new FormatException(
                        $"Conflict entry #{i + 1} has an empty reason. " +
                        "All conflict matrix entries must include a reason string.");

                loaded.Add(new ConflictEntry(
                    axisA.axis,
                    axisA.value,
                    axisB.axis,
                    axisB.value,
                    row.Reason.Trim()));
                i++;
            }

            return loaded.Count == 0 ? Empty : new TextingStyleConflicts(loaded);
        }

        private static (string axis, string value) ValidateAxisValue(
            string? rawAxis,
            string? rawValue,
            int entryIndex,
            string fieldName)
        {
            string axis = rawAxis?.Trim() ?? string.Empty;
            string value = rawValue?.Trim() ?? string.Empty;
            if (axis.Length == 0 || value.Length == 0)
                throw new FormatException(
                    $"Conflict entry #{entryIndex + 1} has an incomplete {fieldName}; both axis and value are required.");

            if (!KnownAxes.Contains(axis))
                throw new FormatException(
                    $"Conflict entry #{entryIndex + 1} references unknown texting-style axis '{axis}'.");

            return (axis, value);
        }

        private static string RequireAxis(string? axis, string parameterName)
        {
            string value = RequireValue(axis, parameterName);
            if (!KnownAxes.Contains(value))
                throw new ArgumentException($"Unknown texting-style axis '{value}'.", parameterName);
            return value;
        }

        private static string RequireValue(string? value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Value must be non-empty.", parameterName);
            return value.Trim();
        }

        private static readonly IReadOnlyCollection<string> KnownAxes =
            new HashSet<string>(TextingStyleAggregator.CanonicalAxisOrder, StringComparer.OrdinalIgnoreCase);
    }
}
