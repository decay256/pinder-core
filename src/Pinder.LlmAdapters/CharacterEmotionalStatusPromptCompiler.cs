using System;
using System.Collections.Generic;
using System.Globalization;
using Pinder.Core.Text;

namespace Pinder.LlmAdapters
{
    internal static class CharacterEmotionalStatusPromptCompiler
    {
        private const string ContextKey = "character-emotional-status-context";
        private const string UnavailableKey = "character-emotional-status-unavailable";

        public static PromptTraceResult Compile(
            PromptCatalog catalog,
            string subjectName,
            int? subjectHfi,
            int? subjectTor,
            string counterpartName,
            int? counterpartHfi,
            int? counterpartTor)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (!subjectHfi.HasValue || !subjectTor.HasValue
                || !counterpartHfi.HasValue || !counterpartTor.HasValue)
            {
                return EntryTrace(catalog, UnavailableKey);
            }

            PromptEntry context = RequireSystemPrompt(catalog, ContextKey);
            var values = new Dictionary<string, PromptTraceResult>(StringComparer.Ordinal)
            {
                ["subject_name"] = RuntimeTrace(subjectName, "SubjectName"),
                ["subject_hfi"] = RuntimeTrace(subjectHfi.Value.ToString(CultureInfo.InvariantCulture), "SubjectHfi"),
                ["subject_hfi_meaning"] = EntryTrace(catalog, HfiMeaningKey(subjectHfi.Value)),
                ["subject_tor"] = RuntimeTrace(subjectTor.Value.ToString(CultureInfo.InvariantCulture), "SubjectTor"),
                ["subject_tor_meaning"] = EntryTrace(catalog, TorMeaningKey(subjectTor.Value)),
                ["counterpart_name"] = RuntimeTrace(counterpartName, "CounterpartName"),
                ["counterpart_hfi"] = RuntimeTrace(counterpartHfi.Value.ToString(CultureInfo.InvariantCulture), "CounterpartHfi"),
                ["counterpart_hfi_meaning"] = EntryTrace(catalog, HfiMeaningKey(counterpartHfi.Value)),
                ["counterpart_tor"] = RuntimeTrace(counterpartTor.Value.ToString(CultureInfo.InvariantCulture), "CounterpartTor"),
                ["counterpart_tor_meaning"] = EntryTrace(catalog, TorMeaningKey(counterpartTor.Value)),
            };
            return Render(context, ContextKey, values);
        }

        private static string HfiMeaningKey(int value)
            => value < 10 ? "character-emotional-hfi-low" : "character-emotional-hfi-high";

        private static string TorMeaningKey(int value)
            => value < 10 ? "character-emotional-tor-low" : "character-emotional-tor-high";

        private static PromptTraceResult EntryTrace(PromptCatalog catalog, string key)
        {
            PromptEntry entry = RequireSystemPrompt(catalog, key);
            string text = entry.SystemPrompt!;
            return new PromptTraceResult(
                text,
                new[] { new AnnotatedSpan(0, text.Length, entry.SourceFile, key) });
        }

        private static PromptTraceResult RuntimeTrace(string value, string key)
            => new PromptTraceResult(
                value ?? string.Empty,
                new[] { new AnnotatedSpan(0, (value ?? string.Empty).Length, PromptTraceDiagnosticContract.CharacterEmotionalStatusRuntimeSource, key) });

        private static PromptTraceResult Render(
            PromptEntry entry,
            string key,
            IReadOnlyDictionary<string, PromptTraceResult> values)
        {
            string template = entry.SystemPrompt!;
            var builder = new AnnotatedStringBuilder();
            int cursor = 0;
            while (cursor < template.Length)
            {
                KeyValuePair<string, PromptTraceResult>? next = null;
                int nextIndex = template.Length;
                foreach (KeyValuePair<string, PromptTraceResult> value in values)
                {
                    string placeholder = "{" + value.Key + "}";
                    int index = template.IndexOf(placeholder, cursor, StringComparison.Ordinal);
                    if (index >= 0 && index < nextIndex)
                    {
                        next = value;
                        nextIndex = index;
                    }
                }

                if (!next.HasValue)
                {
                    builder.Append(template.Substring(cursor), entry.SourceFile, key);
                    break;
                }

                string token = "{" + next.Value.Key + "}";
                builder.Append(template.Substring(cursor, nextIndex - cursor), entry.SourceFile, key);
                builder.Append(next.Value.Value);
                cursor = nextIndex + token.Length;
            }

            foreach (string valueKey in values.Keys)
            {
                if (template.IndexOf("{" + valueKey + "}", StringComparison.Ordinal) < 0)
                    throw new InvalidOperationException(ContextKey + " is missing {" + valueKey + "}.");
            }
            return new PromptTraceResult(builder.ToString(), builder.Spans);
        }

        private static PromptEntry RequireSystemPrompt(PromptCatalog catalog, string key)
        {
            PromptEntry? entry = catalog.TryGet(key);
            if (entry == null || string.IsNullOrWhiteSpace(entry.SystemPrompt))
                throw new InvalidOperationException("prompt-catalog: missing required emotional status prompt '" + key + "'.");
            return entry;
        }
    }
}
