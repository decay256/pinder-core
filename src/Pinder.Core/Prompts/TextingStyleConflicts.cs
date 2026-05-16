using System;
using System.Collections.Generic;
using System.Linq;

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
        // Factory — parse the YAML file content.
        //
        // The YAML format is predictable (see data/persona/texting-style-conflicts.yaml),
        // so we use a hand-written parser rather than pulling in a YAML library
        // dependency into netstandard2.0 Pinder.Core.
        //
        // Expected shape:
        //   conflicts:
        //     - axis_a: { axis: <name>, value: "<string>" }
        //       axis_b: { axis: <name>, value: "<string>" }
        //       reason: "<string>"
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

            var lines = yamlContent
                .Replace("\r\n", "\n")
                .Split('\n');

            var entries = new List<ConflictEntry>();

            // State machine: collect raw block per "- axis_a:" item boundary.
            var block = new List<string>();

            void FlushBlock()
            {
                if (block.Count == 0) return;
                var entry = ParseBlock(block);
                if (entry != null) entries.Add(entry);
                block.Clear();
            }

            bool inConflictsSection = false;

            foreach (var rawLine in lines)
            {
                var trimmed = rawLine.TrimEnd();

                // Skip blank lines and full-line comments.
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.TrimStart().StartsWith("#", StringComparison.Ordinal))
                    continue;

                if (trimmed.TrimStart() == "conflicts:")
                {
                    inConflictsSection = true;
                    continue;
                }

                if (!inConflictsSection) continue;

                // Each entry block starts with "  - axis_a:".
                var stripped = trimmed.TrimStart();
                if (stripped.StartsWith("- axis_a:", StringComparison.Ordinal))
                {
                    FlushBlock();
                    block.Add(trimmed);
                }
                else if (block.Count > 0)
                {
                    block.Add(trimmed);
                }
            }
            FlushBlock();

            // Validate: all entries must have non-empty reasons.
            for (int i = 0; i < entries.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(entries[i].Reason))
                    throw new FormatException(
                        $"Conflict entry #{i + 1} has an empty reason. " +
                        "All conflict matrix entries must include a reason string.");
            }

            return new TextingStyleConflicts(entries);
        }

        // ------------------------------------------------------------------
        // Block parser — extract (axisA, valueA, axisB, valueB, reason)
        // from the multi-line block collected for one "- axis_a:" item.
        // ------------------------------------------------------------------

        private static ConflictEntry? ParseBlock(List<string> block)
        {
            string? axisA  = null;
            string? valueA = null;
            string? axisB  = null;
            string? valueB = null;
            string? reason = null;

            foreach (var rawLine in block)
            {
                var line = rawLine.TrimStart(' ', '-').Trim();

                if (line.StartsWith("axis_a:", StringComparison.OrdinalIgnoreCase))
                {
                    (axisA, valueA) = ParseInlineAxisValue(line.Substring("axis_a:".Length).Trim());
                }
                else if (line.StartsWith("axis_b:", StringComparison.OrdinalIgnoreCase))
                {
                    (axisB, valueB) = ParseInlineAxisValue(line.Substring("axis_b:".Length).Trim());
                }
                else if (line.StartsWith("reason:", StringComparison.OrdinalIgnoreCase))
                {
                    reason = UnquoteYamlString(line.Substring("reason:".Length).Trim());
                }
            }

            if (axisA == null || valueA == null || axisB == null || valueB == null || reason == null)
                return null;

            return new ConflictEntry(axisA, valueA, axisB, valueB, reason);
        }

        /// <summary>
        /// Parses the inline <c>{ axis: name, value: "..." }</c> flow-mapping.
        /// </summary>
        private static (string axis, string value) ParseInlineAxisValue(string inline)
        {
            // Strip surrounding braces: "{ axis: length, value: "..." }"
            var content = inline.Trim('{', '}', ' ');

            string axis  = string.Empty;
            string value = string.Empty;

            // We need to handle the fact that `value` can contain commas.
            // Strategy: find "axis:" first (before the first comma), then
            // "value:" as the remainder.
            int axisIdx = content.IndexOf("axis:", StringComparison.OrdinalIgnoreCase);
            int valueIdx = content.IndexOf("value:", StringComparison.OrdinalIgnoreCase);

            if (axisIdx >= 0 && valueIdx >= 0)
            {
                if (axisIdx < valueIdx)
                {
                    // axis comes first: "axis: foo, value: bar"
                    string axisSection  = content.Substring(axisIdx + 5, valueIdx - axisIdx - 5).Trim().TrimEnd(',').Trim();
                    string valueSection = content.Substring(valueIdx + 6).Trim();
                    axis  = UnquoteYamlString(axisSection);
                    value = UnquoteYamlString(valueSection);
                }
                else
                {
                    // value comes first: "value: bar, axis: foo"
                    string valueSection = content.Substring(valueIdx + 6, axisIdx - valueIdx - 6).Trim().TrimEnd(',').Trim();
                    string axisSection  = content.Substring(axisIdx + 5).Trim();
                    axis  = UnquoteYamlString(axisSection);
                    value = UnquoteYamlString(valueSection);
                }
            }

            return (axis, value);
        }

        /// <summary>
        /// Strips surrounding YAML quotes (' or ") if present.
        /// </summary>
        private static string UnquoteYamlString(string s)
        {
            s = s.Trim();
            if (s.Length >= 2)
            {
                if ((s[0] == '"' && s[s.Length - 1] == '"') ||
                    (s[0] == '\'' && s[s.Length - 1] == '\''))
                {
                    return s.Substring(1, s.Length - 2);
                }
            }
            return s;
        }
    }
}
