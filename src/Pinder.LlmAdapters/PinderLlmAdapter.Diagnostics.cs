using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Text;

namespace Pinder.LlmAdapters
{
    public sealed partial class PinderLlmAdapter
    {
        private const string DateePrivatePhaseDirector = "director";
        private const string DateePrivatePhasePerformance = "performance";

        private static readonly string[] SafeDiagnosticMetadataKeys = new[]
        {
            "prompt_key",
            "system_prompt_source",
            "user_template_source",
            "compiled_input_sources",
            "compiled_input_keys",
            "prompt_trace_type",
            "prompt_trace_sources",
            "prompt_trace_keys",
        };

        private static Dictionary<string, string> BuildDateePerformanceMetadata(PromptTraceResult trace)
        {
            if (trace == null) throw new ArgumentNullException(nameof(trace));

            return BuildPromptTraceMetadata(
                new Dictionary<string, string>(StringComparer.Ordinal),
                trace,
                "datee");
        }

        private static Dictionary<string, string> BuildEmotionalDirectorMetadata(
            IReadOnlyDictionary<string, string> baseMetadata,
            PromptTraceResult systemPrompt)
        {
            if (baseMetadata == null) throw new ArgumentNullException(nameof(baseMetadata));
            if (systemPrompt == null) throw new ArgumentNullException(nameof(systemPrompt));

            return BuildPromptTraceMetadata(
                baseMetadata,
                systemPrompt,
                "emotional_director");
        }

        private static Dictionary<string, string> BuildPromptTraceMetadata(
            IReadOnlyDictionary<string, string> baseMetadata,
            PromptTraceResult trace,
            string traceType)
        {
            var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in baseMetadata)
                metadata[pair.Key] = pair.Value;

            metadata["prompt_trace_type"] = traceType;
            metadata["prompt_trace_sources"] = JoinTraceValues(trace, span => span.SourceFile);
            metadata["prompt_trace_keys"] = JoinTraceValues(trace, span => span.Key);

            return metadata;
        }

