using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;
using Pinder.Core.Text;
using Pinder.LlmAdapters.Anthropic;

namespace Pinder.LlmAdapters
{
    /// <summary>
    /// Provider-agnostic implementation of ILlmAdapter and its stateful session extensions.
    /// All game-level prompt building and response parsing lives here — single source of truth.
    /// Delegates raw LLM I/O to an ILlmTransport (AnthropicTransport, OpenAiTransport, etc.).
    ///
    /// This replaces the need for every transport to duplicate game logic.
    /// Plain transports accept a single user message; conversation transports
    /// additionally accept ordered typed prior messages. Provider wire formats
    /// remain below the transport boundary.
    /// </summary>
    public sealed partial class PinderLlmAdapter : ISessionStatefulLlmAdapter, IDisposable
    {
        private const string HorninessOverlayPrompt = "horniness_overlay";
        private const string TrapOverlayPrompt = "trap_overlay";
        private const string FailureCorruptionPrompt = "failure_corruption";
        private const string ShadowCorruptionPrompt = "shadow_corruption";
        private const string OverlayProviderPrimary = "primary";
        private const string OverlayReasonSkippedNoInstruction = "skipped_no_instruction";
        private const string OverlayReasonEmptyOutput = "empty_output";
        private const string OverlayReasonRefusal = "refusal";
        private const string OverlayReasonError = "error";
        private const string RefusalPrefixCant = "I can't";
        private const string RefusalPrefixCannot = "I cannot";
        private const string RefusalPhraseInappropriate = "inappropriate";
        private const string RefusalPhraseHappyToHelp = "I'd be happy to help";

        private readonly ILlmTransport _transport;
        private readonly ILlmTransport _overlayTransport;
        private readonly PinderLlmAdapterOptions _options;
        private readonly PinderLlmAdapterTemperatureSource _temperatures;

        public bool SupportsConversationSessions
            => _transport is IConversationLlmTransport contextual
                && contextual.SupportsConversationMessages
                && (!(_transport is IStructuredLlmTransport)
                    || (_transport is IStructuredConversationLlmTransport structuredContextual
                        && structuredContextual.SupportsStructuredConversationMessages));

        // #788: datee conversation state lives on GameSession, not here.
        // The adapter is pure-stateless and safe for concurrent reuse across sessions.

        /// <summary>
        /// Initializes a new instance of the <see cref="PinderLlmAdapter"/> class.
        /// </summary>
        /// <param name="transport">The primary LLM transport.</param>
        /// <param name="options">The adapter configuration options.</param>
        /// <param name="overlayTransport">The optional secondary transport for overlay rewriting.</param>
        /// <remarks>
        /// When null, overlay calls (ApplyHorninessOverlayAsync/ApplyTrapOverlayAsync/ApplyShadowCorruptionAsync) use the same transport as primary game-turn calls. Pass a distinct transport (built the same way as the primary transport, via whatever factory the host application uses to resolve provider-qualified model specs) to route overlay rewrites to a different/cheaper model. Overlay routing must never be selected by vendor-specific fields on PinderLlmAdapterOptions — the transport instance is the only routing mechanism.
        /// </remarks>
        public PinderLlmAdapter(ILlmTransport transport, PinderLlmAdapterOptions options, ILlmTransport? overlayTransport = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _overlayTransport = overlayTransport ?? transport;
            _temperatures = new PinderLlmAdapterTemperatureSource(_options);
        }

        // ── ILlmAdapter ────────────────────────────────────────────────────

        /// <inheritdoc />
        public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context, CancellationToken ct = default)
            => GetDialogueOptionsCoreAsync(context, priorMessages: null, ct);

