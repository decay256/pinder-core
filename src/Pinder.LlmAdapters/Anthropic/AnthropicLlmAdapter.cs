using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pinder.LlmAdapters.Groq;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;
using Pinder.LlmAdapters.Anthropic.Dto;

namespace Pinder.LlmAdapters.Anthropic
{
    /// <summary>
    /// Concrete ILlmAdapter implementation using the Anthropic Claude Messages API.
    /// Translates the four ILlmAdapter method calls into Anthropic requests,
    /// using AnthropicClient for HTTP transport, SessionDocumentBuilder for prompt
    /// formatting, and CacheBlockBuilder for prompt caching.
    ///
    /// Parsing, request building, improvement, debug logging, and overlay logic
    /// are delegated to single-responsibility modules:
    /// - AnthropicRequestBuilders
    /// - DialogueOptionParsers
    /// - OpponentResponseParsers
    /// - StatNameNormalizer
    /// - AnthropicResponseImprover
    /// - AnthropicDebugLogger
    /// - AnthropicOverlayApplier
    /// </summary>
    public sealed class AnthropicLlmAdapter : IStatefulLlmAdapter, IDisposable
    {
        // Default temperatures per method (used when AnthropicOptions override is null)
        private const double DefaultDialogueOptionsTemperature = 0.9;
        private const double DefaultDeliveryTemperature = 0.7;
        private const double DefaultOpponentResponseTemperature = 0.85;

        private readonly AnthropicClient _client;
        private readonly AnthropicOptions _options;
        private readonly AnthropicDebugLogger _debugLogger = new AnthropicDebugLogger();

        // #788: opponent conversation state lives on GameSession, not here.
        // The adapter is pure-stateless and safe for concurrent reuse across sessions.

        /// <summary>
        /// Creates adapter with internally-owned AnthropicClient.
        /// The adapter owns the client's lifecycle and disposes it.
        /// </summary>
        public AnthropicLlmAdapter(AnthropicOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _client = new AnthropicClient(options.ApiKey);
        }

        /// <summary>
        /// Creates adapter with externally-provided HttpClient (for testing).
        /// The adapter does NOT dispose the external HttpClient.
        /// </summary>
        public AnthropicLlmAdapter(AnthropicOptions options, System.Net.Http.HttpClient httpClient)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            if (httpClient == null) throw new ArgumentNullException(nameof(httpClient));
            _client = new AnthropicClient(options.ApiKey, httpClient);
        }



        /// <inheritdoc />
        public async Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var userContent = SessionDocumentBuilder.BuildDialogueOptionsPrompt(context);
            var fullPlayerPrompt = SessionSystemPromptBuilder.BuildPlayer(context.PlayerPrompt, _options.GameDefinition);
            var systemBlocks = CacheBlockBuilder.BuildPlayerOnlySystemBlocks(fullPlayerPrompt);

            var request = AnthropicRequestBuilders.BuildMessagesRequest(
                _options.Model, _options.MaxTokens, systemBlocks, userContent,
                _options.DialogueOptionsTemperature ?? DefaultDialogueOptionsTemperature);
            AnthropicRequestBuilders.AttachTool(request, ToolSchemas.DialogueOptions);

            var response = await _client.SendMessagesAsync(request).ConfigureAwait(false);
            _debugLogger.LogDebug("options", context.CurrentTurn, request, response, _options.DebugDirectory);

            // Try structured tool_use first, fall back to text parsing
            var toolInput = response.GetToolInput();
            if (toolInput != null)
            {
                var parsed = DialogueOptionParsers.ParseDialogueOptionsTool(toolInput);
                if (parsed != null) return parsed;
            }

