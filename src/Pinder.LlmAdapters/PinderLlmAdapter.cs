using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
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

        // Stateful opponent session
        private ConversationSession? _opponentSession;
        private string? _opponentSystemPrompt;
        // Local parallel tracking for stateful history (for transports that lack multi-turn support)
        private readonly List<(string Role, string Content)> _opponentHistory = new List<(string Role, string Content)>();

        public PinderLlmAdapter(ILlmTransport transport, PinderLlmAdapterOptions options)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        // ── IStatefulLlmAdapter ────────────────────────────────────────────

        /// <inheritdoc />
        public void StartOpponentSession(string opponentSystemPrompt)
        {
            _opponentSystemPrompt = opponentSystemPrompt ?? throw new ArgumentNullException(nameof(opponentSystemPrompt));
            _opponentSession = new ConversationSession();
        }

        /// <inheritdoc />
        public bool HasOpponentSession => _opponentSession != null;

        // ── ILlmAdapter ────────────────────────────────────────────────────

        /// <inheritdoc />
        public async Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var userContent = SessionDocumentBuilder.BuildDialogueOptionsPrompt(context);
            var systemPrompt = SessionSystemPromptBuilder.BuildPlayer(context.PlayerPrompt, _options.GameDefinition);
            double temperature = _options.DialogueOptionsTemperature ?? DefaultDialogueOptionsTemperature;

            var responseText = await _transport.SendAsync(systemPrompt, userContent, temperature, _options.MaxTokens)
                .ConfigureAwait(false);

            return DialogueOptionParsers.ParseDialogueOptionsText(responseText);
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

            var responseText = await _transport.SendAsync(systemPrompt, userContent, temperature, _options.MaxTokens)
                .ConfigureAwait(false);

            return responseText ?? "";
        }

        /// <inheritdoc />
        public async Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var userContent = SessionDocumentBuilder.BuildOpponentPrompt(context);
            var systemPrompt = SessionSystemPromptBuilder.BuildOpponent(context.OpponentPrompt, _options.GameDefinition);
            double temperature = _options.OpponentResponseTemperature ?? DefaultOpponentResponseTemperature;

            string responseText;
            if (_opponentSession != null)
            {
                // Stateful: accumulate user message, send the full history
                _opponentSession.AppendUser(userContent);
                _opponentHistory.Add(("user", userContent));
                responseText = await SendStatefulOpponentAsync(systemPrompt, temperature).ConfigureAwait(false);
                _opponentSession.AppendAssistant(responseText);
                _opponentHistory.Add(("assistant", responseText));
            }
            else
            {
                responseText = await _transport.SendAsync(systemPrompt, userContent, temperature, _options.MaxTokens)
                    .ConfigureAwait(false);
            }

            return OpponentResponseParsers.ParseOpponentResponseText(responseText);
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
                var responseText = await _transport.SendAsync(systemPrompt, userContent, temperature, _options.MaxTokens)
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
        public async Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? opponentContext = null)
        {
            if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(instruction))
                return message;

            // Route to Groq if configured
            if (!string.IsNullOrWhiteSpace(_options.OverlayGroqModel) && !string.IsNullOrWhiteSpace(_options.OverlayGroqApiKey))
            {
                return await GroqOverlayApplier.ApplyHorninessOverlayAsync(
                    _options.OverlayGroqApiKey, _options.OverlayGroqModel, message, instruction, opponentContext)
                    .ConfigureAwait(false);
            }

            // Use primary transport
            string systemPrompt = "You are editing dialogue for Pinder, a comedy RPG where sentient penises date each other on a fictional app. " +
                "The humour is absurdist and satirical — characters are oblivious to double-entendre, not explicit. " +
                "Apply the overlay instruction to rewrite the message with the requested tonal shift. " +
                "Return ONLY the rewritten message text. No commentary, no refusals, no preamble.";

            if (!string.IsNullOrWhiteSpace(opponentContext))
                systemPrompt += $"\n\nThe message being sent is directed at this character:\n{opponentContext}";

            string userContent = $"OVERLAY INSTRUCTION:\n{instruction}\n\nORIGINAL MESSAGE:\n{message}\n\nApply the overlay and return the modified message.";

            try
            {
                double temperature = _options.DeliveryTemperature ?? 0.7;
                var result = await _transport.SendAsync(systemPrompt, userContent, temperature, _options.MaxTokens)
                    .ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(result)) return message;
                string trimmed = result.Trim();

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

            var responseText = await _transport.SendAsync(systemPrompt, sb.ToString(), 0.9, _options.MaxTokens)
                .ConfigureAwait(false);

            var question = responseText?.Trim();
            if (string.IsNullOrWhiteSpace(question))
                return "so... when are we doing this?";

            if (question.Length >= 2 && question[0] == '"' && question[question.Length - 1] == '"')
                question = question.Substring(1, question.Length - 2).Trim();

            return question;
        }

        // ── Private helpers ────────────────────────────────────────────────

        /// <summary>
        /// Sends a stateful opponent request by building a multi-turn conversation
        /// from the accumulated history and sending it via the transport.
        /// The transport's SendAsync only supports single-turn (system + user), so for
        /// stateful mode we flatten the accumulated history into the user message.
        /// </summary>
        private async Task<string> SendStatefulOpponentAsync(string systemPrompt, double temperature)
        {
            if (_opponentHistory.Count == 0)
                return await _transport.SendAsync(systemPrompt, "", temperature, _options.MaxTokens)
                    .ConfigureAwait(false);

            // The last message is the current user message (just appended)
            if (_opponentHistory.Count == 1)
            {
                // Single user message — just send it directly
                return await _transport.SendAsync(systemPrompt, _opponentHistory[0].Content, temperature, _options.MaxTokens)
                    .ConfigureAwait(false);
            }

            // Multi-turn: prefix prior exchanges into the user message for context
            var contextBuilder = new StringBuilder();
            contextBuilder.AppendLine("[PREVIOUS CONVERSATION CONTEXT]");
            for (int i = 0; i < _opponentHistory.Count - 1; i++)
            {
                var (role, content) = _opponentHistory[i];
                string displayRole = string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase)
                    ? "OPPONENT" : "PLAYER";
                contextBuilder.AppendLine($"[{displayRole}] {content}");
            }
            contextBuilder.AppendLine();
            contextBuilder.AppendLine("[CURRENT TURN]");

            // Last message is the current user prompt
            contextBuilder.Append(_opponentHistory[_opponentHistory.Count - 1].Content);

            return await _transport.SendAsync(systemPrompt, contextBuilder.ToString(), temperature, _options.MaxTokens)
                .ConfigureAwait(false);
        }

        public void Dispose()
        {
            if (_transport is IDisposable disposable)
                disposable.Dispose();
        }
    }
}
