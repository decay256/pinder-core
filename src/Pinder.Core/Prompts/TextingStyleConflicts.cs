using System;
using System.Collections.Generic;
using System.IO;
using YamlDotNet.RepresentationModel;

namespace Pinder.Core.Prompts
{
    /// <summary>
    /// Compatibility matrix for texting-style axis values that MUST NOT
    /// co-occur in the same aggregated profile. Loaded from
    /// <c>data/persona/texting-style-conflicts.yaml</c> at startup.
    ///
    /// Every conflict entry is bidirectional by construction: lookup is
    /// symmetric regardless of which axis-value pair is passed first.
    /// Entries with empty reasons are rejected at load time.
    ///
    /// See #907.
    /// </summary>
    public class TextingStyleConflicts
    {
        // Key: "axisA::valueA||axisB::valueB" — normalized key.
        // Value: human-readable reason string.
        private readonly Dictionary<string, string> _conflicts
            = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Load conflicts from <paramref name="yamlFilePath"/>.
        /// The file must contain a top-level <c>conflicts</c> sequence
        /// whose entries have <c>axis_a</c>, <c>axis_b</c>, and
        /// <c>reason</c> fields.
        /// </summary>
        /// <exception cref="InvalidDataException">
        /// Any entry has an empty or whitespace-only reason.
        /// </exception>
        public TextingStyleConflicts(string yamlFilePath)
        {
            if (string.IsNullOrEmpty(yamlFilePath))
                throw new ArgumentNullException(nameof(yamlFilePath));
            Load(yamlFilePath);
        }

        /// <summary>
        /// Returns <c>true</c> if the two axis-value pairs are known to
        /// conflict under the loaded matrix. Bidirectional — order of
        /// <paramref name="a"/> and <paramref name="b"/> does not matter.
        /// </summary>
        public bool AreConflicting(
            (string axis, string value) a,
            (string axis, string value) b)
            => FindConflict(a, b) != null;

        /// <summary>
        /// Returns the human-readable reason for the conflict, or
        /// <c>null</c> if the pair does not conflict.
        /// </summary>
        public string GetReason(
            (string axis, string value) a,
            (string axis, string value) b)
        {
            var key = FindConflict(a, b);
            if (key == null) return null;
            _conflicts.TryGetValue(key, out var reason);
            return reason;
        }

        /// <summary>
        /// Number of conflict entries loaded (one per unordered pair).
        /// </summary>
        public int Count => _conflicts.Count;

        private string FindConflict(
            (string axis, string value) a,
            (string axis, string value) b)
        {
            string keyAB = KeyFor(a, b);
            if (_conflicts.ContainsKey(keyAB)) return keyAB;

            string keyBA = KeyFor(b, a);
            if (_conflicts.ContainsKey(keyBA)) return keyBA;

            return null;
        }

        private static string KeyFor(
            (string axis, string value) a,
            (string axis, string value) b)
            => $"{a.axis}::{a.value}||{b.axis}::{b.value}";

        private void Load(string yamlFilePath)
        {
            using var reader = new StreamReader(yamlFilePath);
            var yaml = new YamlStream();
            yaml.Load(reader);

            if (yaml.Documents.Count == 0)
                throw new InvalidDataException(
                    $"YAML file '{yamlFilePath}' contains no documents.");

            var root = yaml.Documents[0].RootNode as YamlMappingNode;
            if (root == null)
                throw new InvalidDataException(
                    $"YAML file '{yamlFilePath}' root is not a mapping node.");

            if (!root.Children.TryGetValue(
                    new YamlScalarNode("conflicts"), out var conflictsNode))
                throw new InvalidDataException(
                    $"YAML file '{yamlFilePath}' missing top-level 'conflicts' key.");

            var sequence = conflictsNode as YamlSequenceNode;
            if (sequence == null)
                throw new InvalidDataException(
                    $"YAML file '{yamlFilePath}' 'conflicts' is not a sequence.");

            foreach (var entry in sequence)
            {
                var mapping = entry as YamlMappingNode;
                if (mapping == null) continue;

                var (axisA, valueA) = ReadAxisPair(mapping, "axis_a");
                var (axisB, valueB) = ReadAxisPair(mapping, "axis_b");
                var reason = ReadScalar(mapping, "reason");

                if (string.IsNullOrWhiteSpace(reason))
                    throw new InvalidDataException(
                        $"Conflict entry ({axisA}: {valueA}) vs ({axisB}: {valueB}) " +
                        $"has empty reason in '{yamlFilePath}'.");

                var key = KeyFor((axisA, valueA), (axisB, valueB));
                _conflicts[key] = reason;
            }
        }

        private static (string axis, string value) ReadAxisPair(
            YamlMappingNode parent, string key)
        {
            if (!parent.Children.TryGetValue(
                    new YamlScalarNode(key), out var node))
                throw new InvalidDataException(
                    $"Missing '{key}' in conflict entry.");

            var axisMapping = node as YamlMappingNode;
            if (axisMapping == null)
                throw new InvalidDataException(
                    $"'{key}' is not a mapping node in conflict entry.");

            string axis = ReadScalar(axisMapping, "axis");
            string value = ReadScalar(axisMapping, "value");

            if (string.IsNullOrWhiteSpace(axis))
                throw new InvalidDataException(
                    $"'{key}.axis' is missing or empty.");
            if (value == null) // empty value is semantically valid
                value = string.Empty;

            return (axis, value);
        }

        private static string ReadScalar(YamlMappingNode parent, string key)
        {
            if (!parent.Children.TryGetValue(
                    new YamlScalarNode(key), out var node))
                throw new InvalidDataException(
                    $"Missing '{key}' in YAML mapping.");

            var scalar = node as YamlScalarNode;
            return scalar?.Value ?? string.Empty;
        }
    }
}
