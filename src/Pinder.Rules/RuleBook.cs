using System;
using System.Collections.Generic;
using System.Linq;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Pinder.Rules
{
    /// <summary>
    /// Loads enriched YAML rules and provides indexed lookups by id and type.
    /// Immutable after construction.
    /// </summary>
    public sealed class RuleBook
    {
        private readonly IReadOnlyList<RuleEntry> _entries;
        private readonly Dictionary<string, RuleEntry> _byId;
        private readonly Dictionary<string, List<RuleEntry>> _byType;

        private RuleBook(IReadOnlyList<RuleEntry> entries)
        {
            _entries = entries;
            _byId = new Dictionary<string, RuleEntry>(StringComparer.OrdinalIgnoreCase);
            _byType = new Dictionary<string, List<RuleEntry>>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in entries)
            {
                if (!string.IsNullOrEmpty(entry.Id))
                {
                    _byId[entry.Id] = entry;
                }

                if (!string.IsNullOrEmpty(entry.Type))
                {
                    if (!_byType.TryGetValue(entry.Type, out var list))
                    {
                        list = new List<RuleEntry>();
                        _byType[entry.Type] = list;
                    }
                    list.Add(entry);
                }
            }
        }

        /// <summary>
        /// Load rules from YAML content string.
        /// Expects a YAML list of rule entry mappings.
        /// </summary>
        /// <exception cref="FormatException">On invalid YAML or unexpected structure.</exception>
        public static RuleBook LoadFrom(string yamlContent)
        {
            if (string.IsNullOrWhiteSpace(yamlContent))
                throw new FormatException("YAML content is empty.");

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();

            List<Dictionary<object, object>>? rawEntries;
            try
            {
                rawEntries = deserializer.Deserialize<List<Dictionary<object, object>>>(yamlContent);
            }
            catch (Exception ex)
            {
                throw new FormatException("Failed to parse YAML: " + ex.Message, ex);
            }

            if (rawEntries == null)
                throw new FormatException("YAML content did not parse to a list of entries.");

            var entries = new List<RuleEntry>(rawEntries.Count);
            foreach (var raw in rawEntries)
            {
                entries.Add(ConvertEntry(raw));
            }

            return new RuleBook(entries);
        }

        /// <summary>Get a rule by its id. Returns null if not found.</summary>
        public RuleEntry? GetById(string id)
        {
            _byId.TryGetValue(id, out var entry);
            return entry;
        }

        /// <summary>Get all rules matching the given type.</summary>
        public IEnumerable<RuleEntry> GetRulesByType(string type)
        {
            if (_byType.TryGetValue(type, out var list))
                return list;
            return Enumerable.Empty<RuleEntry>();
        }

        /// <summary>Get all loaded rules.</summary>
        public IReadOnlyList<RuleEntry> All => _entries;

        /// <summary>Total number of rules loaded.</summary>
        public int Count => _entries.Count;

        private static RuleEntry ConvertEntry(Dictionary<object, object> raw)
        {
            var entry = new RuleEntry
            {
                Id = GetString(raw, "id"),
                Section = GetString(raw, "section"),
                Title = GetString(raw, "title"),
                Type = GetString(raw, "type"),
                Description = GetString(raw, "description"),
                Condition = GetDict(raw, "condition"),
                Outcome = GetDict(raw, "outcome")
            };
            return entry;
        }

        private static string GetString(Dictionary<object, object> raw, string key)
        {
            if (raw.TryGetValue(key, out var val) && val != null)
                return val.ToString() ?? "";
            return "";
        }

        private static Dictionary<string, object>? GetDict(Dictionary<object, object> raw, string key)
        {
            if (!raw.TryGetValue(key, out var val) || val == null)
                return null;

            if (val is Dictionary<object, object> objDict)
            {
                return ConvertDict(objDict);
            }

            return null;
        }

        // Recursively convert Dictionary<object, object> to Dictionary<string, object>
        // and List<object> items as needed.
        private static Dictionary<string, object> ConvertDict(Dictionary<object, object> source)
        {
            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in source)
            {
                var k = kvp.Key?.ToString() ?? "";
                var v = NormalizeValue(kvp.Value);
                result[k] = v;
            }
            return result;
        }

        private static object NormalizeValue(object? value)
        {
            if (value == null)
                return "";

            if (value is Dictionary<object, object> d)
                return ConvertDict(d);

            if (value is List<object> list)
            {
                var normalized = new List<object>(list.Count);
                foreach (var item in list)
                {
                    normalized.Add(NormalizeValue(item));
                }
                return normalized;
            }

            return value;
        }
    }
}
