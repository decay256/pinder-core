using System;
using System.Collections.Generic;
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
    /// Provider-agnostic implementation of ILlmAdapter and IStatefulLlmAdapter.
    /// All game-level prompt building and response parsing lives here — single source of truth.
    /// Delegates raw LLM I/O to an ILlmTransport (AnthropicTransport, OpenAiTransport, etc.).
    ///
    /// This replaces the need for every transport to duplicate game logic.
    /// The transport does ONE thing: (systemPrompt, userMessage) → rawText.
    /// </summary>
    public sealed class PinderLlmAdapter : IStatefulLlmAdapter, IDisposable
    {
        private const string HorninessOverlayPrompt = "horniness_overlay";
        private const string TrapOverlayPrompt = "trap_overlay";
        private const string FailureCorruptionPrompt = "failure_corruption";
        private const string ShadowCorruptionPrompt = "shadow_corruption";

        private readonly ILlmTransport _transport;
        private readonly ILlmTransport _overlayTransport;
        private readonly PinderLlmAdapterOptions _options;

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
        }

        // ── ILlmAdapter ────────────────────────────────────────────────────

        /// <inheritdoc />
        public async Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context, CancellationToken ct = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var gameDef = RequireGameDefinition();
            var userContent = SessionDocumentBuilder.BuildDialogueOptionsPrompt(context);
            var systemPrompt = SessionSystemPromptBuilder.BuildPlayerAvatar(context.PlayerAvatarPrompt, gameDef);
            double temperature = _options.DialogueOptionsTemperature ?? LlmPhaseTemperatures.DialogueOptions;

            int attempt = 0;
            int maxAttempts = GetContractViolationAttemptLimit();

            while (true)
            {
                attempt++;
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
                                ct)
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
                                ct)
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

                    return parsedOptions;
                }
                catch (LlmContractException ex)
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

                    if (attempt >= maxAttempts)
                    {
                        throw;
                    }

                    int delayMs = GetContractViolationBackoffDelayMs(attempt);
                    if (delayMs > 0)
                    {
                        await Task.Delay(delayMs, ct).ConfigureAwait(false);
                    }
                }
            }
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
        public async Task<StatefulDateeResult> GetDateeResponseAsync(
            DateeContext context,
            IReadOnlyList<ConversationMessage> history,
            CancellationToken cancellationToken = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (history == null) throw new ArgumentNullException(nameof(history));

            var gameDef = RequireGameDefinition();
            var userContent = SessionDocumentBuilder.BuildDateePrompt(context);
            var systemPrompt = SessionSystemPromptBuilder.BuildDatee(context.DateePrompt, gameDef);
            double temperature = _options.DateeResponseTemperature ?? LlmPhaseTemperatures.DateeResponse;

            int attempt = 0;
            int maxAttempts = GetContractViolationAttemptLimit();

            while (true)
            {
                attempt++;
                try
                {
                    // DateeContext.ConversationHistory is the authoritative transcript
                    // and BuildDateePrompt renders it into userContent. Prefixing the
                    // separate engine-owned DateeHistory here would include each prior
                    // full prompt again and cause nested, quadratic prompt growth.
                    string responseText = await SendWithDiagnosticsAsync(
                            _transport,
                            systemPrompt,
                            userContent,
                            temperature,
                            _options.MaxTokens,
                            LlmPhase.OpponentResponse,
                            context.CurrentTurn,
                            cancellationToken)
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

                    var parsed = DateeResponseParsers.ParseDateeResponseText(responseText, GetDiagnosticSink());

                    // Keep dialogue history semantic: never persist the generated
                    // prompt document as though it were a player message.
                    var newEntries = new ConversationMessage[]
                    {
                        ConversationMessage.User(context.PlayerDeliveredMessage),
                        ConversationMessage.Assistant(responseText ?? string.Empty),
                    };
                    return new StatefulDateeResult(parsed, newEntries);
                }
                catch (LlmContractException ex)
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

                    if (attempt >= maxAttempts)
                    {
                        throw;
                    }

                    int delayMs = GetContractViolationBackoffDelayMs(attempt);
                    if (delayMs > 0)
                    {
                        await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
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
                context.PlayerName);

            // Use datee system prompt if provided, otherwise skip system prompt
            string systemPrompt = string.IsNullOrWhiteSpace(context.DateePrompt)
                ? SessionSystemPromptBuilder.BuildDatee("", gameDef)
                : SessionSystemPromptBuilder.BuildDatee(context.DateePrompt, gameDef);

            double temperature = _options.Temperature;

            try
            {
                var responseText = await SendWithDiagnosticsAsync(_transport, systemPrompt, userContent, temperature, _options.MaxTokens, LlmPhase.InterestChangeBeat, null, ct)
                    .ConfigureAwait(false);

                var trimmed = responseText?.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    RaiseOverlayDegraded(new OverlayDegradedEvent(
                        overlayType: "interest_beat",
                        provider: "primary",
                        model: null,
                        reason: "empty_output",
                        outcome: OverlayOutcome.Degraded
                    ));
                    return null;
                }

                // Strip surrounding quotes if present
                if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"')
                    trimmed = trimmed.Substring(1, trimmed.Length - 2).Trim();

                return trimmed;
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
                        overlayType: "horniness_overlay",
                        provider: "primary",
                        model: null,
                        reason: "skipped_no_instruction",
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
                double temperature = _options.DeliveryTemperature ?? LlmPhaseTemperatures.OverlayRewrite;
                var result = await SendWithDiagnosticsAsync(_overlayTransport, prompt.SystemPrompt, prompt.UserContent, temperature, _options.MaxTokens, LlmPhase.HorninessOverlay, null, ct)
                    .ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(result))
                {
                    RaiseOverlayDegraded(new OverlayDegradedEvent(
                        overlayType: "horniness_overlay",
                        provider: "primary",
                        model: null,
                        reason: "empty_output",
                        outcome: OverlayOutcome.Degraded
                    ));
                    return message;
                }
                // #831: thinking-block stripping moved to
                // ThinkingStrippingLlmTransport (transport decorator).
                // The transport already strips, so we only trim here.
                // Refusal detection sees the cleaned text the same way it
                // did when the strip ran at this call site.
                string trimmed = result.Trim();

                // Detect refusal — fall back to original message silently
                if (trimmed.StartsWith("I can't", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("I cannot", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.IndexOf("inappropriate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    trimmed.IndexOf("I'd be happy to help", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    RaiseOverlayDegraded(new OverlayDegradedEvent(
                        overlayType: "horniness_overlay",
                        provider: "primary",
                        model: null,
                        reason: "refusal",
                        outcome: OverlayOutcome.Degraded
                    ));
                    return message;
                }

                return trimmed;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // #794: cancellation must propagate.
            }
            catch (Exception ex)
            {
                RaiseOverlayDegraded(new OverlayDegradedEvent(
                    overlayType: "horniness_overlay",
                    provider: "primary",
                    model: null,
                    reason: "error",
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
                        overlayType: "trap_overlay",
                        provider: "primary",
                        model: null,
                        reason: "skipped_no_instruction",
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
                double temperature = _options.DeliveryTemperature ?? LlmPhaseTemperatures.OverlayRewrite;
                var result = await SendWithDiagnosticsAsync(_overlayTransport, prompt.SystemPrompt, prompt.UserContent, temperature, _options.MaxTokens, LlmPhase.TrapOverlay, null, ct)
                    .ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(result))
                {
                    RaiseOverlayDegraded(new OverlayDegradedEvent(
                        overlayType: "trap_overlay",
                        provider: "primary",
                        model: null,
                        reason: "empty_output",
                        outcome: OverlayOutcome.Degraded,
                        trapName: trapName
                    ));
                    return message;
                }
                // #831: thinking-block stripping moved to
                // ThinkingStrippingLlmTransport (transport decorator).
                string trimmed = result.Trim();

                // Detect refusal — fall back to original message silently.
                if (trimmed.StartsWith("I can't", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("I cannot", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.IndexOf("inappropriate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    trimmed.IndexOf("I'd be happy to help", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    RaiseOverlayDegraded(new OverlayDegradedEvent(
                        overlayType: "trap_overlay",
                        provider: "primary",
                        model: null,
                        reason: "refusal",
                        outcome: OverlayOutcome.Degraded,
                        trapName: trapName
                    ));
                    return message;
                }

                return trimmed;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // #794: cancellation must propagate.
            }
            catch (Exception ex)
            {
                RaiseOverlayDegraded(new OverlayDegradedEvent(
                    overlayType: "trap_overlay",
                    provider: "primary",
                    model: null,
                    reason: "error",
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
                        overlayType: "failure_corruption",
                        provider: "primary",
                        model: null,
                        reason: "skipped_no_instruction",
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
                double temperature = _options.DeliveryTemperature ?? LlmPhaseTemperatures.OverlayRewrite;
                var result = await SendWithDiagnosticsAsync(_overlayTransport, prompt.SystemPrompt, prompt.UserContent, temperature, _options.MaxTokens, LlmPhase.Delivery, null, ct)
                    .ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(result))
                {
                    RaiseOverlayDegraded(new OverlayDegradedEvent(
                        overlayType: "failure_corruption",
                        provider: "primary",
                        model: null,
                        reason: "empty_output",
                        outcome: OverlayOutcome.Degraded
                    ));
                    return message;
                }

                string trimmed = result.Trim();

                // Detect refusal — fall back to original message silently
                if (trimmed.StartsWith("I can't", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("I cannot", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.IndexOf("inappropriate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    trimmed.IndexOf("I'd be happy to help", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    RaiseOverlayDegraded(new OverlayDegradedEvent(
                        overlayType: "failure_corruption",
                        provider: "primary",
                        model: null,
                        reason: "refusal",
                        outcome: OverlayOutcome.Degraded
                    ));
                    return message;
                }

                return trimmed;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // #794: cancellation must propagate.
            }
            catch (Exception ex)
            {
                RaiseOverlayDegraded(new OverlayDegradedEvent(
                    overlayType: "failure_corruption",
                    provider: "primary",
                    model: null,
                    reason: "error",
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
                        overlayType: "shadow_corruption",
                        provider: "primary",
                        model: null,
                        reason: "skipped_no_instruction",
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
                double temperature = _options.DeliveryTemperature ?? LlmPhaseTemperatures.OverlayRewrite;
                var result = await SendWithDiagnosticsAsync(_overlayTransport, prompt.SystemPrompt, prompt.UserContent, temperature, _options.MaxTokens, LlmPhase.ShadowCorruption, null, ct)
                    .ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(result))
                {
                    RaiseOverlayDegraded(new OverlayDegradedEvent(
                        overlayType: "shadow_corruption",
                        provider: "primary",
                        model: null,
                        reason: "empty_output",
                        outcome: OverlayOutcome.Degraded
                    ));
                    return message;
                }
                // #831: thinking-block stripping moved to
                // ThinkingStrippingLlmTransport (transport decorator).
                string trimmed = result.Trim();

                // Detect refusal — fall back to original message silently
                if (trimmed.StartsWith("I can't", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("I cannot", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.IndexOf("inappropriate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    trimmed.IndexOf("I'd be happy to help", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    RaiseOverlayDegraded(new OverlayDegradedEvent(
                        overlayType: "shadow_corruption",
                        provider: "primary",
                        model: null,
                        reason: "refusal",
                        outcome: OverlayOutcome.Degraded
                    ));
                    return message;
                }

                return trimmed;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // #794: cancellation must propagate.
            }
            catch (Exception ex)
            {
                RaiseOverlayDegraded(new OverlayDegradedEvent(
                    overlayType: "shadow_corruption",
                    provider: "primary",
                    model: null,
                    reason: "error",
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

            var responseText = await SendWithDiagnosticsAsync(_transport, systemPrompt, userContent, 0.8, _options.MaxTokens, LlmPhase.Delivery, null, ct)
                .ConfigureAwait(false);

            var improved = (responseText ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(improved) || string.Equals(improved, "...", StringComparison.OrdinalIgnoreCase))
            {
                RaiseOverlayDegraded(new OverlayDegradedEvent(
                    overlayType: "success_improvement",
                    provider: "primary",
                    model: null,
                    reason: "empty_output",
                    outcome: OverlayOutcome.Degraded
                ));
                return context.DeliveredMessage;
            }

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

            if (improved.Length >= 2 && improved[0] == '"' && improved[improved.Length - 1] == '"')
                improved = improved.Substring(1, improved.Length - 2).Trim();

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

            var responseText = await SendWithDiagnosticsAsync(_transport, systemPrompt, prompt, 0.9, _options.MaxTokens, LlmPhase.Steering, null, ct)
                .ConfigureAwait(false);

            // #831: thinking-block stripping moved to
            // ThinkingStrippingLlmTransport (transport decorator). The
            // steering question now arrives already stripped, so we only
            // trim here.
            var question = (responseText ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(question))
            {
                RaiseOverlayDegraded(new OverlayDegradedEvent(
                    overlayType: "steering",
                    provider: "primary",
                    model: null,
                    reason: "empty_output",
                    outcome: OverlayOutcome.Degraded
                ));
                return string.Empty;
            }

            if (question.Length >= 2 && question[0] == '"' && question[question.Length - 1] == '"')
                question = question.Substring(1, question.Length - 2).Trim();

            return question;
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

            var responseText = await SendWithDiagnosticsAsync(_transport, systemPrompt, prompt, 0.9, _options.MaxTokens, LlmPhase.HorninessOverlay, null, ct)
                .ConfigureAwait(false);

            var question = (responseText ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(question))
            {
                RaiseOverlayDegraded(new OverlayDegradedEvent(
                    overlayType: "horniness_question",
                    provider: "primary",
                    model: null,
                    reason: "empty_output",
                    outcome: OverlayOutcome.Degraded
                ));
                throw new InvalidOperationException("LLM horniness_question output is empty or whitespace.");
            }

            if (question.Length >= 2 && question[0] == '"' && question[question.Length - 1] == '"')
                question = question.Substring(1, question.Length - 2).Trim();

            return question;
        }


        private static int GetExpectedDialogueOptionCount(DialogueContext context, GameDefinition gameDef)
        {
            return context.AvailableStats != null
                ? Math.Min(context.AvailableStats.Length, gameDef.MaxDialogueOptions)
                : gameDef.MaxDialogueOptions;
        }

        private static void AppendConfiguredConversationHistory(
            StringBuilder sb,
            IReadOnlyList<(string Sender, string Text)> history)
        {
            sb.AppendLine(PromptTemplates.ConversationHistoryHeading);
            if (history == null || history.Count == 0)
            {
                sb.AppendLine(PromptTemplates.ConversationHistoryEmpty);
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

        private void RaiseOverlayDegraded(OverlayDegradedEvent evt)
        {
            var handler = _options.OnOverlayDegraded ?? PinderLlmAdapterOptions.DefaultOnOverlayDegraded;
            handler?.Invoke(evt);
        }

        private int GetContractViolationAttemptLimit()
        {
            return Math.Max(0, _options.MaxContractViolationRetries) + 1;
        }

        private int GetContractViolationBackoffDelayMs(int completedAttemptCount)
        {
            if (_options.ContractViolationBackoffMs <= 0)
            {
                return 0;
            }

            var delay = _options.ContractViolationBackoffMs * Math.Pow(2, completedAttemptCount - 1);
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

        private static string FormatConversationHistory(IEnumerable<(string Sender, string Text)> history)
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
                : PromptTemplates.ConversationHistoryEmpty;
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
            CancellationToken ct)
        {
            var sink = GetDiagnosticSink();
            string callId = OperationalDiagnostics.CreateCallId();
            var hints = new Dictionary<string, string> { ["phase"] = phase };
            if (turnId.HasValue)
            {
                hints["turn"] = turnId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

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
                    correlationHints: hints));

            try
            {
                var result = await transport.SendStructuredAsync(request, ct).ConfigureAwait(false);
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
                OperationalDiagnostics.Emit(
                    sink,
                    new OperationalDiagnosticEvent(
                        "PinderLlmAdapter",
                        "LlmTransportCancelled",
                        OperationalDiagnosticSeverity.Warning,
                        "Structured LLM transport operation was cancelled.",
                        ex,
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
                OperationalDiagnostics.Emit(
                    sink,
                    new OperationalDiagnosticEvent(
                        "PinderLlmAdapter",
                        "LlmTransportFailed",
                        OperationalDiagnosticSeverity.Error,
                        "Structured LLM transport operation failed.",
                        ex,
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
            CancellationToken ct)
        {
            var sink = GetDiagnosticSink();
            string callId = OperationalDiagnostics.CreateCallId();
            var hints = new Dictionary<string, string>
            {
                ["phase"] = phase,
            };
            if (turnId.HasValue)
            {
                hints["turn"] = turnId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

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
                    correlationHints: hints));

            try
            {
                string result = await transport
                    .SendAsync(systemPrompt, userContent, temperature, maxTokens, phase: phase, ct: ct)
                    .ConfigureAwait(false);

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
                OperationalDiagnostics.Emit(
                    sink,
                    new OperationalDiagnosticEvent(
                        "PinderLlmAdapter",
                        "LlmTransportCancelled",
                        OperationalDiagnosticSeverity.Warning,
                        "LLM transport operation was cancelled.",
                        ex,
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
                OperationalDiagnostics.Emit(
                    sink,
                    new OperationalDiagnosticEvent(
                        "PinderLlmAdapter",
                        "LlmTransportFailed",
                        OperationalDiagnosticSeverity.Error,
                        "LLM transport operation failed.",
                        ex,
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
    }
}
