using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
    /// - AnthropicResponseParsers
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

        // Stateful opponent session (#536)
        private ConversationSession? _opponentSession;
        private string? _opponentSystemPrompt;

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
        public void StartOpponentSession(string opponentSystemPrompt)
        {
            _opponentSystemPrompt = opponentSystemPrompt ?? throw new ArgumentNullException(nameof(opponentSystemPrompt));
            _opponentSession = new ConversationSession();
        }

        /// <inheritdoc />
        public bool HasOpponentSession => _opponentSession != null;

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
                var parsed = AnthropicResponseParsers.ParseDialogueOptionsTool(toolInput);
                if (parsed != null) return parsed;
            }

            var optionsDraft = response.GetText();
            // Skip improvement pass on T1 — conversation history is empty
            string optionsText = context.CurrentTurn <= 1
                ? optionsDraft
                : await AnthropicResponseImprover.ApplyImprovementAsync(
                    _client, _options, systemBlocks, userContent, optionsDraft,
                    _options.DialogueOptionsTemperature ?? DefaultDialogueOptionsTemperature).ConfigureAwait(false);
            return AnthropicResponseParsers.ParseDialogueOptionsText(optionsText);
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
            if (context == null) throw new ArgumentNullException(nameof(context));

            var userContent = SessionDocumentBuilder.BuildOpponentPrompt(context);
            var fullOpponentPrompt = SessionSystemPromptBuilder.BuildOpponent(context.OpponentPrompt, _options.GameDefinition);
            var systemBlocks = CacheBlockBuilder.BuildOpponentOnlySystemBlocks(fullOpponentPrompt);

            MessagesRequest request;
            if (_opponentSession != null)
            {
                _opponentSession.AppendUser(userContent);
                request = _opponentSession.BuildRequest(
                    _options.Model,
                    _options.MaxTokens,
                    _options.OpponentResponseTemperature ?? DefaultOpponentResponseTemperature,
                    systemBlocks);
            }
            else
            {
                request = AnthropicRequestBuilders.BuildMessagesRequest(
                    _options.Model, _options.MaxTokens, systemBlocks, userContent,
                    _options.OpponentResponseTemperature ?? DefaultOpponentResponseTemperature);
            }
            AnthropicRequestBuilders.AttachTool(request, ToolSchemas.OpponentResponse);

            var response = await _client.SendMessagesAsync(request).ConfigureAwait(false);
            _debugLogger.LogDebug("opponent", context.CurrentTurn, request, response, _options.DebugDirectory);

            // Try structured tool_use first
            var toolInput = response.GetToolInput();
            if (toolInput != null)
            {
                var parsed = AnthropicResponseParsers.ParseOpponentResponseTool(toolInput);
                if (parsed != null)
                {
                    if (_opponentSession != null)
                    {
                        _opponentSession.AppendAssistant(parsed.MessageText);
                    }
                    return parsed;
                }
            }

            // Fallback: text parsing with improvement pass
            var responseText = response.GetText();
            responseText = await AnthropicResponseImprover.ApplyImprovementAsync(
                _client, _options, systemBlocks, userContent, responseText,
                _options.OpponentResponseTemperature ?? DefaultOpponentResponseTemperature).ConfigureAwait(false);

            if (_opponentSession != null)
            {
                _opponentSession.AppendAssistant(responseText);
            }

            return AnthropicResponseParsers.ParseOpponentResponseText(responseText);
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
        /// </summary>
        public async Task<string> ApplyHorninessOverlayAsync(string message, string instruction)
        {
            return await AnthropicOverlayApplier.ApplyHorninessOverlayAsync(
                _client, _options, message, instruction).ConfigureAwait(false);
        }

        // Backward-compatibility: expose static parse methods for tests
        // that reference AnthropicLlmAdapter.ParseDialogueOptions / ParseOpponentResponse

        /// <summary>
        /// Parses structured LLM output into DialogueOption array.
        /// Delegates to AnthropicResponseParsers.ParseDialogueOptionsText.
        /// </summary>
        internal static DialogueOption[] ParseDialogueOptions(string? llmResponse)
            => AnthropicResponseParsers.ParseDialogueOptionsText(llmResponse);

        /// <summary>
        /// Parses structured LLM output with optional [SIGNALS] blocks.
        /// Delegates to AnthropicResponseParsers.ParseOpponentResponseText.
        /// </summary>
        internal static OpponentResponse ParseOpponentResponse(string? llmResponse)
            => AnthropicResponseParsers.ParseOpponentResponseText(llmResponse);

        public void Dispose()
        {
            _client.Dispose();
        }
    }
}
