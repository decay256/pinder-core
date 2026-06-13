using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Text;

namespace Pinder.LlmAdapters.OpenAi
{
    /// <summary>
    /// ILlmAdapter + IStatefulLlmAdapter implementation for OpenAI-compatible APIs.
    /// Supports OpenAI, Groq, Together, OpenRouter, Ollama, and any provider
    /// that implements the /v1/chat/completions endpoint.
    /// </summary>
    [Obsolete("OpenAiLlmAdapter is deprecated. Use PinderLlmAdapter instead.")]
    public sealed partial class OpenAiLlmAdapter : IStatefulLlmAdapter, IDisposable
    {
        private const double DefaultDialogueOptionsTemperature = 0.9;
        private const double DefaultDeliveryTemperature = 0.7;
        private const double DefaultDateeResponseTemperature = 0.85;

        private readonly OpenAiClient _client;
        private readonly OpenAiOptions _options;

        // #788: datee conversation state lives on GameSession, not here.
        // The adapter is pure-stateless and safe for concurrent reuse across sessions.

        /// <summary>Creates adapter with internally-owned OpenAiClient.</summary>
        public OpenAiLlmAdapter(OpenAiOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _client = new OpenAiClient(options.ApiKey, options.BaseUrl);
        }

        /// <summary>Creates adapter with externally-provided HttpClient (for testing).</summary>
        public OpenAiLlmAdapter(OpenAiOptions options, HttpClient httpClient)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            if (httpClient == null) throw new ArgumentNullException(nameof(httpClient));
            _client = new OpenAiClient(options.ApiKey, options.BaseUrl, httpClient);
        }

        /// <inheritdoc />
        public async Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context, CancellationToken ct = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var userContent = SessionDocumentBuilder.BuildDialogueOptionsPrompt(context);
            var systemPrompt = SessionSystemPromptBuilder.BuildPlayerAvatar(context.PlayerAvatarPrompt, _options.GameDefinition);

            var requestJson = BuildRequestJson(systemPrompt, userContent, DefaultDialogueOptionsTemperature);
            var responseText = await _client.SendChatCompletionAsync(requestJson, ct).ConfigureAwait(false);