            var optionsDraft = response.GetText();
            // Skip improvement pass on T1 — conversation history is empty
            string optionsText = context.CurrentTurn <= 1
                ? optionsDraft
                : await AnthropicResponseImprover.ApplyImprovementAsync(
                    _client, _options, systemBlocks, userContent, optionsDraft,
                    _options.DialogueOptionsTemperature ?? DefaultDialogueOptionsTemperature).ConfigureAwait(false);
            return DialogueOptionParsers.ParseDialogueOptionsText(optionsText);
        }

        /// <inheritdoc />
        public async Task<string> DeliverMessageAsync(DeliveryContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var deliveryRules = _options.GameDefinition?.DeliveryRules;
            var userContent = SessionDocumentBuilder.BuildDeliveryPrompt(context, deliveryRules: deliveryRules, statDeliveryInstructions: _options.StatDeliveryInstructions);
            var fullPlayerPrompt = SessionSystemPromptBuilder.BuildPlayer(context.PlayerPrompt, _options.GameDefinition);
            var systemBlocks = CacheBlockBuilder.BuildPlayerOnlySystemBlocks(fullPlayerPrompt);

            var request = AnthropicRequestBuilders.BuildMessagesRequest(
                _options.Model, _options.MaxTokens, systemBlocks, userContent,
                _options.DeliveryTemperature ?? DefaultDeliveryTemperature);
            AnthropicRequestBuilders.AttachTool(request, ToolSchemas.Delivery);

            var response = await _client.SendMessagesAsync(request).ConfigureAwait(false);
            _debugLogger.LogDebug("delivery", context.CurrentTurn, request, response, _options.DebugDirectory);

            // Try structured tool_use first
            var toolInput = response.GetToolInput();
            if (toolInput != null)
            {
                var delivered = toolInput.Value<string>("delivered");
                if (!string.IsNullOrWhiteSpace(delivered))
                    return delivered;
            }

            var deliveryDraft = response.GetText();

            // Only apply improvement on notable outcomes
            bool applyImprovement = context.CurrentTurn > 1 && (
                context.Outcome == Pinder.Core.Rolls.FailureTier.None
                    ? context.BeatDcBy >= 5 || context.IsNat20
                    : context.Outcome >= Pinder.Core.Rolls.FailureTier.Misfire);

            return applyImprovement
                ? await AnthropicResponseImprover.ApplyImprovementAsync(
                    _client, _options, systemBlocks, userContent, deliveryDraft,
                    _options.DeliveryTemperature ?? DefaultDeliveryTemperature).ConfigureAwait(false)
                : deliveryDraft;
        }

        /// <inheritdoc />
        public async Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
        {
            // #788: stateless single-turn fallback. Stateful callers route
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
            var fullOpponentPrompt = SessionSystemPromptBuilder.BuildOpponent(context.OpponentPrompt, _options.GameDefinition);
            var systemBlocks = CacheBlockBuilder.BuildOpponentOnlySystemBlocks(fullOpponentPrompt);

            MessagesRequest request;
            if (history.Count == 0)
            {
                request = AnthropicRequestBuilders.BuildMessagesRequest(
                    _options.Model, _options.MaxTokens, systemBlocks, userContent,
                    _options.OpponentResponseTemperature ?? DefaultOpponentResponseTemperature);
            }
            else
            {
                // Multi-turn: build a fresh ConversationSession per call from the
                // engine-supplied history. The session object is purely a
                // request-builder helper here — no state survives the call.
                var ephemeral = new ConversationSession();
                for (int i = 0; i < history.Count; i++)
                {
                    var msg = history[i];
                    if (msg.Role == ConversationMessage.UserRole)
                        ephemeral.AppendUser(msg.Content);
                    else
                        ephemeral.AppendAssistant(msg.Content);
                }
                ephemeral.AppendUser(userContent);
                request = ephemeral.BuildRequest(
                    _options.Model,
                    _options.MaxTokens,
                    _options.OpponentResponseTemperature ?? DefaultOpponentResponseTemperature,
                    systemBlocks);
            }
            AnthropicRequestBuilders.AttachTool(request, ToolSchemas.OpponentResponse);

            var response = await _client.SendMessagesAsync(request).ConfigureAwait(false);
            _debugLogger.LogDebug("opponent", context.CurrentTurn, request, response, _options.DebugDirectory);

            OpponentResponse parsed;
            string assistantTextForHistory;

            // Try structured tool_use first
            var toolInput = response.GetToolInput();
            var toolParsed = toolInput != null
                ? OpponentResponseParsers.ParseOpponentResponseTool(toolInput)
                : null;
            if (toolParsed != null)
            {
                parsed = toolParsed;
                assistantTextForHistory = toolParsed.MessageText ?? string.Empty;
            }
            else
            {
                // Fallback: text parsing with improvement pass
                var responseText = response.GetText();
                responseText = await AnthropicResponseImprover.ApplyImprovementAsync(
                    _client, _options, systemBlocks, userContent, responseText,
                    _options.OpponentResponseTemperature ?? DefaultOpponentResponseTemperature).ConfigureAwait(false);
                parsed = OpponentResponseParsers.ParseOpponentResponseText(responseText);
                assistantTextForHistory = responseText ?? string.Empty;
            }

            var newEntries = new ConversationMessage[]
            {
                ConversationMessage.User(userContent),
                ConversationMessage.Assistant(assistantTextForHistory),
            };
            return new StatefulOpponentResult(parsed, newEntries);
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

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("CONVERSATION SO FAR:");
            foreach (var (sender, text) in context.ConversationHistory)
            {
                sb.AppendLine($"{sender}: {text}");
            }
            sb.AppendLine();
            sb.AppendLine(prompt);

            var fullPlayerPrompt = SessionSystemPromptBuilder.BuildPlayer(context.PlayerPrompt, _options.GameDefinition);
            var systemBlocks = CacheBlockBuilder.BuildPlayerOnlySystemBlocks(fullPlayerPrompt);

            var request = AnthropicRequestBuilders.BuildMessagesRequest(
                _options.Model, _options.MaxTokens, systemBlocks, sb.ToString(), 0.9);
            var response = await _client.SendMessagesAsync(request).ConfigureAwait(false);

            var question = response.GetText()?.Trim();
            if (string.IsNullOrWhiteSpace(question))
                return "so... when are we doing this?";

            if (question.Length >= 2 && question[0] == '"' && question[question.Length - 1] == '"')
                question = question.Substring(1, question.Length - 2).Trim();

            return question;
        }

        /// <inheritdoc />
        public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            return Task.FromResult<string?>(null);
        }

        /// <summary>Returns a read-only view of all per-call token stats collected during the session.</summary>
        public IReadOnlyList<CallSummaryStat> GetCallStats() => _debugLogger.GetCallStats();

        /// <summary>Writes the token summary table to the end of the debug transcript.</summary>
        public void WriteDebugSummary()
        {
            _debugLogger.WriteDebugSummary(_options.DebugDirectory);
        }

        /// <summary>
        /// Apply a horniness overlay to a delivered message by calling the LLM.
        /// Routes to Groq when OverlayGroqModel and OverlayGroqApiKey are configured.
        /// </summary>
        public async Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? opponentContext = null, string? archetypeDirective = null)
        {
            if (!string.IsNullOrWhiteSpace(_options.OverlayGroqModel) && !string.IsNullOrWhiteSpace(_options.OverlayGroqApiKey))
            {
                return await GroqOverlayApplier.ApplyHorninessOverlayAsync(
                    _options.OverlayGroqApiKey, _options.OverlayGroqModel, message, instruction, opponentContext, archetypeDirective)
                    .ConfigureAwait(false);
            }
            return await AnthropicOverlayApplier.ApplyHorninessOverlayAsync(
                _client, _options, message, instruction, opponentContext, archetypeDirective).ConfigureAwait(false);
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
                var systemBlocks = CacheBlockBuilder.BuildPlayerOnlySystemBlocks(systemPrompt);
                var request = AnthropicRequestBuilders.BuildMessagesRequest(
                    _options.Model, _options.MaxTokens, systemBlocks, userContent,
                    _options.DeliveryTemperature ?? 0.7);
                var response = await _client.SendMessagesAsync(request).ConfigureAwait(false);
                var result = response.GetText()?.Trim();

                if (string.IsNullOrWhiteSpace(result)) return message;

                // Detect refusal
                if (result.StartsWith("I can't", StringComparison.OrdinalIgnoreCase) ||
                    result.StartsWith("I cannot", StringComparison.OrdinalIgnoreCase) ||
                    result.IndexOf("inappropriate", StringComparison.OrdinalIgnoreCase) >= 0)
                    return message;

                return result;
            }
            catch
            {
                return message;
            }
        }

        /// <summary>
        /// Apply a trap overlay to a delivered message (issue #371). Routes to
        /// Groq when configured; otherwise uses the Anthropic transport with the
        /// trap-overlay system prompt. Returns the message unchanged on transport
        /// failure or detected refusal.
        /// </summary>
        public async Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction, string trapName, string? opponentContext = null, string? archetypeDirective = null)
        {
            if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(trapInstruction))
                return message;

            if (!string.IsNullOrWhiteSpace(_options.OverlayGroqModel) && !string.IsNullOrWhiteSpace(_options.OverlayGroqApiKey))
            {
                return await GroqOverlayApplier.ApplyTrapOverlayAsync(
                    _options.OverlayGroqApiKey, _options.OverlayGroqModel, message, trapInstruction, trapName, opponentContext, archetypeDirective)
                    .ConfigureAwait(false);
            }

            string systemPrompt = "You are editing a text message for Pinder, a satirical comedy dating app. " +
                "A trap is currently corrupting the character's voice. " +
                "Apply the trap instruction to rewrite the delivered message so the trap's signature taint is visible. " +
                "Return ONLY the rewritten message text. No commentary, no preamble, no refusals.";

            if (!string.IsNullOrWhiteSpace(opponentContext))
                systemPrompt += $"\n\nThe message being sent is directed at this character:\n{opponentContext}";

            // Inject the speaker's active archetype directive (#372 + #371 union) so the
            // trap-overlay rewrite still sounds like the character.
            string userContent = !string.IsNullOrWhiteSpace(archetypeDirective)
                ? $"{archetypeDirective}\n\nTRAP INSTRUCTION ({trapName}):\n{trapInstruction}\n\nORIGINAL MESSAGE:\n{message}\n\nApply the trap taint (preserving the archetype voice above) and return the modified message."
                : $"TRAP INSTRUCTION ({trapName}):\n{trapInstruction}\n\nORIGINAL MESSAGE:\n{message}\n\nApply the trap taint and return the modified message.";

            try
            {
                var systemBlocks = CacheBlockBuilder.BuildPlayerOnlySystemBlocks(systemPrompt);
                var request = AnthropicRequestBuilders.BuildMessagesRequest(
                    _options.Model, _options.MaxTokens, systemBlocks, userContent,
                    _options.DeliveryTemperature ?? 0.7);
                var response = await _client.SendMessagesAsync(request).ConfigureAwait(false);
                var result = response.GetText()?.Trim();

                if (string.IsNullOrWhiteSpace(result)) return message;

                if (result.StartsWith("I can't", StringComparison.OrdinalIgnoreCase) ||
                    result.StartsWith("I cannot", StringComparison.OrdinalIgnoreCase) ||
                    result.IndexOf("inappropriate", StringComparison.OrdinalIgnoreCase) >= 0)
                    return message;

                return result;
            }
            catch
            {
                return message;
            }
        }

        // Backward-compatibility: expose static parse methods for tests
        // that reference AnthropicLlmAdapter.ParseDialogueOptions / ParseOpponentResponse

        /// <summary>
        /// Parses structured LLM output into DialogueOption array.
        /// Delegates to DialogueOptionParsers.ParseDialogueOptionsText.
        /// </summary>
        internal static DialogueOption[] ParseDialogueOptions(string? llmResponse)
            => DialogueOptionParsers.ParseDialogueOptionsText(llmResponse);

        /// <summary>
        /// Parses structured LLM output with optional [SIGNALS] blocks.
        /// Delegates to OpponentResponseParsers.ParseOpponentResponseText.
        /// </summary>
        internal static OpponentResponse ParseOpponentResponse(string? llmResponse)
            => OpponentResponseParsers.ParseOpponentResponseText(llmResponse);

        public void Dispose()
        {
            _client.Dispose();
        }
    }
}
