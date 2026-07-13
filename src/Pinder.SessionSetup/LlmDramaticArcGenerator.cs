using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Interfaces;
using Pinder.Core.Text;
using Pinder.LlmAdapters;

namespace Pinder.SessionSetup
{
    /// <summary>
    /// Default <see cref="IDramaticArcGenerator"/> built on
    /// <see cref="ILlmTransport"/>. One LLM call per session.
    /// Issue #821.
    /// </summary>
    /// <remarks>
    /// Uses the canonical <see cref="LlmPhase.DramaticArc"/> phase
    /// label so snapshot recording and audit decorators tag the exchange
    /// without re-deriving the phase from prompt text.
    /// </remarks>
    public sealed class LlmDramaticArcGenerator : IDramaticArcGenerator
    {
        private readonly ILlmTransport _transport;
        private readonly Options _options;
        private readonly PromptCatalog _catalog;

        public LlmDramaticArcGenerator(ILlmTransport transport, Options? options = null, PromptCatalog? catalog = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _options = options ?? new Options();
            _catalog = PromptCatalog.ResolveCatalogOrThrow(catalog);
            _catalog.RequireCompleteEntry(
                "dramatic_arc",
                "prompt-catalog: missing required key 'dramatic_arc'.");
        }

        /// <summary>
        /// Generates a light dramatic arc asynchronously.
        /// Incomplete outputs are retried and fail explicitly after the retry budget.
        /// Recoverable transport failures preserve the generator's existing degradation callback behavior.
        /// </summary>
        public async Task<string> GenerateAsync(
            string playerName,
            string playerStake,
            string playerBio,
            string dateeName,
            string dateeStake,
            string dateeBio,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(playerName))
                throw new ArgumentException("playerName must not be null or whitespace.", nameof(playerName));
            if (string.IsNullOrWhiteSpace(dateeName))
                throw new ArgumentException("dateeName must not be null or whitespace.", nameof(dateeName));
            // Stakes and bios are allowed to be empty/whitespace (character might not have them yet)

            var entry = _catalog.Get("dramatic_arc");
            string systemPrompt = entry.SystemPrompt!;
            string userTemplate = entry.UserTemplate!;

            string pStake = string.IsNullOrWhiteSpace(playerStake) ? "(none)" : playerStake;
            string pBio = string.IsNullOrWhiteSpace(playerBio) ? "(none)" : playerBio;
            string dStake = string.IsNullOrWhiteSpace(dateeStake) ? "(none)" : dateeStake;
            string dBio = string.IsNullOrWhiteSpace(dateeBio) ? "(none)" : dateeBio;

            var values = new Dictionary<string, string>
            {
                { "playerName", playerName },
                { "playerStake", pStake },
                { "playerBio", pBio },
                { "dateeName", dateeName },
                { "dateeStake", dStake },
                { "dateeBio", dBio }
            };

            systemPrompt = PromptCatalog.Substitute(systemPrompt, values);
            string userMessage = PromptCatalog.Substitute(userTemplate, values);

            double temperature = _options.Temperature != GeneratorDefaultConfigs.DramaticArc.Temperature
                ? _options.Temperature
                : entry.Temperature!.Value;
            int maxTokens = _options.MaxTokens != GeneratorDefaultConfigs.DramaticArc.MaxTokens
                ? _options.MaxTokens
                : entry.MaxTokens!.Value;

            string lastFailureCode = "invalid_output";
            for (int attempt = 1; attempt <= _options.MaxValidationAttempts; attempt++)
            {
                string sourceFile = entry.SourceFile ?? "data/prompts/dramatic_arc.yaml";
                InMemoryPromptTraceService.Instance.RecordTrace(
                    "dramatic-arc-system",
                    new PromptTraceResult(
                        systemPrompt,
                        new[] { new AnnotatedSpan(0, systemPrompt.Length, sourceFile, "dramatic_arc.system_prompt") }));
                InMemoryPromptTraceService.Instance.RecordTrace(
                    "dramatic-arc-user",
                    new PromptTraceResult(
                        userMessage,
                        new[] { new AnnotatedSpan(0, userMessage.Length, sourceFile, "dramatic_arc.user_template") }));

                string response;
                try
                {
                    response = await _transport
                        .SendAsync(
                            systemPrompt,
                            userMessage,
                            temperature,
                            maxTokens,
                            phase: LlmPhase.DramaticArc,
                            ct: cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (LlmTransportException)
                {
                    if (_options.OnDegraded != null)
                    {
                        _options.OnDegraded.Invoke(
                            SetupGenerationResult.DegradedFailure("dramatic_arc", "transport_error"));
                        return string.Empty;
                    }

                    throw;
                }

                string trimmed = (response ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(trimmed))
                {
                    lastFailureCode = "empty_output";
                    continue;
                }

                if (IsCompleteDramaticArc(trimmed))
                {
                    return trimmed;
                }

                lastFailureCode = "invalid_output";
            }

            _options.OnDegraded?.Invoke(
                SetupGenerationResult.DegradedFailure("dramatic_arc", lastFailureCode));
            throw new InvalidOperationException(
                $"dramatic_arc output failed validation after {_options.MaxValidationAttempts} attempts: " +
                "expected 3-5 complete sentences of plain prose.");
        }

        private static bool IsCompleteDramaticArc(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            int sentences = 0;
            bool inTerminatorRun = false;
            bool lastSignificantWasTerminator = false;

            foreach (char ch in text)
            {
                if (char.IsWhiteSpace(ch))
                    continue;

                bool isTerminator = ch == '.' || ch == '!' || ch == '?';
                if (isTerminator)
                {
                    if (!inTerminatorRun)
                        sentences++;
                    inTerminatorRun = true;
                    lastSignificantWasTerminator = true;
                }
                else if (lastSignificantWasTerminator && IsClosingDelimiter(ch))
                {
                    continue;
                }
                else
                {
                    inTerminatorRun = false;
                    lastSignificantWasTerminator = false;
                }
            }

            return lastSignificantWasTerminator && sentences >= 3 && sentences <= 5;
        }

        private static bool IsClosingDelimiter(char ch) =>
            ch == '\'' || ch == '"' || ch == ')' || ch == ']' || ch == '}' ||
            ch == '\u2019' || ch == '\u201D' || ch == '\u00BB';

        /// <summary>Tunable knobs for <see cref="LlmDramaticArcGenerator"/>.</summary>
        public sealed class Options
        {
            /// <summary>Temperature. Default 0.85 — creative but grounded.</summary>
            public double Temperature { get; set; } = GeneratorDefaultConfigs.DramaticArc.Temperature;

            /// <summary>Max tokens for dramatic arc generation.</summary>
            public int MaxTokens { get; set; } = GeneratorDefaultConfigs.DramaticArc.MaxTokens;

            /// <summary>Total attempts for incomplete dramatic-arc output before failing.</summary>
            public int MaxValidationAttempts { get; set; } = 3;

            /// <summary>
            /// Opt-in callback triggered when generation is degraded (e.g. recoverable transport failure or empty output).
            /// </summary>
            public Action<SetupGenerationResult>? OnDegraded { get; set; }
        }
    }
}