        private static string JoinTraceValues(
            PromptTraceResult trace,
            Func<AnnotatedSpan, string?> selector)
        {
            return string.Join(
                ",",
                trace.Spans
                    .Select(selector)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal));
        }

        private static Dictionary<string, string> BuildDiagnosticHints(
            string phase,
            int? turnId,
            int? attempt,
            int? totalAttempts,
            string? dateePrivatePhase,
            IReadOnlyDictionary<string, string>? metadata)
        {
            var hints = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["phase"] = phase ?? string.Empty,
            };

            if (turnId.HasValue)
            {
                hints["turn"] = turnId.Value.ToString(CultureInfo.InvariantCulture);
            }

            if (attempt.HasValue)
            {
                hints["attempt"] = attempt.Value.ToString(CultureInfo.InvariantCulture);
            }

            if (totalAttempts.HasValue)
            {
                hints["total_attempts"] = totalAttempts.Value.ToString(CultureInfo.InvariantCulture);
            }

            if (!string.IsNullOrWhiteSpace(dateePrivatePhase))
            {
                hints["datee_private_phase"] = dateePrivatePhase!;
            }

            CopySafeDiagnosticMetadata(hints, metadata);
            return hints;
        }

        private static void CopySafeDiagnosticMetadata(
            IDictionary<string, string> hints,
            IReadOnlyDictionary<string, string>? metadata)
        {
            if (metadata == null)
            {
                return;
            }

            foreach (string key in SafeDiagnosticMetadataKeys)
            {
                if (metadata.TryGetValue(key, out string value)
                    && !string.IsNullOrWhiteSpace(value))
                {
                    hints[key] = value;
                }
            }
        }

        private static Dictionary<string, string> CloneHints(
            IReadOnlyDictionary<string, string> hints)
        {
            var copy = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in hints)
            {
                copy[pair.Key] = pair.Value;
            }

            return copy;
        }

        private static void AddStructuredResponseHints(
            IDictionary<string, string> hints,
            StructuredLlmResponse response)
        {
            if (response == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(response.Provider))
            {
                hints["provider"] = response.Provider!;
            }

            if (!string.IsNullOrWhiteSpace(response.Model))
            {
                hints["model"] = response.Model!;
            }

            hints["structured_output_mode"] = response.UsedNativeStructuredOutput
                ? "native"
                : "local_validation";
            hints["validation_mode"] = response.ValidationMode;
        }

        private static void AddElapsedHint(
            IDictionary<string, string> hints,
            Stopwatch stopwatch)
        {
            stopwatch.Stop();
            long elapsedMs = Math.Max(0, stopwatch.ElapsedMilliseconds);
            hints["elapsed_ms"] = elapsedMs.ToString(CultureInfo.InvariantCulture);
        }

        private static void AddExceptionTypeHint(
            IDictionary<string, string> hints,
            Exception exception)
        {
            if (exception == null)
            {
                return;
            }

            hints["exception_type"] = exception.GetType().Name;
            if (exception is LlmTransportException transportException)
            {
                hints["failure_kind"] = transportException.FailureKind.ToString();
            }
        }

        private static bool ShouldSuppressDiagnosticException(
            string phase,
            string? dateePrivatePhase)
        {
            return !string.IsNullOrWhiteSpace(dateePrivatePhase)
                || string.Equals(phase, LlmPhase.EmotionalDirector, StringComparison.Ordinal)
                || string.Equals(phase, LlmPhase.OpponentResponse, StringComparison.Ordinal);
        }

        private static TokenUsageSnapshot CaptureTokenUsageSnapshot(object transport)
        {
            try
            {
                var provider = transport as ITokenUsageProvider;
                if (provider == null)
                {
                    return TokenUsageSnapshot.Unavailable;
                }

                SessionTokenUsage usage = provider.GetSessionUsage();
                if (usage == null)
                {
                    return TokenUsageSnapshot.Unavailable;
                }

                return new TokenUsageSnapshot(
                    true,
                    usage.InputTokens,
                    usage.OutputTokens,
                    usage.CacheReadInputTokens,
                    usage.CacheCreationInputTokens,
                    usage.CallCount);
            }
            catch
            {
                return TokenUsageSnapshot.Unavailable;
            }
        }

        private static void AddTokenUsageHints(
            IDictionary<string, string> hints,
            TokenUsageSnapshot before,
            TokenUsageSnapshot after)
        {
            if (!before.IsAvailable || !after.IsAvailable)
            {
                hints["token_source"] = "unavailable";
                return;
            }

            hints["token_source"] = "ITokenUsageProvider.session_delta";
            hints["input_tokens"] = NonNegativeDelta(after.InputTokens, before.InputTokens).ToString(CultureInfo.InvariantCulture);
            hints["output_tokens"] = NonNegativeDelta(after.OutputTokens, before.OutputTokens).ToString(CultureInfo.InvariantCulture);
            hints["cache_read_input_tokens"] = NonNegativeDelta(after.CacheReadInputTokens, before.CacheReadInputTokens).ToString(CultureInfo.InvariantCulture);
            hints["cache_creation_input_tokens"] = NonNegativeDelta(after.CacheCreationInputTokens, before.CacheCreationInputTokens).ToString(CultureInfo.InvariantCulture);
            hints["call_count_delta"] = NonNegativeDelta(after.CallCount, before.CallCount).ToString(CultureInfo.InvariantCulture);
        }

        private static int NonNegativeDelta(int after, int before)
        {
            return Math.Max(0, after - before);
        }

        private struct TokenUsageSnapshot
        {
            public static readonly TokenUsageSnapshot Unavailable =
                new TokenUsageSnapshot(false, 0, 0, 0, 0, 0);

            public TokenUsageSnapshot(
                bool isAvailable,
                int inputTokens,
                int outputTokens,
                int cacheReadInputTokens,
                int cacheCreationInputTokens,
                int callCount)
            {
                IsAvailable = isAvailable;
                InputTokens = inputTokens;
                OutputTokens = outputTokens;
                CacheReadInputTokens = cacheReadInputTokens;
                CacheCreationInputTokens = cacheCreationInputTokens;
                CallCount = callCount;
            }

            public bool IsAvailable { get; }
            public int InputTokens { get; }
            public int OutputTokens { get; }
            public int CacheReadInputTokens { get; }
            public int CacheCreationInputTokens { get; }
            public int CallCount { get; }
        }
    }
}
