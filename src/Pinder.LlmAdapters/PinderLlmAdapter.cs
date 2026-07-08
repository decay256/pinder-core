using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;
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
        private const double DefaultDialogueOptionsTemperature = 0.9;
        private const double DefaultDeliveryTemperature = 0.7;
        private const double DefaultDateeResponseTemperature = 0.85;
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
            double temperature = _options.DialogueOptionsTemperature ?? DefaultDialogueOptionsTemperature;

            int attempt = 0;
            int maxRetries = Math.Max(1, _options.MaxContractViolationRetries);

            while (true)
            {
                attempt++;
                try
                {
                    var responseText = await _transport.SendAsync(systemPrompt, userContent, temperature, _options.MaxTokens, phase: LlmPhase.DialogueOptions, ct: ct)
                        .ConfigureAwait(false);

                    string? errorCode;
                    string? errorMessage;
                    int parsedCount;
                    int expectedCount;

                    var parsedOptions = DialogueOptionParsers.ParseDialogueOptionsStrict(
                        responseText,
                        context.AvailableStats,
                        gameDef.MaxDialogueOptions,
                        out errorCode,
                        out errorMessage,
                        out parsedCount,
                        out expectedCount);

                    if (errorCode != null)
                    {
                        throw new LlmContractException(
                            phase: "dialogue_options",
                            reason: errorCode,
                            message: errorMessage!,
                            provider: null,
                            model: null,
                            parserName: "StrictDialogueOptionsParser",
                            expectedOptionCount: expectedCount,
                            parsedOptionCount: parsedCount,
                            optionCount: parsedCount,
                            signalCount: null,
                            sessionId: null,
                            turnId: context.CurrentTurn
                        );
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

                    if (attempt >= maxRetries)
                    {
                        throw;
                    }

                    int delayMs = (int)(_options.ContractViolationBackoffMs * Math.Pow(2, attempt - 1));
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
            double temperature = _options.DateeResponseTemperature ?? DefaultDateeResponseTemperature;

            int attempt = 0;
            int maxRetries = Math.Max(1, _options.MaxContractViolationRetries);

            while (true)
            {
                attempt++;
                try
                {
                    string responseText;
                    if (history.Count == 0)
                    {
                        // No prior turns — single-shot.
                        responseText = await _transport.SendAsync(systemPrompt, userContent, temperature, _options.MaxTokens, phase: LlmPhase.OpponentResponse, ct: cancellationToken)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        // Multi-turn: flatten the supplied history into the user content
                        // (the transport contract is single-turn). The current turn's
                        // user content is appended last, mirroring Anthropic/OpenAI
                        // wire ordering.
                        responseText = await SendStatefulDateeAsync(systemPrompt, userContent, history, temperature, cancellationToken)
                            .ConfigureAwait(false);
                    }

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

                    // Hand the engine the two new history entries to append: the user
                    // prompt we just sent and the assistant response we got back.
                    var newEntries = new ConversationMessage[]
                    {
                        ConversationMessage.User(userContent),
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

                    if (attempt >= maxRetries)
                    {
                        throw;
                    }

                    int delayMs = (int)(_options.ContractViolationBackoffMs * Math.Pow(2, attempt - 1));
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
                var responseText = await _transport.SendAsync(systemPrompt, userContent, temperature, _options.MaxTokens, phase: LlmPhase.InterestChangeBeat, ct: ct)
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
                double temperature = _options.DeliveryTemperature ?? DefaultDeliveryTemperature;
                var result = await _overlayTransport.SendAsync(prompt.SystemPrompt, prompt.UserContent, temperature, _options.MaxTokens, phase: LlmPhase.HorninessOverlay, ct: ct)
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
                double temperature = _options.DeliveryTemperature ?? DefaultDeliveryTemperature;
                var result = await _overlayTransport.SendAsync(prompt.SystemPrompt, prompt.UserContent, temperature, _options.MaxTokens, phase: LlmPhase.TrapOverlay, ct: ct)
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
                double temperature = _options.DeliveryTemperature ?? DefaultDeliveryTemperature;
                var result = await _overlayTransport.SendAsync(prompt.SystemPrompt, prompt.UserContent, temperature, _options.MaxTokens, phase: LlmPhase.Delivery, ct: ct)
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
                double temperature = _options.DeliveryTemperature ?? DefaultDeliveryTemperature;
                var result = await _overlayTransport.SendAsync(prompt.SystemPrompt, prompt.UserContent, temperature, _options.MaxTokens, phase: LlmPhase.ShadowCorruption, ct: ct)
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

            string template = null;
            if (_options.StatDeliveryInstructions != null)
            {
                template = _options.StatDeliveryInstructions.Get(context.Stat, context.TierKey);
            }

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

            string prompt = template
                .Replace("{player_name}", context.PlayerName)
                .Replace("{datee_name}", context.DateeName)
                .Replace("{delivered_message}", context.DeliveredMessage);

            var sb = new StringBuilder();
            sb.AppendLine("<ENGINE_STATE>");
            sb.AppendLine("[ENGINE — CALL PURPOSE: SUCCESS_IMPROVEMENT]");
            sb.AppendLine($"Delivery outcome / tier: {(context.TierKey ?? "").ToUpperInvariant()}");
            sb.AppendLine($"Selected stat: {context.Stat}");
            sb.AppendLine($"Current delivered PLAYER AVATAR message: \"{context.DeliveredMessage}\"");
            sb.AppendLine("Output requirement: return ONE rewritten PLAYER AVATAR message only — sharper wording, same voice. No analysis, no OPTIONS, no engine commentary, no ENGINE_STATE echo.");
            sb.AppendLine("</ENGINE_STATE>");
            sb.AppendLine();
            sb.AppendLine("CONVERSATION SO FAR:");
            foreach (var (sender, text) in context.ConversationHistory)
            {
                sb.AppendLine($"{sender}: {text}");
            }
            sb.AppendLine();
            sb.AppendLine(prompt);

            string systemPrompt = SessionSystemPromptBuilder.BuildPlayerAvatar(context.PlayerAvatarPrompt, gameDef);

            var responseText = await _transport.SendAsync(systemPrompt, sb.ToString(), 0.8, _options.MaxTokens, phase: LlmPhase.Delivery, ct: ct)
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

            string template = gameDef.SteeringPrompt;
            if (string.IsNullOrWhiteSpace(template))
                template = GameDefinition.DefaultSteeringPrompt;

            string prompt = template
                .Replace("{player_name}", context.PlayerName)
                .Replace("{datee_name}", context.DateeName)
                .Replace("{delivered_message}", context.DeliveredMessage);

            var sb = new StringBuilder();
            sb.AppendLine("CONVERSATION SO FAR:");
            foreach (var (sender, text) in context.ConversationHistory)
            {
                sb.AppendLine($"{sender}: {text}");
            }
            sb.AppendLine();
            sb.AppendLine(prompt);

            string systemPrompt = SessionSystemPromptBuilder.BuildPlayerAvatar(context.PlayerAvatarPrompt, gameDef);

            var responseText = await _transport.SendAsync(systemPrompt, sb.ToString(), 0.9, _options.MaxTokens, phase: LlmPhase.Steering, ct: ct)
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

            string template = gameDef.HorninessPrompt;
            if (string.IsNullOrWhiteSpace(template))
                template = GameDefinition.DefaultHorninessPrompt;

            string prompt = template
                .Replace("{player_name}", context.PlayerName)
                .Replace("{datee_name}", context.DateeName)
                .Replace("{delivered_message}", context.DeliveredMessage);

            var sb = new StringBuilder();
            sb.AppendLine("CONVERSATION SO FAR:");
            foreach (var (sender, text) in context.ConversationHistory)
            {
                sb.AppendLine($"{sender}: {text}");
            }
            sb.AppendLine();
            sb.AppendLine(prompt);

            string systemPrompt = SessionSystemPromptBuilder.BuildPlayerAvatar(context.PlayerAvatarPrompt, gameDef);

            var responseText = await _transport.SendAsync(systemPrompt, sb.ToString(), 0.9, _options.MaxTokens, phase: LlmPhase.HorninessOverlay, ct: ct)
                .ConfigureAwait(false);

            var question = (responseText ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(question))
                return "so... your place or mine?";

            if (question.Length >= 2 && question[0] == '"' && question[question.Length - 1] == '"')
                question = question.Substring(1, question.Length - 2).Trim();

            return question;
        }


        /// <summary>
        /// Sends a stateful datee request by flattening the supplied history
        /// into the user message. The transport contract is single-turn
        /// (system + user), so prior exchanges are prefixed into the user payload
        /// before the current turn's content. Pure function of its inputs — no
        /// adapter-side state is read or written.
        /// </summary>
        private Task<string> SendStatefulDateeAsync(
            string systemPrompt,
            string currentUserContent,
            IReadOnlyList<ConversationMessage> priorHistory,
            double temperature,
            CancellationToken ct = default)
        {
            return SendStatefulAsync(
                systemPrompt, currentUserContent, priorHistory, temperature,
                LlmPhase.OpponentResponse, ct);
        }

        /// <summary>
        /// #1123: the single shared compile path for BOTH the datee and avatar
        /// sessions. Each session = GM system prompt + character spec (the
        /// cacheable prefix passed in <paramref name="systemPrompt"/>) + the
        /// running labelled transcript as the volatile suffix. The ONLY
        /// difference between the two sessions is the injected character spec;
        /// the history-flattening, ordering, and cache breakpoints are identical.
        /// </summary>
        private Task<string> SendStatefulAsync(
            string systemPrompt,
            string currentUserContent,
            IReadOnlyList<ConversationMessage> priorHistory,
            double temperature,
            string phase,
            CancellationToken ct = default)
        {
            // Multi-turn: prefix prior exchanges into the user message for context.
            var contextBuilder = new StringBuilder();
            contextBuilder.AppendLine("[PREVIOUS CONVERSATION CONTEXT]");
            for (int i = 0; i < priorHistory.Count; i++)
            {
                var msg = priorHistory[i];
                string displayRole = string.Equals(msg.Role, ConversationMessage.AssistantRole, StringComparison.OrdinalIgnoreCase)
                    ? "DATEE" : "PLAYER";
                contextBuilder.AppendLine($"[{displayRole}] {msg.Content}");
            }
            contextBuilder.AppendLine();
            contextBuilder.AppendLine("[CURRENT TURN]");
            contextBuilder.Append(currentUserContent);

            return _transport.SendAsync(
                systemPrompt,
                contextBuilder.ToString(),
                temperature,
                _options.MaxTokens,
                phase: phase,
                ct: ct);
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
                return new RenderedOverlayPrompt(string.Empty, instruction + "\n\n" + message);

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

        private Action<OperationalDiagnosticEvent>? GetDiagnosticSink()
        {
            return _options.OnDiagnostic ?? PinderLlmAdapterOptions.DefaultOnDiagnostic;
        }

        private GameDefinition RequireGameDefinition([System.Runtime.CompilerServices.CallerMemberName] string methodName = "")
        {
            if (_options.GameDefinition == null)
            {
                throw new InvalidOperationException($"Production path '{methodName}' is missing GameDefinition. GameDefinition is required at the production adapter boundary to avoid silent fallbacks.");
            }
            return _options.GameDefinition;
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
