using System;
using System.Collections.Generic;
using Pinder.Core.Prompts;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Pinder.LlmAdapters
{
    /// <summary>
    /// Parses <c>data/persona/texting-style-conflicts.yaml</c> and returns
    /// the Core-owned validated conflict catalog.
    /// </summary>
    public static class TextingStyleConflictYamlLoader
    {
        public static TextingStyleConflicts LoadFrom(string yamlContent)
        {
            if (string.IsNullOrWhiteSpace(yamlContent))
                return TextingStyleConflicts.Empty;

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

            var rows = new List<(string AxisA, string ValueA, string AxisB, string ValueB, string Reason)>();
            var loaded = catalog?.Conflicts ?? new List<ConflictEntryDto>();
            for (int i = 0; i < loaded.Count; i++)
            {
                var dto = loaded[i];
                if (dto.AxisA == null)
                    throw new FormatException($"Conflict entry #{i + 1} is missing axis_a.");
                if (dto.AxisB == null)
                    throw new FormatException($"Conflict entry #{i + 1} is missing axis_b.");

                rows.Add((
                    dto.AxisA.Axis!,
                    dto.AxisA.Value!,
                    dto.AxisB.Axis!,
                    dto.AxisB.Value!,
                    dto.Reason!));
            }

            return TextingStyleConflicts.FromEntries(rows);
        }

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
