using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Pinder.Core.Characters
{
    /// <summary>
    /// Central policy for anatomy identifiers that existed in old persisted data
    /// but must not be emitted or accepted by new writes.
    /// </summary>
    public static class DeprecatedAnatomyFields
    {
        private static readonly string[] DeprecatedExpressionTargetIds =
        {
            "sad",
            "happy",
            "serius",
        };

        private static readonly HashSet<string> DeprecatedExpressionTargetSet =
            new HashSet<string>(DeprecatedExpressionTargetIds, StringComparer.Ordinal);

        public static IReadOnlyList<string> ExpressionTargetIds => DeprecatedExpressionTargetIds;

        public static bool IsDeprecated(string fieldName)
        {
            return DeprecatedExpressionTargetSet.Contains(fieldName);
        }

        public static void StripForLegacyRead(IDictionary<string, float> anatomy)
        {
            if (anatomy == null) throw new ArgumentNullException(nameof(anatomy));

            foreach (string field in DeprecatedExpressionTargetIds)
                anatomy.Remove(field);
        }

        public static void ThrowIfPresentForWrite(
            IReadOnlyDictionary<string, float> anatomy,
            string sourceDescription = "Character definition")
        {
            if (anatomy == null) throw new ArgumentNullException(nameof(anatomy));

            foreach (string field in DeprecatedExpressionTargetIds)
            {
                if (anatomy.ContainsKey(field))
                    ThrowDeprecated(sourceDescription, field);
            }
        }

        public static void ThrowIfJsonRootContainsDeprecatedForWrite(
            JsonElement root,
            string sourceDescription)
        {
            if (root.ValueKind != JsonValueKind.Object)
                return;

            if (!root.TryGetProperty("anatomy", out var anatomy)
                || anatomy.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            foreach (var property in anatomy.EnumerateObject())
            {
                if (IsDeprecated(property.Name))
                    ThrowDeprecated(sourceDescription, property.Name);
            }
        }

        private static void ThrowDeprecated(string sourceDescription, string field)
        {
            throw new FormatException(
                $"{sourceDescription} field anatomy.{field} is deprecated and must not be written.");
        }
    }
}