        public async Task<DialogueOption[]> GetDialogueOptionsAsync(
            DialogueContext context,
            IReadOnlyList<ConversationMessage> avatarHistory,
            LlmConversationSessionSnapshot? avatarSession,
            CancellationToken cancellationToken = default)
        {
            await using PiConversationSession session = await PiConversationSession.RestoreOrImportAsync(
                avatarSession,
                avatarHistory,
                "avatar").ConfigureAwait(false);
            IReadOnlyList<ConversationMessage> priorMessages = await session.BuildSemanticHistoryAsync()
                .ConfigureAwait(false);
            return await GetDialogueOptionsCoreAsync(context, priorMessages, cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task<DialogueOption[]> GetDialogueOptionsCoreAsync(
            DialogueContext context,
            IReadOnlyList<ConversationMessage>? priorMessages,
            CancellationToken ct)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var gameDef = RequireGameDefinition();
            string userContent = priorMessages == null
                ? SessionDocumentBuilder.BuildDialogueOptionsPrompt(context, _options.PromptCatalog)
                : SessionDocumentBuilder.BuildDialogueOptionsSessionPromptEx(context, _options.PromptCatalog).Text;
            var systemPrompt = SessionSystemPromptBuilder.BuildPlayerAvatar(context.PlayerAvatarPrompt, gameDef);
            double temperature = _temperatures.For(PinderLlmAdapterPhase.DialogueOptions);

            int maxAttempts = GetContractViolationAttemptLimit();
            var recovery = await SemanticOutputRecoveryExecutor.ExecuteAsync<DialogueOption[], LlmContractException>(
                maxAttempts,
                async (attempt, attemptCancellationToken) =>
                {
                    try
                    {
                        DialogueOption[] parsedOptions;
                        if (_transport is IStructuredLlmTransport structuredTransport)
                        {
                            var request = DialogueOptionsStructuredContract.CreateRequest(
                                systemPrompt,
                                userContent,
                                temperature,
                                _options.MaxTokens,
                                context,
                                GetExpectedDialogueOptionCount(context, gameDef));
                            var structuredResponse = await SendStructuredWithDiagnosticsAsync(
                                    structuredTransport,
                                    request,
                                    LlmPhase.DialogueOptions,
                                    context.CurrentTurn,
                                    attemptCancellationToken,
                                    priorMessages: priorMessages)
                                .ConfigureAwait(false);
                            try
                            {
                                if (structuredResponse.UsedNativeStructuredOutput)
                                {
                                    parsedOptions = DialogueOptionsStructuredContract.ParseStrict(
                                        structuredResponse.JsonText,
                                        context.AvailableStats,
                                        gameDef.MaxDialogueOptions,
                                        out string? errorCode,
                                        out string? errorMessage,
                                        out int parsedCount,
                                        out int expectedCount);

                                    if (errorCode != null)
                                    {
                                        throw CreateDialogueOptionsContractException(
                                            errorCode,
                                            errorMessage!,
                                            "StructuredDialogueOptionsParser",
                                            expectedCount,
                                            parsedCount,
                                            context.CurrentTurn,
                                            structuredResponse.Provider,
                                            structuredResponse.Model);
                                    }
                                }
                                else
                                {
                                    parsedOptions = ParseDialogueOptionsFromTextOrJson(
                                        structuredResponse.JsonText,
                                        context,
                                        gameDef);
                                }

                                structuredResponse.ReportValidation("accepted");
                            }
                            catch (LlmContractException ex)
                            {
                                structuredResponse.ReportValidation("rejected", ex.Reason);
                                throw;
                            }
                            catch (Exception ex)
                            {
                                structuredResponse.ReportValidation("rejected", ex.GetType().Name);
                                throw;
                            }
                        }
                        else
                        {
                            var responseText = await SendWithDiagnosticsAsync(
                                    _transport,
                                    systemPrompt,
                                    userContent,
                                    temperature,
                                    _options.MaxTokens,
                                    LlmPhase.DialogueOptions,
                                    context.CurrentTurn,
                                    attemptCancellationToken,
                                    priorMessages: priorMessages)
                                .ConfigureAwait(false);

                            parsedOptions = ParseDialogueOptionsFromTextOrJson(
                                responseText,
                                context,
                                gameDef);
                        }

                        // #950: warn when the option generator skips all stake content.
                        // Lightweight check: split stake lines on sentence/clause boundaries,
                        // discard fragments shorter than 8 chars, look for any fragment in any option.
                        if (context.StakeLines != null && context.StakeLines.Length > 0 && parsedOptions.Length > 0)
                        {
                            WarnIfStakeSkipped(context, parsedOptions);
                        }

                        return SemanticOutputRecoveryAttemptResult<DialogueOption[], LlmContractException>.Accepted(parsedOptions);
                    }
                    catch (LlmContractException ex)
                    {
                        return SemanticOutputRecoveryAttemptResult<DialogueOption[], LlmContractException>.Rejected(ex);
                    }
                },
                delayAfterRejectedAttempt: attempt => TimeSpan.FromMilliseconds(
                    GetContractViolationBackoffDelayMs(_options.ContractViolationBackoffMs, attempt)),
                onRejected: rejection => NotifyContractViolation(rejection.Rejection),
                cancellationToken: ct).ConfigureAwait(false);

            if (recovery.IsAccepted)
            {
                return recovery.AcceptedValue;
            }

            ExceptionDispatchInfo.Capture(recovery.Exhaustion.FinalRejection).Throw();
            throw recovery.Exhaustion.FinalRejection;
        }

        /// <inheritdoc />
        public async Task<DateeResponse> GetDateeResponseAsync(DateeContext context, CancellationToken ct = default)
        {
            RequireGameDefinition();
            // #788: stateless single-turn fallback path. Stateful callers route
            // through the IStatefulLlmAdapter overload that takes a history.
            var result = await GetDateeResponseAsync(context, System.Array.Empty<ConversationMessage>(), ct).ConfigureAwait(false);
            return result.Response;
        }

        /// <inheritdoc />
        public Task<StatefulDateeResult> GetDateeResponseAsync(
            DateeContext context,
            IReadOnlyList<ConversationMessage> history,
            CancellationToken cancellationToken = default)
            => GetDateeResponseCoreAsync(
                context,
                history,
                priorMessages: null,
                dateeSession: null,
                cancellationToken);

        public async Task<StatefulDateeResult> GetDateeResponseAsync(
            DateeContext context,
            IReadOnlyList<ConversationMessage> dateeHistory,
            IReadOnlyList<ConversationMessage> avatarHistory,
            LlmConversationSessionSnapshot? dateeSession,
            LlmConversationSessionSnapshot? avatarSession,
            CancellationToken cancellationToken = default)
        {
            await using PiConversationSession datee = await PiConversationSession.RestoreOrImportAsync(
                dateeSession, dateeHistory, "datee").ConfigureAwait(false);
            await using PiConversationSession avatar = await PiConversationSession.RestoreOrImportAsync(
                avatarSession, avatarHistory, "avatar").ConfigureAwait(false);

            IReadOnlyList<ConversationMessage> priorMessages = await datee.BuildSemanticHistoryAsync()
                .ConfigureAwait(false);
            StatefulDateeResult accepted = await GetDateeResponseCoreAsync(
                context, dateeHistory, priorMessages, datee, cancellationToken).ConfigureAwait(false);

            await datee.AppendUserAsync(context.PlayerDeliveredMessage).ConfigureAwait(false);
            await datee.AppendAssistantAsync(accepted.Response.MessageText).ConfigureAwait(false);
            await avatar.AppendAssistantAsync(context.PlayerDeliveredMessage).ConfigureAwait(false);
            await avatar.AppendUserAsync(accepted.Response.MessageText).ConfigureAwait(false);

            return new StatefulDateeResult(
                accepted.Response,
                accepted.NewHistoryEntries,
                await datee.SnapshotAsync().ConfigureAwait(false),
                await avatar.SnapshotAsync().ConfigureAwait(false));
        }

        private async Task<StatefulDateeResult> GetDateeResponseCoreAsync(
            DateeContext context,
            IReadOnlyList<ConversationMessage> history,
            IReadOnlyList<ConversationMessage>? priorMessages,
            PiConversationSession? dateeSession,
            CancellationToken cancellationToken)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (history == null) throw new ArgumentNullException(nameof(history));

            var gameDef = RequireGameDefinition();
            if (context.EmotionalTurnEvent == null)
            {
                throw new InvalidOperationException(
                    "DateeContext.EmotionalTurnEvent is required for the production DATEE response path.");
            }

            var emotionalPromptCompiler = new EmotionalPromptCompiler(
                PromptCatalog.ResolveCatalogOrThrow(_options.PromptCatalog));
            var systemPrompt = SessionSystemPromptBuilder.BuildDatee(context.DateePrompt, gameDef);
            EmotionalDirectorDirection emotionalDirection;
            if (dateeSession != null)
            {
                await using PiConversationBranch directorBranch = await dateeSession.ForkAsync(
                    "datee-private-analysis").ConfigureAwait(false);
                IReadOnlyList<ConversationMessage> directorHistory =
                    await directorBranch.BuildSemanticHistoryAsync().ConfigureAwait(false);
                emotionalDirection = await GenerateEmotionalDirectionAsync(
                        context,
                        emotionalPromptCompiler,
                        directorHistory,
                        systemPrompt,
                        directorBranch,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                emotionalDirection = await GenerateEmotionalDirectionAsync(
                        context,
                        emotionalPromptCompiler,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            PromptTraceResult dateePrompt = emotionalPromptCompiler.CompilePerformance(
                context,
                emotionalDirection,
                includeConversationHistory: priorMessages == null);
            InMemoryPromptTraceService.Instance.RecordTrace("datee", dateePrompt);
            var userContent = dateePrompt.Text;
            double temperature = _temperatures.For(PinderLlmAdapterPhase.DateeResponse);
            var performanceMetadata = BuildDateePerformanceMetadata(dateePrompt);

            int maxAttempts = GetContractViolationAttemptLimit();
            var recovery = await SemanticOutputRecoveryExecutor.ExecuteAsync<StatefulDateeResult, LlmContractException>(
                maxAttempts,
                async (attempt, attemptCancellationToken) =>
                {
                    try
                    {
                        // Legacy calls render DateeContext.ConversationHistory into
                        // userContent. Session calls omit that block and supply ordered
                        // semantic priorMessages instead. Never combine both forms: doing
                        // so duplicates context and produces quadratic prompt growth.
                        string responseText = await SendWithDiagnosticsAsync(
                                _transport,
                                systemPrompt,
                                userContent,
                                temperature,
                                _options.MaxTokens,
                                LlmPhase.OpponentResponse,
                                context.CurrentTurn,
                                attemptCancellationToken,
                                attempt,
                                maxAttempts,
                                DateePrivatePhasePerformance,
                                performanceMetadata,
                                priorMessages)
                            .ConfigureAwait(false);

                        if (string.IsNullOrWhiteSpace(responseText))
                        {
                            throw new LlmContractException(
                                phase: "datee_response",
                                reason: "empty_output",
                                message: "LLM datee_response output is empty or whitespace.",
                                provider: null,
                                model: null,
                                parserName: "StrictDateeResponseParser",
                                expectedOptionCount: null,
                                parsedOptionCount: null,
                                optionCount: null,
                                signalCount: 0,
                                sessionId: null,
                                turnId: context.CurrentTurn
                            );
                        }

                        EmotionalDirectionLeakGuard.ThrowIfDetected(responseText, context.CurrentTurn);

                        var validationResult = GmOutputContract.ValidateSignalsStrict(responseText, out string? errorDetail);
                        if (validationResult == DateeSignalsValidationResult.MalformedSignals)
                        {
                            throw new LlmContractException(
                                phase: "datee_response",
                                reason: "malformed_signals",
                                message: $"LLM datee_response has malformed signals block: {errorDetail}",
                                provider: null,
                                model: null,
                                parserName: "StrictDateeResponseParser",
                                expectedOptionCount: null,
                                parsedOptionCount: null,
                                optionCount: null,
                                signalCount: null,
                                sessionId: null,
                                turnId: context.CurrentTurn
                            );
                        }

                        var parsed = DateeResponseParsers.ParseDateeResponseText(
                            responseText,
                            GetDiagnosticSink(),
                            requireValidatedSignals: validationResult == DateeSignalsValidationResult.ValidSignals);

                        // Keep dialogue history semantic: never persist the generated
                        // prompt document or hidden signal block as though it were
                        // visible chat content.
                        var newEntries = new ConversationMessage[]
                        {
                            ConversationMessage.User(context.PlayerDeliveredMessage),
                            ConversationMessage.Assistant(parsed.MessageText),
                        };
                        return SemanticOutputRecoveryAttemptResult<StatefulDateeResult, LlmContractException>.Accepted(
                            new StatefulDateeResult(parsed, newEntries));
                    }
                    catch (LlmContractException ex)
                    {
                        return SemanticOutputRecoveryAttemptResult<StatefulDateeResult, LlmContractException>.Rejected(ex);
                    }
                },
                delayAfterRejectedAttempt: attempt => TimeSpan.FromMilliseconds(
                    GetContractViolationBackoffDelayMs(_options.ContractViolationBackoffMs, attempt)),
                onRejected: rejection => NotifyContractViolation(rejection, DateePrivatePhasePerformance),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (recovery.IsAccepted)
            {
                return recovery.AcceptedValue;
            }

            ExceptionDispatchInfo.Capture(recovery.Exhaustion.FinalRejection).Throw();
            throw recovery.Exhaustion.FinalRejection;
        }

        /// <inheritdoc />
        public async Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context, CancellationToken ct = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var gameDef = RequireGameDefinition();

            // Build user content with history context
            var userContent = SessionDocumentBuilder.BuildInterestChangeBeatPrompt(
                context.DateeName,
                context.InterestBefore,
                context.InterestAfter,
                context.NewState,
                context.ConversationHistory,
                context.PlayerName,
                _options.PromptCatalog);

            // Use datee system prompt if provided, otherwise skip system prompt
            string systemPrompt = string.IsNullOrWhiteSpace(context.DateePrompt)
                ? SessionSystemPromptBuilder.BuildDatee("", gameDef)
                : SessionSystemPromptBuilder.BuildDatee(context.DateePrompt, gameDef);

            double temperature = _temperatures.For(PinderLlmAdapterPhase.InterestChangeBeat);

            try
            {
                var responseText = await SendWithDiagnosticsAsync(_transport, systemPrompt, userContent, temperature, _options.MaxTokens, LlmPhase.InterestChangeBeat, null, ct)
                    .ConfigureAwait(false);

                return NormalizeSingleTextOutput(
                    responseText,
                    "interest_beat",
                    rejectEllipsis: false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Cancellation must propagate — don't bury OCE under the
                // generic LLM-failure fallback (#794).
                throw;
            }
            catch (Exception ex)
            {
                RaiseOverlayDegraded(new OverlayDegradedEvent(
                    overlayType: "interest_beat",
                    provider: "primary",
                    model: null,
                    reason: "error",
                    outcome: OverlayOutcome.Degraded,
                    errorCode: ex.GetType().Name,
                    exception: ex
                ));
                return null;
            }
        }

        /// <inheritdoc />
        public async Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? dateeContext = null, string? archetypeDirective = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(instruction))
            {
                if (string.IsNullOrWhiteSpace(instruction))
                {
                    RaiseOverlayDegraded(new OverlayDegradedEvent(
                        overlayType: HorninessOverlayPrompt,
                        provider: OverlayProviderPrimary,
                        model: null,
                        reason: OverlayReasonSkippedNoInstruction,
                        outcome: OverlayOutcome.Skipped
                    ));
                }
                return message;
            }

            var prompt = BuildOverlayPrompt(
                HorninessOverlayPrompt,
                message,
                instruction,
                dateeContext: dateeContext,
                archetypeDirective: archetypeDirective);

            try
            {
                double temperature = _temperatures.For(PinderLlmAdapterPhase.OverlayRewrite);
                var result = await SendWithDiagnosticsAsync(_overlayTransport, prompt.SystemPrompt, prompt.UserContent, temperature, _options.MaxTokens, LlmPhase.HorninessOverlay, null, ct)
                    .ConfigureAwait(false);

                var normalized = NormalizeOverlayRewriteResult(result, HorninessOverlayPrompt);
                if (normalized.Degraded)
                    return message;

                return normalized.RewrittenText!;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // #794: cancellation must propagate.
            }
            catch (Exception ex)
            {
                RaiseOverlayDegraded(new OverlayDegradedEvent(
                    overlayType: HorninessOverlayPrompt,
                    provider: OverlayProviderPrimary,
                    model: null,
                    reason: OverlayReasonError,
                    outcome: OverlayOutcome.Degraded,
                    errorCode: ex.GetType().Name,
                    exception: ex
                ));
                return message;
            }
        }

        /// <inheritdoc />
        public async Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction, string trapName, string? dateeContext = null, string? archetypeDirective = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(trapInstruction))
            {
                if (string.IsNullOrWhiteSpace(trapInstruction))
                {
                    RaiseOverlayDegraded(new OverlayDegradedEvent(
                        overlayType: TrapOverlayPrompt,
                        provider: OverlayProviderPrimary,
                        model: null,
                        reason: OverlayReasonSkippedNoInstruction,
                        outcome: OverlayOutcome.Skipped,
                        trapName: trapName
                    ));
                }
                return message;
            }

            var prompt = BuildOverlayPrompt(
                TrapOverlayPrompt,
                message,
                trapInstruction,
                trapName: trapName,
                dateeContext: dateeContext,
                archetypeDirective: archetypeDirective);

            try
            {
                double temperature = _temperatures.For(PinderLlmAdapterPhase.OverlayRewrite);
                var result = await SendWithDiagnosticsAsync(_overlayTransport, prompt.SystemPrompt, prompt.UserContent, temperature, _options.MaxTokens, LlmPhase.TrapOverlay, null, ct)
                    .ConfigureAwait(false);

                var normalized = NormalizeOverlayRewriteResult(result, TrapOverlayPrompt, trapName);
                if (normalized.Degraded)
                    return message;

                return normalized.RewrittenText!;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // #794: cancellation must propagate.
            }
            catch (Exception ex)
            {
                RaiseOverlayDegraded(new OverlayDegradedEvent(
                    overlayType: TrapOverlayPrompt,
                    provider: OverlayProviderPrimary,
                    model: null,
                    reason: OverlayReasonError,
                    outcome: OverlayOutcome.Degraded,
                    errorCode: ex.GetType().Name,
                    trapName: trapName,
                    exception: ex
                ));
                return message;
            }
        }

