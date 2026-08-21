using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Diagnostics.AgentJournals;
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
            AgentJournalOneShotContext? agentJournal = ResolveAgentJournalContext();
            ValidateOneShotJournalConfiguration(agentJournal);

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

            GameRunPromptDocumentPair documents = GameRunPromptDocumentBuilder.BuildDramaticArcDocuments(entry, values);
            systemPrompt = documents.System.Text;
            string userMessage = documents.User.Text;

            double temperature = _options.Temperature != GeneratorDefaultConfigs.DramaticArc.Temperature
                ? _options.Temperature
                : entry.Temperature!.Value;
            int maxTokens = _options.MaxTokens != GeneratorDefaultConfigs.DramaticArc.MaxTokens
                ? _options.MaxTokens
                : entry.MaxTokens!.Value;

            if (_options.MaxValidationAttempts <= 0)
            {
                _options.OnDegraded?.Invoke(
                    SetupGenerationResult.DegradedFailure("dramatic_arc", "invalid_output"));
                throw new InvalidOperationException(
                    $"dramatic_arc output failed validation after {_options.MaxValidationAttempts} attempts: " +
                    "expected 3-5 complete sentences of plain prose.");
            }

            var recovery = await SemanticOutputRecoveryExecutor.ExecuteAsync<string, DramaticArcRejection>(
                _options.MaxValidationAttempts,
                async (attempt, attemptCancellationToken) =>
                {
                    AgentJournalAttempt? journalAttempt = await StartOneShotJournalAsync(
                            agentJournal,
                            documents,
                            attempt,
                            attemptCancellationToken)
                        .ConfigureAwait(false);
                    var usageMeasurement = TokenUsageMeasurement.Start(_transport);
                    string trimmed;
                    bool isComplete;
                    try
                    {
                        string response = await LlmOptionalTextGeneration.RunAsync(
                                "dramatic_arc",
                                _transport,
                                systemPrompt,
                                userMessage,
                                entry,
                                LlmPhase.DramaticArc,
                                temperature,
                                GeneratorDefaultConfigs.DramaticArc.Temperature,
                                maxTokens,
                                GeneratorDefaultConfigs.DramaticArc.MaxTokens,
                                onDegraded: null,
                                _options.OnDiagnostic,
                                LlmOptionalTextGeneration.CancellationBehavior.Throw,
                                attemptCancellationToken,
                                passCancellationTokenToTransport: true,
                                callId: journalAttempt?.InvocationRecord.Correlation.InvocationId)
                            .ConfigureAwait(false);
                        trimmed = (response ?? string.Empty).Trim();
                        isComplete = IsCompleteDramaticArc(trimmed);
                    }
                    catch (OperationCanceledException)
                    {
                        await CompleteCancelledOneShotAsync(journalAttempt, usageMeasurement).ConfigureAwait(false);
                        throw;
                    }
                    catch (LlmTransportException ex)
                    {
                        await CompleteProviderFailedOneShotAsync(journalAttempt, ex, usageMeasurement).ConfigureAwait(false);
                        if (_options.OnDegraded != null)
                        {
                            _options.OnDegraded.Invoke(
                                SetupGenerationResult.DegradedFailure("dramatic_arc", "transport_error"));
                            return SemanticOutputRecoveryAttemptResult<string, DramaticArcRejection>.Accepted(string.Empty);
                        }

                        throw;
                    }
                    catch (Exception ex)
                    {
                        await CompleteProviderFailedOneShotAsync(journalAttempt, ex, usageMeasurement).ConfigureAwait(false);
                        throw;
                    }

                    if (string.IsNullOrEmpty(trimmed))
                    {
                        await CompleteValidationRejectedOneShotAsync(journalAttempt, "empty_output", usageMeasurement)
                            .ConfigureAwait(false);
                        return SemanticOutputRecoveryAttemptResult<string, DramaticArcRejection>.Rejected(
                            new DramaticArcRejection("empty_output"));
                    }

                    if (isComplete)
                    {
                        await CompleteAcceptedOneShotAsync(journalAttempt, trimmed, usageMeasurement).ConfigureAwait(false);
                        return SemanticOutputRecoveryAttemptResult<string, DramaticArcRejection>.Accepted(trimmed);
                    }

                    await CompleteValidationRejectedOneShotAsync(journalAttempt, "invalid_output", usageMeasurement)
                        .ConfigureAwait(false);
                    return SemanticOutputRecoveryAttemptResult<string, DramaticArcRejection>.Rejected(
                        new DramaticArcRejection("invalid_output"));
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (recovery.IsAccepted)
            {
                return recovery.AcceptedValue;
            }

            _options.OnDegraded?.Invoke(
                SetupGenerationResult.DegradedFailure("dramatic_arc", recovery.Exhaustion.FinalRejection.FailureCode));
            throw new InvalidOperationException(
                $"dramatic_arc output failed validation after {_options.MaxValidationAttempts} attempts: " +
                "expected 3-5 complete sentences of plain prose.");
        }

        private async Task<AgentJournalAttempt?> StartOneShotJournalAsync(
            AgentJournalOneShotContext? agentJournal,
            GameRunPromptDocumentPair documents,
            int attemptOrdinal,
            CancellationToken cancellationToken)
        {
            ValidateOneShotJournalConfiguration(agentJournal);
            if (_options.AgentJournalHostSink == null)
            {
                return null;
            }

            if (agentJournal == null)
            {
                throw new InvalidOperationException(
                    "Agent journal host sink was configured for dramatic-arc setup, but no AgentJournalOneShotContext was supplied.");
            }

            var recorderContext = new AgentJournalRecorderContext(
                agentJournal.ToCorrelation(
                    attemptOrdinal,
                    OperationalDiagnostics.CreateCallId()),
                agentJournal.ModelId,
                LlmPhase.DramaticArc,
                new[]
                {
                    documents.System.ToAgentJournalInputDocument(),
                    documents.User.ToAgentJournalInputDocument(),
                })
            {
                HostSink = _options.AgentJournalHostSink,
                SinkFailureMode = _options.AgentJournalSinkFailureMode,
                OnDiagnostic = _options.OnDiagnostic,
                Clock = _options.AgentJournalClock,
            };

            return await new AgentJournalRecorder(recorderContext).StartAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        private AgentJournalOneShotContext? ResolveAgentJournalContext()
        {
            if (_options.AgentJournal != null)
            {
                return _options.AgentJournal;
            }
            if (_options.AgentJournalOneShotContextFactory == null)
            {
                return null;
            }

            return _options.AgentJournalOneShotContextFactory.Create(new GameRunOneShotJournalRequest(
                GameRunOneShotJournalTaxonomy.DramaticArcSetup,
                GameRunOneShotJournalTaxonomy.DramaticArcSetup,
                GameRunOneShotJournalTaxonomy.GameRunSetupOneShotRecord,
                turnId: null,
                outputLinkId: "setup.dramatic-arc.output",
                requestId: "setup.dramatic-arc.request",
                context: new Dictionary<string, string> { ["setup_stage"] = "dramatic_arc" },
                invocationIdPrefix: "setup.dramatic-arc.invocation"));
        }

        private void ValidateOneShotJournalConfiguration(AgentJournalOneShotContext? agentJournal)
        {
            if (_options.AgentJournalHostSink != null && agentJournal == null)
            {
                throw new InvalidOperationException(
                    "Agent journal host sink was configured for a Game Run one-shot path, but no AgentJournalOneShotContext was supplied.");
            }
        }

        private static async Task CompleteAcceptedOneShotAsync(
            AgentJournalAttempt? attempt,
            string outputText,
            TokenUsageMeasurement usageMeasurement)
        {
            if (attempt != null)
            {
                AgentJournalUsageCapture capture = AgentJournalUsageCapture.Capture(usageMeasurement);
                await attempt.CompleteAcceptedAsync(
                    outputText,
                    capture.Usage,
                    usageStatus: capture.Status).ConfigureAwait(false);
            }
        }

        private static async Task CompleteValidationRejectedOneShotAsync(
            AgentJournalAttempt? attempt,
            string validationCode,
            TokenUsageMeasurement usageMeasurement)
        {
            if (attempt != null)
            {
                AgentJournalUsageCapture capture = AgentJournalUsageCapture.Capture(usageMeasurement);
                await attempt.CompleteValidationRejectedAsync(
                    validationCode,
                    capture.Usage,
                    capture.Status).ConfigureAwait(false);
            }
        }

        private static async Task CompleteCancelledOneShotAsync(
            AgentJournalAttempt? attempt,
            TokenUsageMeasurement usageMeasurement)
        {
            if (attempt != null)
            {
                AgentJournalUsageCapture capture = AgentJournalUsageCapture.Capture(usageMeasurement);
                await attempt.CompleteCancelledAsync(
                    AgentJournalTerminalCodes.Cancelled,
                    usage: capture.Usage,
                    usageStatus: capture.Status).ConfigureAwait(false);
            }
        }

        private static async Task CompleteProviderFailedOneShotAsync(
            AgentJournalAttempt? attempt,
            Exception exception,
            TokenUsageMeasurement usageMeasurement)
        {
            if (attempt != null)
            {
                AgentJournalUsageCapture capture = AgentJournalUsageCapture.Capture(usageMeasurement);
                await attempt.CompleteProviderFailedAsync(
                    exception.GetType().Name,
                    capture.Usage,
                    capture.Status).ConfigureAwait(false);
            }
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

            /// <summary>
            /// Opt-in operational diagnostic sink. Null keeps diagnostics disabled.
            /// </summary>
            public Action<OperationalDiagnosticEvent>? OnDiagnostic { get; set; }


            /// <summary>
            /// Optional host-owned durable sink for no-session Game Run setup one-shot records.
            /// </summary>
            public IAgentJournalSink? AgentJournalHostSink { get; set; }

            /// <summary>
            /// Persistence policy for the host-owned one-shot journal sink. Production callers should fail closed.
            /// </summary>
            public AgentJournalSinkFailureMode AgentJournalSinkFailureMode { get; set; } = AgentJournalSinkFailureMode.FailClosed;

            public AgentJournalOneShotContext? AgentJournal { get; set; }

            public IAgentJournalOneShotContextFactory? AgentJournalOneShotContextFactory { get; set; }

            public Func<DateTimeOffset>? AgentJournalClock { get; set; }
        }

        private sealed class DramaticArcRejection
        {
            public DramaticArcRejection(string failureCode)
            {
                FailureCode = failureCode;
            }

            public string FailureCode { get; }
        }
    }
}