            return ParseDialogueOptions(responseText);
        }

        /// <inheritdoc />
        public async Task<string> DeliverMessageAsync(DeliveryContext context, CancellationToken ct = default)
        {
            // #1123: stateless single-turn fallback. Stateful callers route
            // through the IStatefulLlmAdapter overload that takes a history.
            var result = await DeliverMessageAsync(context, System.Array.Empty<ConversationMessage>(), ct).ConfigureAwait(false);
            return result.DeliveredMessage;
        }

        /// <inheritdoc />
        public async Task<StatefulAvatarResult> DeliverMessageAsync(
            DeliveryContext context,
            IReadOnlyList<ConversationMessage> history,
            CancellationToken cancellationToken = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (history == null) throw new ArgumentNullException(nameof(history));

            var deliveryRules = _options.GameDefinition?.DeliveryRules;
            var userContent = SessionDocumentBuilder.BuildDeliveryPrompt(context, deliveryRules: deliveryRules, statDeliveryInstructions: _options.StatDeliveryInstructions);
            // #1123: shared compile path — system prompt + character spec cached
            // as the static prefix (via cache_control wrapping), transcript as
            // the volatile suffix; symmetric to the datee stateful path.
            var systemPrompt = SessionSystemPromptBuilder.BuildPlayerAvatar(context.PlayerAvatarPrompt, _options.GameDefinition);

            string requestJson;
            if (history.Count == 0)
            {
                requestJson = BuildRequestJson(systemPrompt, userContent, DefaultDeliveryTemperature);
            }
            else
            {
                requestJson = BuildStatefulRequestJson(systemPrompt, history, userContent, DefaultDeliveryTemperature);
            }

            var responseText = await _client.SendChatCompletionAsync(requestJson, cancellationToken).ConfigureAwait(false);
            string delivered = responseText ?? string.Empty;

            var newEntries = new ConversationMessage[]
            {
                ConversationMessage.User(userContent),
                ConversationMessage.Assistant(delivered),
            };
            return new StatefulAvatarResult(delivered, newEntries);
        }

        /// <inheritdoc />
        public async Task<DateeResponse> GetDateeResponseAsync(DateeContext context, CancellationToken ct = default)
        {
            // #788: stateless single-turn fallback. Stateful callers route
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

            string requestJson;
            if (history.Count == 0)
            {
                requestJson = BuildRequestJson(systemPrompt, userContent, DefaultDateeResponseTemperature);
            }
            else
            {
                requestJson = BuildStatefulRequestJson(systemPrompt, history, userContent, DefaultDateeResponseTemperature);
            }

            var responseText = await _client.SendChatCompletionAsync(requestJson, cancellationToken).ConfigureAwait(false);
            var parsed = ParseDateeResponse(responseText);

            // #866: post-LLM length validation (warn-only phase 1 — no retry)
            int playerLen = context.PlayerDeliveredMessage.Length;
            int ceiling = SessionDocumentBuilder.ComputeResponseCeiling(playerLen);
            double slopCeiling = 1.2 * ceiling;
            if (parsed.MessageText != null && parsed.MessageText.Length > slopCeiling)
            {
                Console.Error.WriteLine(
                    $"[WARN] Datee response over length ceiling (slop ceiling={slopCeiling:F0}): " +
                    $"playerLen={playerLen} ceiling={ceiling} responseLen={parsed.MessageText.Length} character={context.DateeName}");
            }

            var newEntries = new ConversationMessage[]
            {
                ConversationMessage.User(userContent),
                ConversationMessage.Assistant(responseText ?? string.Empty),
            };
            return new StatefulDateeResult(parsed, newEntries);
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

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("CONVERSATION SO FAR:");
            foreach (var (sender, text) in context.ConversationHistory)
            {
                sb.AppendLine($"{sender}: {text}");
            }
            sb.AppendLine();
            sb.AppendLine(prompt);

            string fullPlayerAvatarPrompt = SessionSystemPromptBuilder.BuildPlayerAvatar(context.PlayerAvatarPrompt, _options.GameDefinition);
            var requestJson = BuildRequestJson(fullPlayerAvatarPrompt, sb.ToString(), 0.9);
            var responseText = await _client.SendChatCompletionAsync(requestJson, ct).ConfigureAwait(false);

            // #351: strip inline <thinking>/<reasoning> blocks — the steering
            // question is consumed verbatim by the player UI.
            var question = InlineThinkingStripper.Strip(responseText).Trim();
            if (string.IsNullOrWhiteSpace(question))
                return "so... when are we doing this?";

            if (question.Length >= 2 && question[0] == '"' && question[question.Length - 1] == '"')
                question = question.Substring(1, question.Length - 2).Trim();

            return question;
        }

        /// <inheritdoc />
        public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context, CancellationToken ct = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<string?>(null);
        }

        public void Dispose()
        {
            _client.Dispose();
        }

        // ── Request building ──────────────────────────────────────────────

        private string BuildRequestJson(string systemPrompt, string userContent, double temperature)
        {
            // #947: wrap the (byte-stable, large) system prompt in a content
            // block with cache_control: ephemeral so the Anthropic /
            // OpenRouter→Anthropic path can register a prompt-cache breakpoint.
            // OpenAI accepts the content-block shape and ignores the marker.
            var systemContent = OpenAiCacheControl.BuildSystemContent(
                systemPrompt, _options.UseAnthropicCacheControl);

            var request = new
            {
                model = _options.Model,
                max_tokens = _options.MaxTokens,
                temperature = temperature,
                messages = new object[]
                {
                    new { role = "system", content = systemContent },
                    new { role = "user", content = userContent }
                }
            };
            return JsonConvert.SerializeObject(request);
        }

        /// <summary>
        /// Builds a multi-turn /v1/chat/completions request from engine-supplied
        /// history plus the current turn's user content. Pure function of inputs;
        /// no adapter-side state read or written.
        /// </summary>
        private string BuildStatefulRequestJson(
            string systemPrompt,
            IReadOnlyList<ConversationMessage> priorHistory,
            string currentUserContent,
            double temperature)
        {
            // #947: same cache_control wrapping as BuildRequestJson — the
            // stateful path is the one used for multi-turn datee-response
            // exchanges, which is exactly where prompt-cache hits matter most.
            var systemContent = OpenAiCacheControl.BuildSystemContent(
                systemPrompt, _options.UseAnthropicCacheControl);

            var messages = new List<object>();
            messages.Add(new { role = "system", content = systemContent });

            for (int i = 0; i < priorHistory.Count; i++)
            {
                var msg = priorHistory[i];
                messages.Add(new { role = msg.Role, content = msg.Content });
            }
            messages.Add(new { role = "user", content = currentUserContent });

            var request = new
            {
                model = _options.Model,
                max_tokens = _options.MaxTokens,
                temperature = temperature,
                messages = messages
            };
            return JsonConvert.SerializeObject(request);
        }
    }
}