        /// <inheritdoc />
        public async Task<string> ApplyFailureCorruptionAsync(string message, string instruction, StatType stat, Pinder.Core.Rolls.FailureTier tier, string? archetypeDirective = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(instruction))
            {
                if (string.IsNullOrWhiteSpace(instruction))
                {
                    RaiseOverlayDegraded(new OverlayDegradedEvent(
                        overlayType: FailureCorruptionPrompt,
                        provider: OverlayProviderPrimary,
                        model: null,
                        reason: OverlayReasonSkippedNoInstruction,
                        outcome: OverlayOutcome.Skipped
                    ));
                }
                return message;
            }

            var prompt = BuildOverlayPrompt(
                FailureCorruptionPrompt,
                message,
                instruction,
                stat: stat.ToString(),
                tier: tier.ToString(),
                archetypeDirective: archetypeDirective);

            try
            {
                double temperature = _temperatures.For(PinderLlmAdapterPhase.OverlayRewrite);
                var result = await SendWithDiagnosticsAsync(_overlayTransport, prompt.SystemPrompt, prompt.UserContent, temperature, _options.MaxTokens, LlmPhase.Delivery, null, ct)
                    .ConfigureAwait(false);

                var normalized = NormalizeOverlayRewriteResult(result, FailureCorruptionPrompt);
                if (normalized.Degraded)
                    return message;

                return normalized.RewrittenText!;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // #794: cancellation must propagate.
            }
            catch (Exception ex)
            {
                RaiseOverlayDegraded(new OverlayDegradedEvent(
                    overlayType: FailureCorruptionPrompt,
                    provider: OverlayProviderPrimary,
                    model: null,
                    reason: OverlayReasonError,
                    outcome: OverlayOutcome.Degraded,
                    errorCode: ex.GetType().Name,
                    exception: ex
                ));
                return message;
            }
        }

        /// <inheritdoc />
        public async Task<string> ApplyShadowCorruptionAsync(string message, string instruction, ShadowStatType shadow, string? archetypeDirective = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(instruction))
            {
                if (string.IsNullOrWhiteSpace(instruction))
                {
                    RaiseOverlayDegraded(new OverlayDegradedEvent(
                        overlayType: ShadowCorruptionPrompt,
                        provider: OverlayProviderPrimary,
                        model: null,
                        reason: OverlayReasonSkippedNoInstruction,
                        outcome: OverlayOutcome.Skipped
                    ));
                }
                return message;
            }

            var prompt = BuildOverlayPrompt(
                ShadowCorruptionPrompt,
                message,
                instruction,
                shadow: shadow.ToString(),
                archetypeDirective: archetypeDirective);

            try
            {
                double temperature = _temperatures.For(PinderLlmAdapterPhase.OverlayRewrite);
                var result = await SendWithDiagnosticsAsync(_overlayTransport, prompt.SystemPrompt, prompt.UserContent, temperature, _options.MaxTokens, LlmPhase.ShadowCorruption, null, ct)
                    .ConfigureAwait(false);

                var normalized = NormalizeOverlayRewriteResult(result, ShadowCorruptionPrompt);
                if (normalized.Degraded)
                    return message;

                return normalized.RewrittenText!;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // #794: cancellation must propagate.
            }
            catch (Exception ex)
            {
                RaiseOverlayDegraded(new OverlayDegradedEvent(
                    overlayType: ShadowCorruptionPrompt,
                    provider: OverlayProviderPrimary,
                    model: null,
                    reason: OverlayReasonError,
                    outcome: OverlayOutcome.Degraded,
                    errorCode: ex.GetType().Name,
                    exception: ex
                ));
                return message;
            }
        }

        /// <inheritdoc />
        public async Task<string> GetSuccessImprovementAsync(SuccessImprovementContext context, CancellationToken ct = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var gameDef = RequireGameDefinition();

            var instructions = _options.StatDeliveryInstructions ?? StatDeliveryInstructions.TryLoadDefault();
            string template = instructions?.Get(context.Stat, context.TierKey);

            if (string.IsNullOrWhiteSpace(template))
            {
                RaiseOverlayDegraded(new OverlayDegradedEvent(
                    overlayType: "success_improvement",
                    provider: "primary",
                    model: null,
                    reason: "skipped_no_template",
                    outcome: OverlayOutcome.Skipped
                ));
                return context.DeliveredMessage;
            }

            string instruction = PromptCatalog.Substitute(
                template,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["player_name"] = context.PlayerName,
                    ["datee_name"] = context.DateeName,
                    ["delivered_message"] = context.DeliveredMessage,
                });

            string envelope = RequireConfiguredPrompt(
                instructions?.GetSuccessImprovementPromptTemplate() ?? "",
                "success_improvement_prompt_template",
                nameof(GetSuccessImprovementAsync));

            string userContent = RenderRequiredTemplate(
                envelope,
                "success_improvement_prompt_template",
                nameof(GetSuccessImprovementAsync),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["player_name"] = context.PlayerName,
                    ["datee_name"] = context.DateeName,
                    ["delivered_message"] = context.DeliveredMessage,
                    ["tier"] = context.TierKey ?? string.Empty,
                    ["tier_upper"] = (context.TierKey ?? string.Empty).ToUpperInvariant(),
                    ["stat"] = context.Stat.ToString(),
                    ["conversation_history"] = FormatConversationHistory(context.ConversationHistory),
                    ["instruction"] = instruction,
                },
                "tier",
                "stat",
                "delivered_message",
                "conversation_history",
                "instruction");

            string systemPrompt = SessionSystemPromptBuilder.BuildPlayerAvatar(context.PlayerAvatarPrompt, gameDef);

            var responseText = await SendWithDiagnosticsAsync(_transport, systemPrompt, userContent, _temperatures.For(PinderLlmAdapterPhase.SuccessImprovement), _options.MaxTokens, LlmPhase.Delivery, null, ct)
                .ConfigureAwait(false);

            var improved = NormalizeSingleTextOutput(
                responseText,
                "success_improvement",
                rejectEllipsis: true);
            if (improved == null)
                return context.DeliveredMessage;

            if (Pinder.Core.Conversation.SuccessImprovementValidator.IsRejected(improved))
            {
                RaiseOverlayDegraded(new OverlayDegradedEvent(
                    overlayType: "success_improvement",
                    provider: "primary",
                    model: null,
                    reason: "meta_control_output",
                    outcome: OverlayOutcome.Degraded
                ));
                return context.DeliveredMessage;
            }

            return improved;
        }

        /// <inheritdoc />
        public async Task<string> GetSteeringQuestionAsync(SteeringContext context, CancellationToken ct = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var gameDef = RequireGameDefinition();

            string template = RequireConfiguredPrompt(
                gameDef.SteeringPrompt,
                "steering_prompt",
                nameof(GetSteeringQuestionAsync));

            string prompt = RenderRequiredTemplate(
                template,
                "steering_prompt",
                nameof(GetSteeringQuestionAsync),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["player_name"] = context.PlayerName,
                    ["datee_name"] = context.DateeName,
                    ["delivered_message"] = context.DeliveredMessage,
                    ["conversation_history"] = FormatConversationHistory(context.ConversationHistory),
                },
                "conversation_history");

            string systemPrompt = SessionSystemPromptBuilder.BuildPlayerAvatar(context.PlayerAvatarPrompt, gameDef);

            var responseText = await SendWithDiagnosticsAsync(_transport, systemPrompt, prompt, _temperatures.For(PinderLlmAdapterPhase.SteeringQuestion), _options.MaxTokens, LlmPhase.Steering, null, ct)
                .ConfigureAwait(false);

            // #831: thinking-block stripping moved to
            // ThinkingStrippingLlmTransport (transport decorator). The
            // steering question now arrives already stripped, so we only
            // trim here.
            return NormalizeSingleTextOutput(
                responseText,
                "steering",
                rejectEllipsis: false) ?? string.Empty;
        }

        public async Task<string> GetHorninessQuestionAsync(HorninessQuestionContext context, CancellationToken ct = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var gameDef = RequireGameDefinition();

            string template = RequireConfiguredPrompt(
                gameDef.HorninessPrompt,
                "horniness_prompt",
                nameof(GetHorninessQuestionAsync));

            string prompt = RenderRequiredTemplate(
                template,
                "horniness_prompt",
                nameof(GetHorninessQuestionAsync),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["player_name"] = context.PlayerName,
                    ["datee_name"] = context.DateeName,
                    ["delivered_message"] = context.DeliveredMessage,
                    ["conversation_history"] = FormatConversationHistory(context.ConversationHistory),
                },
                "conversation_history");

            string systemPrompt = SessionSystemPromptBuilder.BuildPlayerAvatar(context.PlayerAvatarPrompt, gameDef);

            var responseText = await SendWithDiagnosticsAsync(_transport, systemPrompt, prompt, _temperatures.For(PinderLlmAdapterPhase.HorninessQuestion), _options.MaxTokens, LlmPhase.HorninessOverlay, null, ct)
                .ConfigureAwait(false);

            var question = NormalizeSingleTextOutput(
                responseText,
                "horniness_question",
                rejectEllipsis: false);
            if (question == null)
                throw new InvalidOperationException("LLM horniness_question output is empty or whitespace.");

            return question;
        }


        private static int GetExpectedDialogueOptionCount(DialogueContext context, GameDefinition gameDef)
        {
            return context.AvailableStats != null
                ? Math.Min(context.AvailableStats.Length, gameDef.MaxDialogueOptions)
                : gameDef.MaxDialogueOptions;
        }

        private void AppendConfiguredConversationHistory(
            StringBuilder sb,
            IReadOnlyList<(string Sender, string Text)> history)
        {
            sb.AppendLine(GetPrompt("conversation-history-heading"));
            if (history == null || history.Count == 0)
            {
                sb.AppendLine(GetPrompt("conversation-history-empty"));
                return;
            }

            foreach (var (sender, text) in history)
            {
                sb.AppendLine($"{sender}: {text}");
            }
        }

        private static DialogueOption[] ParseDialogueOptionsFromTextOrJson(
            string responseText,
            DialogueContext context,
            GameDefinition gameDef)
        {
            if (LooksLikeJsonObject(responseText))
            {
                var structuredOptions = DialogueOptionsStructuredContract.ParseStrict(
                    responseText,
                    context.AvailableStats,
                    gameDef.MaxDialogueOptions,
                    out string? jsonErrorCode,
                    out string? jsonErrorMessage,
                    out int jsonParsedCount,
                    out int jsonExpectedCount);

                if (jsonErrorCode == null)
                {
                    return structuredOptions;
                }

                throw CreateDialogueOptionsContractException(
                    jsonErrorCode,
                    jsonErrorMessage!,
                    "StructuredDialogueOptionsParser",
                    jsonExpectedCount,
                    jsonParsedCount,
                    context.CurrentTurn,
                    provider: null,
                    model: null);
            }

            var parsedOptions = DialogueOptionParsers.ParseDialogueOptionsStrict(
                responseText,
                context.AvailableStats,
                gameDef.MaxDialogueOptions,
                out string? errorCode,
                out string? errorMessage,
                out int parsedCount,
                out int expectedCount);

            if (errorCode != null)
            {
                throw CreateDialogueOptionsContractException(
                    errorCode,
                    errorMessage!,
                    "StrictDialogueOptionsParser",
                    expectedCount,
                    parsedCount,
                    context.CurrentTurn,
                    provider: null,
                    model: null);
            }

            return parsedOptions;
        }

        private static bool LooksLikeJsonObject(string? responseText)
        {
            if (string.IsNullOrWhiteSpace(responseText))
            {
                return false;
            }

            return responseText.TrimStart().StartsWith("{", StringComparison.Ordinal);
        }

        private static LlmContractException CreateDialogueOptionsContractException(
            string errorCode,
            string errorMessage,
            string parserName,
            int expectedCount,
            int parsedCount,
            int turnId,
            string? provider,
            string? model)
        {
            return new LlmContractException(
                phase: "dialogue_options",
                reason: errorCode,
                message: errorMessage,
                provider: provider,
                model: model,
                parserName: parserName,
                expectedOptionCount: expectedCount,
                parsedOptionCount: parsedCount,
                optionCount: parsedCount,
                signalCount: null,
                sessionId: null,
                turnId: turnId);
        }

        /// <summary>
        /// #950: emits a trace warning (and fires <see cref="PinderLlmAdapterOptions.OnStakeSkipWarning"/>)
        /// when none of the generated options contain any token from the active stake lines.
        /// Matching strategy: extract all whitespace/punctuation-delimited tokens ≥ 5 chars from stake
        /// lines (covers named fragments such as "Margot", "deleted", "drummer", "thesis", specific years,
        /// etc.) and do a case-insensitive substring check against each option's text.
        /// Intentionally lightweight — no regex, no per-fragment allocation inside the option loop.
        /// </summary>
        private void WarnIfStakeSkipped(DialogueContext context, DialogueOption[] options)
        {
            // Split each stake line on all non-alphanumeric characters to extract meaningful tokens.
            // Minimum 5 chars to filter stop-words; keeps names, verbs, years, nouns.
            var tokens = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var line in context.StakeLines!)
            {
                foreach (var part in line.Split(new[] { ' ', ',', '.', '\n', '\r', ';', ':', '!', '?', '(', ')' }, System.StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = part.Trim();
                    if (trimmed.Length >= 5)
                        tokens.Add(trimmed.ToLowerInvariant());
                }
            }

            if (tokens.Count == 0) return;

            bool anyHit = false;
            foreach (var opt in options)
            {
                string optLower = opt.IntendedText.ToLowerInvariant();
                foreach (var token in tokens)
                {
                    if (optLower.IndexOf(token, System.StringComparison.Ordinal) >= 0)
                    {
                        anyHit = true;
                        break;
                    }
                }
                if (anyHit) break;
            }

            if (!anyHit)
            {
                string warning = $"option_generator_skipped_stake turn={context.CurrentTurn} stake_lines={context.StakeLines!.Length} stake_hits=0";
                System.Diagnostics.Trace.TraceWarning(warning);
                _options.OnStakeSkipWarning?.Invoke(warning);
            }
        }

        private OverlayRewriteResult NormalizeOverlayRewriteResult(string? result, string overlayType, string? trapName = null)
        {
            if (string.IsNullOrWhiteSpace(result))
            {
                RaiseOverlayDegraded(CreateOverlayRewriteDegradedEvent(overlayType, OverlayReasonEmptyOutput, trapName));
                return OverlayRewriteResult.DegradedResult();
            }

            // #831: thinking-block stripping moved to ThinkingStrippingLlmTransport
            // (transport decorator). Refusal detection sees the already-cleaned text.
            string trimmed = result!.Trim();
            if (IsOverlayRefusal(trimmed))
            {
                RaiseOverlayDegraded(CreateOverlayRewriteDegradedEvent(overlayType, OverlayReasonRefusal, trapName));
                return OverlayRewriteResult.DegradedResult();
            }

            return OverlayRewriteResult.Success(trimmed);
        }

        private string? NormalizeSingleTextOutput(
            string? result,
            string overlayType,
            bool rejectEllipsis,
            bool stripQuotes = true)
        {
            string trimmed = (result ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmed) ||
                (rejectEllipsis && string.Equals(trimmed, "...", StringComparison.OrdinalIgnoreCase)))
            {
                RaiseOverlayDegraded(new OverlayDegradedEvent(
                    overlayType: overlayType,
                    provider: OverlayProviderPrimary,
                    model: null,
                    reason: OverlayReasonEmptyOutput,
                    outcome: OverlayOutcome.Degraded
                ));
                return null;
            }

            if (stripQuotes && trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"')
                trimmed = trimmed.Substring(1, trimmed.Length - 2).Trim();

            return trimmed;
        }

        private static bool IsOverlayRefusal(string trimmed)
        {
            return trimmed.StartsWith(RefusalPrefixCant, StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith(RefusalPrefixCannot, StringComparison.OrdinalIgnoreCase) ||
                trimmed.IndexOf(RefusalPhraseInappropriate, StringComparison.OrdinalIgnoreCase) >= 0 ||
                trimmed.IndexOf(RefusalPhraseHappyToHelp, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static OverlayDegradedEvent CreateOverlayRewriteDegradedEvent(
            string overlayType,
            string reason,
            string? trapName = null)
        {
            return new OverlayDegradedEvent(
                overlayType: overlayType,
                provider: OverlayProviderPrimary,
                model: null,
                reason: reason,
                outcome: OverlayOutcome.Degraded,
                trapName: trapName
            );
        }

        private void RaiseOverlayDegraded(OverlayDegradedEvent evt)
        {
            var handler = _options.OnOverlayDegraded ?? PinderLlmAdapterOptions.DefaultOnOverlayDegraded;
            handler?.Invoke(evt);
        }

        private void NotifyContractViolation(LlmContractException ex)
        {
            var violation = new LlmContractViolation(
                phase: ex.Phase,
                reason: ex.Reason,
                provider: ex.Provider,
                model: ex.Model,
                parserName: ex.ParserName,
                expectedOptionCount: ex.ExpectedOptionCount,
                parsedOptionCount: ex.ParsedOptionCount,
                optionCount: ex.OptionCount,
                signalCount: ex.SignalCount,
                sessionId: ex.SessionId,
                turnId: ex.TurnId
            );

            _options.OnLlmContractViolation?.Invoke(violation);
        }

        private void NotifyContractViolation(
            SemanticOutputRecoveryRejection<LlmContractException> rejection,
            string? dateePrivatePhase)
        {
            if (rejection == null) throw new ArgumentNullException(nameof(rejection));

            NotifyContractViolation(rejection.Rejection);
            EmitContractRejectedDiagnostic(rejection, dateePrivatePhase);
        }

        private void EmitContractRejectedDiagnostic(
            SemanticOutputRecoveryRejection<LlmContractException> rejection,
            string? dateePrivatePhase)
        {
            var ex = rejection.Rejection;
            var hints = BuildDiagnosticHints(
                ex.Phase,
                ex.TurnId,
                rejection.Attempt,
                rejection.TotalAttempts,
                dateePrivatePhase,
                null);
            hints["reason"] = ex.Reason;
            hints["will_retry"] = (!rejection.IsFinalAttempt).ToString().ToLowerInvariant();
            if (!rejection.IsFinalAttempt)
            {
                hints["next_attempt"] = (rejection.Attempt + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            if (!string.IsNullOrWhiteSpace(ex.Provider))
            {
                hints["provider"] = ex.Provider!;
            }

            if (!string.IsNullOrWhiteSpace(ex.Model))
            {
                hints["model"] = ex.Model!;
            }

            if (!string.IsNullOrWhiteSpace(ex.ParserName))
            {
                hints["parser"] = ex.ParserName!;
            }

            OperationalDiagnostics.Emit(
                GetDiagnosticSink(),
                new OperationalDiagnosticEvent(
                    "PinderLlmAdapter",
                    "LlmContractRejected",
                    OperationalDiagnosticSeverity.Warning,
                    "LLM output contract violation observed.",
                    operationKind: MapOperationKind(ex.Phase),
                    phaseCode: ex.Phase,
                    lifecycle: OperationalDiagnosticLifecycle.Phase,
                    outcome: rejection.IsFinalAttempt
                        ? OperationalDiagnosticOutcome.Failed
                        : OperationalDiagnosticOutcome.Degraded,
                    failureClassification: OperationalDiagnosticFailureClassification.Permanent,
                    correlationHints: hints));
        }

        private int GetContractViolationAttemptLimit()
        {
            return Math.Max(0, _options.MaxContractViolationRetries) + 1;
        }

        internal static int GetContractViolationBackoffDelayMs(
            int baseDelayMs,
            int completedAttemptCount)
        {
            if (baseDelayMs <= 0)
            {
                return 0;
            }

            var delay = baseDelayMs * Math.Pow(2, completedAttemptCount - 1);
            return delay >= int.MaxValue ? int.MaxValue : (int)delay;
        }

        private RenderedOverlayPrompt BuildOverlayPrompt(
            string overlayType,
            string message,
            string instruction,
            string? stat = null,
            string? tier = null,
            string? trapName = null,
            string? shadow = null,
            string? dateeContext = null,
            string? archetypeDirective = null)
        {
            var instructions = _options.StatDeliveryInstructions ?? StatDeliveryInstructions.TryLoadDefault();
            var template = instructions?.GetOverlayPromptTemplate(overlayType);
            if (template == null)
            {
                throw new InvalidOperationException(
                    $"Production overlay '{overlayType}' is missing a configured overlay prompt template. " +
                    $"Load data/delivery-instructions.yaml with overlay_prompt_templates.{overlayType} before calling the LLM adapter.");
            }

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["instruction"] = instruction,
                ["message"] = message,
                ["stat"] = stat ?? string.Empty,
                ["tier"] = tier ?? string.Empty,
                ["trap_name"] = trapName ?? string.Empty,
                ["shadow"] = shadow ?? string.Empty,
                ["datee_context"] = dateeContext?.Trim() ?? string.Empty,
                ["archetype_directive"] = archetypeDirective?.Trim() ?? string.Empty,
            };

            string userTemplate = !string.IsNullOrWhiteSpace(archetypeDirective) && template.UserWithArchetype != null
                ? template.UserWithArchetype
                : template.User;

            return new RenderedOverlayPrompt(
                RenderOverlayTemplate(template.System, values),
                RenderOverlayTemplate(userTemplate, values));
        }

        private static string RenderOverlayTemplate(string template, IReadOnlyDictionary<string, string> values)
        {
            string rendered = template;
            foreach (var pair in values)
            {
                rendered = rendered.Replace("{" + pair.Key + "}", pair.Value);
            }

            return rendered.Trim();
        }

        private static string RenderRequiredTemplate(
            string template,
            string key,
            string methodName,
            IReadOnlyDictionary<string, string> values,
            params string[] requiredTokens)
        {
            foreach (var token in requiredTokens)
            {
                if (template.IndexOf("{" + token + "}", StringComparison.Ordinal) < 0)
                {
                    throw new InvalidOperationException(
                        $"Production path '{methodName}' has configured template '{key}' without required placeholder '{{{token}}}'.");
                }
            }

            return PromptCatalog.Substitute(template, values).Trim();
        }

        private string FormatConversationHistory(IEnumerable<(string Sender, string Text)> history)
        {
            var sb = new StringBuilder();
            bool hasEntries = false;
            foreach (var (sender, text) in history)
            {
                hasEntries = true;
                sb.AppendLine($"{sender}: {text}");
            }

            return hasEntries
                ? sb.ToString().TrimEnd()
                : GetPrompt("conversation-history-empty");
        }

        private string GetPrompt(string key)
        {
            return PromptTemplates.GetCatalogString(_options.PromptCatalog, key);
        }

        private Action<OperationalDiagnosticEvent>? GetDiagnosticSink()
        {
            return _options.OnDiagnostic ?? PinderLlmAdapterOptions.DefaultOnDiagnostic;
        }

        private async Task<StructuredLlmResponse> SendStructuredWithDiagnosticsAsync(
            IStructuredLlmTransport transport,
            StructuredLlmRequest request,
            string phase,
            int? turnId,
            CancellationToken ct,
            int? attempt = null,
            int? totalAttempts = null,
            string? dateePrivatePhase = null,
            IReadOnlyDictionary<string, string>? metadata = null,
            IReadOnlyList<ConversationMessage>? priorMessages = null)
        {
            var sink = GetDiagnosticSink();
            string callId = OperationalDiagnostics.CreateCallId();
            var baseHints = BuildDiagnosticHints(
                phase,
                turnId,
                attempt,
                totalAttempts,
                dateePrivatePhase,
                metadata ?? request.Metadata);
            baseHints["schema_name"] = request.SchemaName;
            baseHints["schema_version"] = request.SchemaVersion;

            OperationalDiagnostics.Emit(
                sink,
                new OperationalDiagnosticEvent(
                    "PinderLlmAdapter",
                    "LlmTransportStarted",
                    OperationalDiagnosticSeverity.Info,
                    "Structured LLM transport operation started.",
                    operationKind: MapOperationKind(phase),
                    phaseCode: phase,
                    lifecycle: OperationalDiagnosticLifecycle.Start,
                    callId: callId,
                    correlationHints: CloneHints(baseHints)));

            var stopwatch = Stopwatch.StartNew();
            var tokenUsageBefore = CaptureTokenUsageSnapshot(transport);
            try
            {
                StructuredLlmResponse result;
                if (priorMessages != null)
                {
                    if (!(transport is IStructuredConversationLlmTransport contextual))
                        throw new InvalidOperationException(
                            "The configured transport does not support structured ordered conversation messages.");
                    result = await contextual.SendStructuredConversationAsync(request, priorMessages, ct)
                        .ConfigureAwait(false);
                }
                else
                {
                    result = await transport.SendStructuredAsync(request, ct).ConfigureAwait(false);
                }
                var tokenUsageAfter = CaptureTokenUsageSnapshot(transport);
                var hints = CloneHints(baseHints);
                AddElapsedHint(hints, stopwatch);
                AddTokenUsageHints(hints, tokenUsageBefore, tokenUsageAfter);
                AddStructuredResponseHints(hints, result);
                OperationalDiagnostics.Emit(
                    sink,
                    new OperationalDiagnosticEvent(
                        "PinderLlmAdapter",
                        "LlmTransportSucceeded",
                        OperationalDiagnosticSeverity.Info,
                        "Structured LLM transport operation succeeded.",
                        operationKind: MapOperationKind(phase),
                        phaseCode: phase,
                        lifecycle: OperationalDiagnosticLifecycle.Terminal,
                        outcome: OperationalDiagnosticOutcome.Succeeded,
                        callId: callId,
                        correlationHints: hints));
                return result;
            }
            catch (OperationCanceledException ex)
            {
                var tokenUsageAfter = CaptureTokenUsageSnapshot(transport);
                var hints = CloneHints(baseHints);
                AddElapsedHint(hints, stopwatch);
                AddTokenUsageHints(hints, tokenUsageBefore, tokenUsageAfter);
                AddExceptionTypeHint(hints, ex);
                OperationalDiagnostics.Emit(
                    sink,
                    new OperationalDiagnosticEvent(
                        "PinderLlmAdapter",
                        "LlmTransportCancelled",
                        OperationalDiagnosticSeverity.Warning,
                        "Structured LLM transport operation was cancelled.",
                        ShouldSuppressDiagnosticException(phase, dateePrivatePhase) ? null : ex,
                        MapOperationKind(phase),
                        phase,
                        OperationalDiagnosticLifecycle.Terminal,
                        OperationalDiagnosticOutcome.Cancelled,
                        OperationalDiagnosticFailureClassification.Cancelled,
                        callId: callId,
                        correlationHints: hints));
                throw;
            }
            catch (Exception ex)
            {
                var tokenUsageAfter = CaptureTokenUsageSnapshot(transport);
                var hints = CloneHints(baseHints);
                AddElapsedHint(hints, stopwatch);
                AddTokenUsageHints(hints, tokenUsageBefore, tokenUsageAfter);
                AddExceptionTypeHint(hints, ex);
                OperationalDiagnostics.Emit(
                    sink,
                    new OperationalDiagnosticEvent(
                        "PinderLlmAdapter",
                        "LlmTransportFailed",
                        OperationalDiagnosticSeverity.Error,
                        "Structured LLM transport operation failed.",
                        ShouldSuppressDiagnosticException(phase, dateePrivatePhase) ? null : ex,
                        MapOperationKind(phase),
                        phase,
                        OperationalDiagnosticLifecycle.Terminal,
                        OperationalDiagnosticOutcome.Failed,
                        OperationalDiagnostics.ClassifyException(ex),
                        callId: callId,
                        correlationHints: hints));
                throw;
            }
        }

        private async Task<string> SendWithDiagnosticsAsync(
            ILlmTransport transport,
            string systemPrompt,
            string userContent,
            double temperature,
            int maxTokens,
            string phase,
            int? turnId,
            CancellationToken ct,
            int? attempt = null,
            int? totalAttempts = null,
            string? dateePrivatePhase = null,
            IReadOnlyDictionary<string, string>? metadata = null,
            IReadOnlyList<ConversationMessage>? priorMessages = null)
        {
            var sink = GetDiagnosticSink();
            string callId = OperationalDiagnostics.CreateCallId();
            var baseHints = BuildDiagnosticHints(
                phase,
                turnId,
                attempt,
                totalAttempts,
                dateePrivatePhase,
                metadata);

            OperationalDiagnostics.Emit(
                sink,
                new OperationalDiagnosticEvent(
                    "PinderLlmAdapter",
                    "LlmTransportStarted",
                    OperationalDiagnosticSeverity.Info,
                    "LLM transport operation started.",
                    operationKind: MapOperationKind(phase),
                    phaseCode: phase,
                    lifecycle: OperationalDiagnosticLifecycle.Start,
                    callId: callId,
                    correlationHints: CloneHints(baseHints)));

            var stopwatch = Stopwatch.StartNew();
            var tokenUsageBefore = CaptureTokenUsageSnapshot(transport);
            try
            {
                string result;
                if (priorMessages != null)
                {
                    if (!(transport is IConversationLlmTransport contextual))
                        throw new InvalidOperationException(
                            "The configured transport does not support ordered conversation messages.");
                    result = await contextual.SendConversationAsync(
                        systemPrompt,
                        priorMessages,
                        userContent,
                        temperature,
                        maxTokens,
                        phase,
                        ct).ConfigureAwait(false);
                }
                else
                {
                    result = await transport
                        .SendAsync(systemPrompt, userContent, temperature, maxTokens, phase: phase, ct: ct)
                        .ConfigureAwait(false);
                }

                var tokenUsageAfter = CaptureTokenUsageSnapshot(transport);
                var hints = CloneHints(baseHints);
                AddElapsedHint(hints, stopwatch);
                AddTokenUsageHints(hints, tokenUsageBefore, tokenUsageAfter);
                OperationalDiagnostics.Emit(
                    sink,
                    new OperationalDiagnosticEvent(
                        "PinderLlmAdapter",
                        "LlmTransportSucceeded",
                        OperationalDiagnosticSeverity.Info,
                        "LLM transport operation succeeded.",
                        operationKind: MapOperationKind(phase),
                        phaseCode: phase,
                        lifecycle: OperationalDiagnosticLifecycle.Terminal,
                        outcome: OperationalDiagnosticOutcome.Succeeded,
                        callId: callId,
                        correlationHints: hints));

                return result;
            }
            catch (OperationCanceledException ex)
            {
                var tokenUsageAfter = CaptureTokenUsageSnapshot(transport);
                var hints = CloneHints(baseHints);
                AddElapsedHint(hints, stopwatch);
                AddTokenUsageHints(hints, tokenUsageBefore, tokenUsageAfter);
                AddExceptionTypeHint(hints, ex);
                OperationalDiagnostics.Emit(
                    sink,
                    new OperationalDiagnosticEvent(
                        "PinderLlmAdapter",
                        "LlmTransportCancelled",
                        OperationalDiagnosticSeverity.Warning,
                        "LLM transport operation was cancelled.",
                        ShouldSuppressDiagnosticException(phase, dateePrivatePhase) ? null : ex,
                        MapOperationKind(phase),
                        phase,
                        OperationalDiagnosticLifecycle.Terminal,
                        OperationalDiagnosticOutcome.Cancelled,
                        OperationalDiagnosticFailureClassification.Cancelled,
                        callId: callId,
                        correlationHints: hints));
                throw;
            }
            catch (Exception ex)
            {
                var tokenUsageAfter = CaptureTokenUsageSnapshot(transport);
                var hints = CloneHints(baseHints);
                AddElapsedHint(hints, stopwatch);
                AddTokenUsageHints(hints, tokenUsageBefore, tokenUsageAfter);
                AddExceptionTypeHint(hints, ex);
                OperationalDiagnostics.Emit(
                    sink,
                    new OperationalDiagnosticEvent(
                        "PinderLlmAdapter",
                        "LlmTransportFailed",
                        OperationalDiagnosticSeverity.Error,
                        "LLM transport operation failed.",
                        ShouldSuppressDiagnosticException(phase, dateePrivatePhase) ? null : ex,
                        MapOperationKind(phase),
                        phase,
                        OperationalDiagnosticLifecycle.Terminal,
                        OperationalDiagnosticOutcome.Failed,
                        OperationalDiagnostics.ClassifyException(ex),
                        callId: callId,
                        correlationHints: hints));
                throw;
            }
        }

        private static string MapOperationKind(string phase)
        {
            if (string.Equals(phase, LlmPhase.DialogueOptions, StringComparison.Ordinal))
            {
                return OperationalDiagnosticOperationKind.DialogueOptions;
            }

            if (string.Equals(phase, LlmPhase.OpponentResponse, StringComparison.Ordinal))
            {
                return OperationalDiagnosticOperationKind.DateeResponse;
            }

            if (string.Equals(phase, LlmPhase.EmotionalDirector, StringComparison.Ordinal))
            {
                return OperationalDiagnosticOperationKind.DateeEmotionalDirector;
            }

            if (string.Equals(phase, LlmPhase.Delivery, StringComparison.Ordinal))
            {
                return OperationalDiagnosticOperationKind.Delivery;
            }

            if (string.Equals(phase, LlmPhase.HorninessOverlay, StringComparison.Ordinal)
                || string.Equals(phase, LlmPhase.ShadowCorruption, StringComparison.Ordinal)
                || string.Equals(phase, LlmPhase.TrapOverlay, StringComparison.Ordinal))
            {
                return OperationalDiagnosticOperationKind.Overlay;
            }

            return phase ?? LlmPhase.Unknown;
        }

        private GameDefinition RequireGameDefinition([System.Runtime.CompilerServices.CallerMemberName] string methodName = "")
        {
            if (_options.GameDefinition == null)
            {
                throw new InvalidOperationException($"Production path '{methodName}' is missing GameDefinition. GameDefinition is required at the production adapter boundary to avoid silent fallbacks.");
            }
            return _options.GameDefinition;
        }

        private static string RequireConfiguredPrompt(string value, string key, string methodName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    $"Production path '{methodName}' is missing configured GameDefinition.{key}. " +
                    $"Load data/game-definition.yaml with a non-empty '{key}' value.");
            }

            return value;
        }

        public void Dispose()
        {
            if (_transport is IDisposable disposable)
                disposable.Dispose();
        }

        private sealed class RenderedOverlayPrompt
        {
            public RenderedOverlayPrompt(string systemPrompt, string userContent)
            {
                SystemPrompt = systemPrompt;
                UserContent = userContent;
            }

            public string SystemPrompt { get; }

            public string UserContent { get; }
        }

        private sealed class OverlayRewriteResult
        {
            private OverlayRewriteResult(bool degraded, string? rewrittenText)
            {
                Degraded = degraded;
                RewrittenText = rewrittenText;
            }

            public bool Degraded { get; }

            public string? RewrittenText { get; }

            public static OverlayRewriteResult DegradedResult()
            {
                return new OverlayRewriteResult(true, null);
            }

            public static OverlayRewriteResult Success(string rewrittenText)
            {
                return new OverlayRewriteResult(false, rewrittenText);
            }
        }
    }
}
