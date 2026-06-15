using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;
using Pinder.LlmAdapters.Anthropic;
using Pinder.LlmAdapters.Groq;

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

        private readonly ILlmTransport _transport;
        private readonly PinderLlmAdapterOptions _options;

        // #788: datee conversation state lives on GameSession, not here.
        // The adapter is pure-stateless and safe for concurrent reuse across sessions.

        public PinderLlmAdapter(ILlmTransport transport, PinderLlmAdapterOptions options)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        // ── ILlmAdapter ────────────────────────────────────────────────────

        /// <inheritdoc />
        public async Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context, CancellationToken ct = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var userContent = SessionDocumentBuilder.BuildDialogueOptionsPrompt(context);
            var systemPrompt = SessionSystemPromptBuilder.BuildPlayerAvatar(context.PlayerAvatarPrompt, _options.GameDefinition);
            double temperature = _options.DialogueOptionsTemperature ?? DefaultDialogueOptionsTemperature;

            var responseText = await _transport.SendAsync(systemPrompt, userContent, temperature, _options.MaxTokens, phase: LlmPhase.DialogueOptions, ct: ct)
                .ConfigureAwait(false);

            var parsedOptions = DialogueOptionParsers.ParseDialogueOptionsText(responseText, context.AvailableStats);
            int maxOptions = _options.GameDefinition?.MaxDialogueOptions ?? 99;
            if (parsedOptions.Length > maxOptions)
            {
                var capped = new DialogueOption[maxOptions];
                System.Array.Copy(parsedOptions, capped, maxOptions);
                parsedOptions = capped;
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

        /// <inheritdoc />
        public async Task<DateeResponse> GetDateeResponseAsync(DateeContext context, CancellationToken ct = default)
        {
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

            var userContent = SessionDocumentBuilder.BuildDateePrompt(context);
            var systemPrompt = SessionSystemPromptBuilder.BuildDatee(context.DateePrompt, _options.GameDefinition);
            double temperature = _options.DateeResponseTemperature ?? DefaultDateeResponseTemperature;

            string responseText;
            if (history.Count == 0)
            {
                // No prior turns — single-shot.
                responseText = await _transport.SendAsync(systemPrompt, userContent, temperature, _options.MaxTokens, phase: LlmPhase.DateeResponse, ct: cancellationToken)
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

            var parsed = DateeResponseParsers.ParseDateeResponseText(responseText);

            // Hand the engine the two new history entries to append: the user
            // prompt we just sent and the assistant response we got back.
            var newEntries = new ConversationMessage[]
            {
                ConversationMessage.User(userContent),
                ConversationMessage.Assistant(responseText ?? string.Empty),
            };
            return new StatefulDateeResult(parsed, newEntries);
        }

        /// <inheritdoc />
        public async Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context, CancellationToken ct = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

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
                ? SessionSystemPromptBuilder.BuildDatee("", _options.GameDefinition)
                : SessionSystemPromptBuilder.BuildDatee(context.DateePrompt, _options.GameDefinition);

            double temperature = _options.Temperature;

            try
            {
                var responseText = await _transport.SendAsync(systemPrompt, userContent, temperature, _options.MaxTokens, phase: LlmPhase.InterestChangeBeat, ct: ct)
                    .ConfigureAwait(false);

                var trimmed = responseText?.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                    return null;

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
            catch
            {
                return null;
            }
        }

        /// <inheritdoc />
        public async Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? dateeContext = null, string? archetypeDirective = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(instruction))
                return message;

            // Route to Groq if configured
            if (!string.IsNullOrWhiteSpace(_options.OverlayGroqModel) && !string.IsNullOrWhiteSpace(_options.OverlayGroqApiKey))
            {
                return await GroqOverlayApplier.ApplyHorninessOverlayAsync(
                    _options.OverlayGroqApiKey, _options.OverlayGroqModel, message, instruction, dateeContext, archetypeDirective, ct)
                    .ConfigureAwait(false);
            }

            // Use primary transport
            string systemPrompt = "You are editing dialogue for Pinder, a comedy RPG where sentient penises date each other on a fictional app. " +
                "The humour is absurdist and satirical — characters are oblivious to double-entendre, not explicit. " +
                "Apply the overlay instruction to rewrite the message with the requested tonal shift. " +
                "Return ONLY the rewritten message text. No commentary, no refusals, no preamble.";

            if (!string.IsNullOrWhiteSpace(dateeContext))
                systemPrompt += $"\n\nThe message being sent is directed at this character:\n{dateeContext}";

            // Inject the speaker's active archetype directive (#372) so the
            // overlay rewrite stays in the character's voice instead of
            // collapsing to a generic horny rewrite.
            string userContent = !string.IsNullOrWhiteSpace(archetypeDirective)
                ? $"{archetypeDirective}\n\nOVERLAY INSTRUCTION:\n{instruction}\n\nORIGINAL MESSAGE:\n{message}\n\nApply the overlay (preserving the archetype voice above) and return the modified message."
                : $"OVERLAY INSTRUCTION:\n{instruction}\n\nORIGINAL MESSAGE:\n{message}\n\nApply the overlay and return the modified message.";

            try
            {
                double temperature = _options.DeliveryTemperature ?? 0.7;
                var result = await _transport.SendAsync(systemPrompt, userContent, temperature, _options.MaxTokens, phase: LlmPhase.HorninessOverlay, ct: ct)
                    .ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(result)) return message;
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
                    return message;

                return trimmed;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // #794: cancellation must propagate.
            }
            catch
            {
                return message;
            }
        }

        /// <inheritdoc />
        public async Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction, string trapName, string? dateeContext = null, string? archetypeDirective = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(trapInstruction))
                return message;

            // Route to Groq when configured — same path as horniness/shadow overlays.
            if (!string.IsNullOrWhiteSpace(_options.OverlayGroqModel) && !string.IsNullOrWhiteSpace(_options.OverlayGroqApiKey))
            {
                return await GroqOverlayApplier.ApplyTrapOverlayAsync(
                    _options.OverlayGroqApiKey, _options.OverlayGroqModel, message, trapInstruction, trapName, dateeContext, archetypeDirective, ct)
                    .ConfigureAwait(false);
            }

            string systemPrompt = "You are editing dialogue for Pinder, a comedy RPG where sentient penises date each other on a fictional app. " +
                "The humour is absurdist and satirical — characters are oblivious to double-entendre, not explicit. " +
                "A trap is currently corrupting the character's voice. " +
                "Apply the trap instruction to rewrite the message so the trap's signature taint is visible. " +
                "Return ONLY the rewritten message text. No commentary, no refusals, no preamble.";

            if (!string.IsNullOrWhiteSpace(dateeContext))
                systemPrompt += $"\n\nThe message being sent is directed at this character:\n{dateeContext}";

            // Inject the speaker's active archetype directive (#372 + #371 union) so the
            // trap-overlay rewrite still sounds like the character.
            string userContent = !string.IsNullOrWhiteSpace(archetypeDirective)
                ? $"{archetypeDirective}\n\nTRAP INSTRUCTION ({trapName}):\n{trapInstruction}\n\nORIGINAL MESSAGE:\n{message}\n\nApply the trap taint (preserving the archetype voice above) and return the modified message."
                : $"TRAP INSTRUCTION ({trapName}):\n{trapInstruction}\n\nORIGINAL MESSAGE:\n{message}\n\nApply the trap taint and return the modified message.";

            try
            {
                double temperature = _options.DeliveryTemperature ?? 0.7;
                var result = await _transport.SendAsync(systemPrompt, userContent, temperature, _options.MaxTokens, phase: LlmPhase.TrapOverlay, ct: ct)
                    .ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(result)) return message;
                // #831: thinking-block stripping moved to
                // ThinkingStrippingLlmTransport (transport decorator).
                string trimmed = result.Trim();

                // Detect refusal — fall back to original message silently.
                if (trimmed.StartsWith("I can't", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("I cannot", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.IndexOf("inappropriate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    trimmed.IndexOf("I'd be happy to help", StringComparison.OrdinalIgnoreCase) >= 0)
                    return message;

                return trimmed;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // #794: cancellation must propagate.
            }
            catch
            {
                return message;
            }
        }

        /// <inheritdoc />
        public async Task<string> ApplyShadowCorruptionAsync(string message, string instruction, ShadowStatType shadow, string? archetypeDirective = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(instruction))
                return message;

            string systemPrompt = "You are editing a text message for Pinder, a satirical comedy dating app. " +
                "Apply the shadow corruption instruction to rewrite the delivered message. " +
                "The message has already been through its normal delivery transformation. " +
                "Now the character's shadow stat is corrupting it further. " +
                "Return ONLY the corrupted message text. No commentary, no preamble, no refusals.";

            // Inject the speaker's active archetype directive (#372) so the
            // shadow-corrupted rewrite still sounds like the character.
            string userContent = !string.IsNullOrWhiteSpace(archetypeDirective)
                ? $"{archetypeDirective}\n\nSHADOW CORRUPTION INSTRUCTION ({shadow}):\n{instruction}\n\nORIGINAL MESSAGE:\n{message}\n\nApply the corruption (preserving the archetype voice above) and return the modified message."
                : $"SHADOW CORRUPTION INSTRUCTION ({shadow}):\n{instruction}\n\nORIGINAL MESSAGE:\n{message}\n\nApply the corruption and return the modified message.";

            try
            {
                double temperature = _options.DeliveryTemperature ?? 0.7;
                var result = await _transport.SendAsync(systemPrompt, userContent, temperature, _options.MaxTokens, phase: LlmPhase.ShadowCorruption, ct: ct)
                    .ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(result)) return message;
                // #831: thinking-block stripping moved to
                // ThinkingStrippingLlmTransport (transport decorator).
                string trimmed = result.Trim();

                // Detect refusal — fall back to original message silently
                if (trimmed.StartsWith("I can't", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("I cannot", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.IndexOf("inappropriate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    trimmed.IndexOf("I'd be happy to help", StringComparison.OrdinalIgnoreCase) >= 0)
                    return message;

                return trimmed;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // #794: cancellation must propagate.
            }
            catch
            {
                return message;
            }
        }

        /// <inheritdoc />
        public async Task<string> GetSteeringQuestionAsync(SteeringContext context, CancellationToken ct = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            string template = _options.GameDefinition?.SteeringPrompt;
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

            string systemPrompt = SessionSystemPromptBuilder.BuildPlayerAvatar(context.PlayerAvatarPrompt, _options.GameDefinition);

            var responseText = await _transport.SendAsync(systemPrompt, sb.ToString(), 0.9, _options.MaxTokens, phase: LlmPhase.Steering, ct: ct)
                .ConfigureAwait(false);

            // #831: thinking-block stripping moved to
            // ThinkingStrippingLlmTransport (transport decorator). The
            // steering question now arrives already stripped, so we only
            // trim here.
            var question = (responseText ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(question))
                return "so... when are we doing this?";

            if (question.Length >= 2 && question[0] == '"' && question[question.Length - 1] == '"')
                question = question.Substring(1, question.Length - 2).Trim();

            return question;
        }

        // ── Private helpers ────────────────────────────────────────────────

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
                LlmPhase.DateeResponse, ct);
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

        public void Dispose()
        {
            if (_transport is IDisposable disposable)
                disposable.Dispose();
        }
    }
}
