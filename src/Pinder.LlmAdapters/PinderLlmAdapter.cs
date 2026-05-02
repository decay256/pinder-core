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
        private const double DefaultOpponentResponseTemperature = 0.85;

        private readonly ILlmTransport _transport;
        private readonly PinderLlmAdapterOptions _options;

        // #788: opponent conversation state lives on GameSession, not here.
        // The adapter is pure-stateless and safe for concurrent reuse across sessions.

        public PinderLlmAdapter(ILlmTransport transport, PinderLlmAdapterOptions options)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        // ── ILlmAdapter ────────────────────────────────────────────────────

        /// <inheritdoc />
        public async Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var userContent = SessionDocumentBuilder.BuildDialogueOptionsPrompt(context);
            var systemPrompt = SessionSystemPromptBuilder.BuildPlayer(context.PlayerPrompt, _options.GameDefinition);
            double temperature = _options.DialogueOptionsTemperature ?? DefaultDialogueOptionsTemperature;

            var responseText = await _transport.SendAsync(systemPrompt, userContent, temperature, _options.MaxTokens, phase: LlmPhase.DialogueOptions)
                .ConfigureAwait(false);

            var parsedOptions = DialogueOptionParsers.ParseDialogueOptionsText(responseText);
            int maxOptions = _options.GameDefinition?.MaxDialogueOptions ?? 3;
            if (parsedOptions.Length > maxOptions)
            {
                var capped = new DialogueOption[maxOptions];
                System.Array.Copy(parsedOptions, capped, maxOptions);
                return capped;
            }
            return parsedOptions;
        }

        /// <inheritdoc />
        public async Task<string> DeliverMessageAsync(DeliveryContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var deliveryRules = _options.GameDefinition?.DeliveryRules;
            var userContent = SessionDocumentBuilder.BuildDeliveryPrompt(
                context, deliveryRules: deliveryRules, statDeliveryInstructions: _options.StatDeliveryInstructions);
            var systemPrompt = SessionSystemPromptBuilder.BuildPlayer(context.PlayerPrompt, _options.GameDefinition);
            double temperature = _options.DeliveryTemperature ?? DefaultDeliveryTemperature;

            var responseText = await _transport.SendAsync(systemPrompt, userContent, temperature, _options.MaxTokens, phase: LlmPhase.Delivery)
                .ConfigureAwait(false);

            return responseText ?? "";
        }

        /// <inheritdoc />
        public async Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
        {
            // #788: stateless single-turn fallback path. Stateful callers route
            // through the IStatefulLlmAdapter overload that takes a history.
            var result = await GetOpponentResponseAsync(context, System.Array.Empty<ConversationMessage>(), default).ConfigureAwait(false);
            return result.Response;
        }

        /// <inheritdoc />
        public async Task<StatefulOpponentResult> GetOpponentResponseAsync(
            OpponentContext context,
            IReadOnlyList<ConversationMessage> history,
            CancellationToken cancellationToken = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (history == null) throw new ArgumentNullException(nameof(history));

            var userContent = SessionDocumentBuilder.BuildOpponentPrompt(context);
            var systemPrompt = SessionSystemPromptBuilder.BuildOpponent(context.OpponentPrompt, _options.GameDefinition);
            double temperature = _options.OpponentResponseTemperature ?? DefaultOpponentResponseTemperature;

            string responseText;
            if (history.Count == 0)
            {
                // No prior turns — single-shot.
                responseText = await _transport.SendAsync(systemPrompt, userContent, temperature, _options.MaxTokens, phase: LlmPhase.OpponentResponse)
                    .ConfigureAwait(false);
            }
            else
            {
                // Multi-turn: flatten the supplied history into the user content
                // (the transport contract is single-turn). The current turn's
                // user content is appended last, mirroring Anthropic/OpenAI
                // wire ordering.
                responseText = await SendStatefulOpponentAsync(systemPrompt, userContent, history, temperature)
                    .ConfigureAwait(false);
            }

            var parsed = OpponentResponseParsers.ParseOpponentResponseText(responseText);

            // Hand the engine the two new history entries to append: the user
            // prompt we just sent and the assistant response we got back.
            var newEntries = new ConversationMessage[]
            {
                ConversationMessage.User(userContent),
                ConversationMessage.Assistant(responseText ?? string.Empty),
            };
            return new StatefulOpponentResult(parsed, newEntries);
        }

        /// <inheritdoc />
        public async Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            // Build user content with history context
            var userContent = SessionDocumentBuilder.BuildInterestChangeBeatPrompt(
                context.OpponentName,
                context.InterestBefore,
                context.InterestAfter,
                context.NewState,
                context.ConversationHistory,
                context.PlayerName);

            // Use opponent system prompt if provided, otherwise skip system prompt
            string systemPrompt = string.IsNullOrWhiteSpace(context.OpponentPrompt)
                ? SessionSystemPromptBuilder.BuildOpponent("", _options.GameDefinition)
                : SessionSystemPromptBuilder.BuildOpponent(context.OpponentPrompt, _options.GameDefinition);

            double temperature = _options.Temperature;

            try
            {
                var responseText = await _transport.SendAsync(systemPrompt, userContent, temperature, _options.MaxTokens, phase: LlmPhase.InterestChangeBeat)
                    .ConfigureAwait(false);

                var trimmed = responseText?.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                    return null;

                // Strip surrounding quotes if present
                if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"')
                    trimmed = trimmed.Substring(1, trimmed.Length - 2).Trim();

                return trimmed;
            }
            catch
            {
                return null;
            }
        }

        /// <inheritdoc />
        public async Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? opponentContext = null, string? archetypeDirective = null)
        {
            if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(instruction))
                return message;

            // Route to Groq if configured
            if (!string.IsNullOrWhiteSpace(_options.OverlayGroqModel) && !string.IsNullOrWhiteSpace(_options.OverlayGroqApiKey))
            {
                return await GroqOverlayApplier.ApplyHorninessOverlayAsync(
                    _options.OverlayGroqApiKey, _options.OverlayGroqModel, message, instruction, opponentContext, archetypeDirective)
                    .ConfigureAwait(false);
            }

            // Use primary transport
            string systemPrompt = "You are editing dialogue for Pinder, a comedy RPG where sentient penises date each other on a fictional app. " +
                "The humour is absurdist and satirical — characters are oblivious to double-entendre, not explicit. " +
                "Apply the overlay instruction to rewrite the message with the requested tonal shift. " +
                "Return ONLY the rewritten message text. No commentary, no refusals, no preamble.";

            if (!string.IsNullOrWhiteSpace(opponentContext))
                systemPrompt += $"\n\nThe message being sent is directed at this character:\n{opponentContext}";

            // Inject the speaker's active archetype directive (#372) so the
            // overlay rewrite stays in the character's voice instead of
            // collapsing to a generic horny rewrite.
            string userContent = !string.IsNullOrWhiteSpace(archetypeDirective)
                ? $"{archetypeDirective}\n\nOVERLAY INSTRUCTION:\n{instruction}\n\nORIGINAL MESSAGE:\n{message}\n\nApply the overlay (preserving the archetype voice above) and return the modified message."
                : $"OVERLAY INSTRUCTION:\n{instruction}\n\nORIGINAL MESSAGE:\n{message}\n\nApply the overlay and return the modified message.";

            try
            {
                double temperature = _options.DeliveryTemperature ?? 0.7;
                var result = await _transport.SendAsync(systemPrompt, userContent, temperature, _options.MaxTokens, phase: LlmPhase.HorninessOverlay)
                    .ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(result)) return message;
                // #351: strip inline <thinking>...</thinking> /
                // <reasoning>...</reasoning> blocks for prose-only surfaces
                // before refusal-detection — a thinking block could otherwise
                // contain phrases that look like refusal markers and trigger a
                // spurious fallback to the un-overlaid message.
                string trimmed = InlineThinkingStripper.Strip(result).Trim();

                // Detect refusal — fall back to original message silently
                if (trimmed.StartsWith("I can't", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("I cannot", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.IndexOf("inappropriate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    trimmed.IndexOf("I'd be happy to help", StringComparison.OrdinalIgnoreCase) >= 0)
                    return message;

                return trimmed;
            }
            catch
            {
                return message;
            }
        }

        /// <inheritdoc />
        public async Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction, string trapName, string? opponentContext = null, string? archetypeDirective = null)
        {
            if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(trapInstruction))
                return message;

            // Route to Groq when configured — same path as horniness/shadow overlays.
            if (!string.IsNullOrWhiteSpace(_options.OverlayGroqModel) && !string.IsNullOrWhiteSpace(_options.OverlayGroqApiKey))
            {
                return await GroqOverlayApplier.ApplyTrapOverlayAsync(
                    _options.OverlayGroqApiKey, _options.OverlayGroqModel, message, trapInstruction, trapName, opponentContext, archetypeDirective)
                    .ConfigureAwait(false);
            }

            string systemPrompt = "You are editing dialogue for Pinder, a comedy RPG where sentient penises date each other on a fictional app. " +
                "The humour is absurdist and satirical — characters are oblivious to double-entendre, not explicit. " +
                "A trap is currently corrupting the character's voice. " +
                "Apply the trap instruction to rewrite the message so the trap's signature taint is visible. " +
                "Return ONLY the rewritten message text. No commentary, no refusals, no preamble.";

            if (!string.IsNullOrWhiteSpace(opponentContext))
                systemPrompt += $"\n\nThe message being sent is directed at this character:\n{opponentContext}";

            // Inject the speaker's active archetype directive (#372 + #371 union) so the
            // trap-overlay rewrite still sounds like the character.
            string userContent = !string.IsNullOrWhiteSpace(archetypeDirective)
                ? $"{archetypeDirective}\n\nTRAP INSTRUCTION ({trapName}):\n{trapInstruction}\n\nORIGINAL MESSAGE:\n{message}\n\nApply the trap taint (preserving the archetype voice above) and return the modified message."
                : $"TRAP INSTRUCTION ({trapName}):\n{trapInstruction}\n\nORIGINAL MESSAGE:\n{message}\n\nApply the trap taint and return the modified message.";

            try
            {
                double temperature = _options.DeliveryTemperature ?? 0.7;
                var result = await _transport.SendAsync(systemPrompt, userContent, temperature, _options.MaxTokens, phase: LlmPhase.TrapOverlay)
                    .ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(result)) return message;
                // #351: strip inline thinking/reasoning blocks before refusal-
                // detection so a thinking block can't trigger a false fallback.
                string trimmed = InlineThinkingStripper.Strip(result).Trim();

                // Detect refusal — fall back to original message silently.
                if (trimmed.StartsWith("I can't", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("I cannot", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.IndexOf("inappropriate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    trimmed.IndexOf("I'd be happy to help", StringComparison.OrdinalIgnoreCase) >= 0)
                    return message;

                return trimmed;
            }
            catch
            {
                return message;
            }
        }

        /// <inheritdoc />
        public async Task<string> ApplyShadowCorruptionAsync(string message, string instruction, ShadowStatType shadow, string? archetypeDirective = null)
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
                var result = await _transport.SendAsync(systemPrompt, userContent, temperature, _options.MaxTokens, phase: LlmPhase.ShadowCorruption)
                    .ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(result)) return message;
                // #351: strip inline thinking/reasoning blocks before refusal-
                // detection so a thinking block can't trigger a false fallback.
                string trimmed = InlineThinkingStripper.Strip(result).Trim();

                // Detect refusal — fall back to original message silently
                if (trimmed.StartsWith("I can't", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("I cannot", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.IndexOf("inappropriate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    trimmed.IndexOf("I'd be happy to help", StringComparison.OrdinalIgnoreCase) >= 0)
                    return message;

                return trimmed;
            }
            catch
            {
                return message;
            }
        }

        /// <inheritdoc />
        public async Task<string> GetSteeringQuestionAsync(SteeringContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            string template = _options.GameDefinition?.SteeringPrompt;
            if (string.IsNullOrWhiteSpace(template))
                template = GameDefinition.DefaultSteeringPrompt;

            string prompt = template
                .Replace("{player_name}", context.PlayerName)
                .Replace("{opponent_name}", context.OpponentName)
                .Replace("{delivered_message}", context.DeliveredMessage);

            var sb = new StringBuilder();
            sb.AppendLine("CONVERSATION SO FAR:");
            foreach (var (sender, text) in context.ConversationHistory)
            {
                sb.AppendLine($"{sender}: {text}");
            }
            sb.AppendLine();
            sb.AppendLine(prompt);

            string systemPrompt = SessionSystemPromptBuilder.BuildPlayer(context.PlayerPrompt, _options.GameDefinition);

            var responseText = await _transport.SendAsync(systemPrompt, sb.ToString(), 0.9, _options.MaxTokens, phase: LlmPhase.Steering)
                .ConfigureAwait(false);

            // #351: strip inline <thinking>/<reasoning> blocks before any
            // other shaping. This is a prose-only surface and the steering
            // question is consumed verbatim by the player UI — a stray
            // thinking block would corrupt the visible question.
            var question = InlineThinkingStripper.Strip(responseText).Trim();
            if (string.IsNullOrWhiteSpace(question))
                return "so... when are we doing this?";

            if (question.Length >= 2 && question[0] == '"' && question[question.Length - 1] == '"')
                question = question.Substring(1, question.Length - 2).Trim();

            return question;
        }

        // ── Private helpers ────────────────────────────────────────────────

        /// <summary>
        /// Sends a stateful opponent request by flattening the supplied history
        /// into the user message. The transport contract is single-turn
        /// (system + user), so prior exchanges are prefixed into the user payload
        /// before the current turn's content. Pure function of its inputs — no
        /// adapter-side state is read or written.
        /// </summary>
        private Task<string> SendStatefulOpponentAsync(
            string systemPrompt,
            string currentUserContent,
            IReadOnlyList<ConversationMessage> priorHistory,
            double temperature)
        {
            // Multi-turn: prefix prior exchanges into the user message for context.
            var contextBuilder = new StringBuilder();
            contextBuilder.AppendLine("[PREVIOUS CONVERSATION CONTEXT]");
            for (int i = 0; i < priorHistory.Count; i++)
            {
                var msg = priorHistory[i];
                string displayRole = string.Equals(msg.Role, ConversationMessage.AssistantRole, StringComparison.OrdinalIgnoreCase)
                    ? "OPPONENT" : "PLAYER";
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
                phase: LlmPhase.OpponentResponse);
        }

        public void Dispose()
        {
            if (_transport is IDisposable disposable)
                disposable.Dispose();
        }
    }
}
