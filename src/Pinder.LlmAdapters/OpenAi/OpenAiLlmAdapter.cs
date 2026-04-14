using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;
using Pinder.LlmAdapters.Anthropic;
using Pinder.LlmAdapters.Anthropic.Dto;

namespace Pinder.LlmAdapters.OpenAi
{
    /// <summary>
    /// ILlmAdapter + IStatefulLlmAdapter implementation for OpenAI-compatible APIs.
    /// Supports OpenAI, Groq, Together, OpenRouter, Ollama, and any provider
    /// that implements the /v1/chat/completions endpoint.
    /// </summary>
    public sealed class OpenAiLlmAdapter : IStatefulLlmAdapter, IDisposable
    {
        private const double DefaultDialogueOptionsTemperature = 0.9;
        private const double DefaultDeliveryTemperature = 0.7;
        private const double DefaultOpponentResponseTemperature = 0.85;

        // Regex patterns — same as AnthropicLlmAdapter
        private static readonly Regex OptionHeaderRegex = new Regex(
            @"OPTION_\d+", RegexOptions.Compiled);
        private static readonly Regex StatRegex = new Regex(
            @"\[STAT:\s*(\w+)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex CallbackRegex = new Regex(
            @"\[CALLBACK:\s*([^\]]+)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ComboRegex = new Regex(
            @"\[COMBO:\s*([^\]]+)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex TellBonusRegex = new Regex(
            @"\[TELL_BONUS:\s*(\w+)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex QuotedTextRegex = new Regex(
            @"""([^""]+)""", RegexOptions.Compiled);
        private static readonly Regex TellSignalRegex = new Regex(
            @"TELL:\s*(\w+)\s*\(([^)]+)\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex WeaknessSignalRegex = new Regex(
            @"WEAKNESS:\s*(\w+)\s*-(\d+)\s*\(([^)]+)\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly StatType[] DefaultPaddingStats = new[]
        {
            StatType.Charm, StatType.Honesty, StatType.Wit, StatType.Chaos
        };

        private readonly OpenAiClient _client;
        private readonly OpenAiOptions _options;

        // Stateful opponent session
        private ConversationSession? _opponentSession;
        private string? _opponentSystemPrompt;

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
            var systemPrompt = SessionSystemPromptBuilder.BuildPlayer(context.PlayerPrompt, _options.GameDefinition);

            var requestJson = BuildRequestJson(systemPrompt, userContent, DefaultDialogueOptionsTemperature);
            var responseText = await _client.SendChatCompletionAsync(requestJson).ConfigureAwait(false);

            return ParseDialogueOptions(responseText);
        }

        /// <inheritdoc />
        public async Task<string> DeliverMessageAsync(DeliveryContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var deliveryRules = _options.GameDefinition?.DeliveryRules;
            var userContent = SessionDocumentBuilder.BuildDeliveryPrompt(context, deliveryRules: deliveryRules, statDeliveryInstructions: _options.StatDeliveryInstructions);
            var systemPrompt = SessionSystemPromptBuilder.BuildPlayer(context.PlayerPrompt, _options.GameDefinition);

            var requestJson = BuildRequestJson(systemPrompt, userContent, DefaultDeliveryTemperature);
            var responseText = await _client.SendChatCompletionAsync(requestJson).ConfigureAwait(false);

            return responseText;
        }

        /// <inheritdoc />
        public async Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var userContent = SessionDocumentBuilder.BuildOpponentPrompt(context);
            var systemPrompt = SessionSystemPromptBuilder.BuildOpponent(context.OpponentPrompt, _options.GameDefinition);

            string requestJson;
            if (_opponentSession != null)
            {
                // Stateful: accumulate messages
                _opponentSession.AppendUser(userContent);
                requestJson = BuildStatefulRequestJson(systemPrompt, _opponentSession, DefaultOpponentResponseTemperature);
            }
            else
            {
                requestJson = BuildRequestJson(systemPrompt, userContent, DefaultOpponentResponseTemperature);
            }

            var responseText = await _client.SendChatCompletionAsync(requestJson).ConfigureAwait(false);

            if (_opponentSession != null)
            {
                _opponentSession.AppendAssistant(responseText);
            }

            return ParseOpponentResponse(responseText);
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

            string fullPlayerPrompt = SessionSystemPromptBuilder.BuildPlayer(context.PlayerPrompt, _options.GameDefinition);
            var requestJson = BuildRequestJson(fullPlayerPrompt, sb.ToString(), 0.9);
            var responseText = await _client.SendChatCompletionAsync(requestJson).ConfigureAwait(false);

            var question = responseText?.Trim();
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

        public void Dispose()
        {
            _client.Dispose();
        }

        // ── Request building ──────────────────────────────────────────────

        private string BuildRequestJson(string systemPrompt, string userContent, double temperature)
        {
            var request = new
            {
                model = _options.Model,
                max_tokens = _options.MaxTokens,
                temperature = temperature,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userContent }
                }
            };
            return JsonConvert.SerializeObject(request);
        }

        private string BuildStatefulRequestJson(string systemPrompt, ConversationSession session, double temperature)
        {
            // Build messages array: system + all accumulated conversation messages
            var messages = new List<object>();
            messages.Add(new { role = "system", content = systemPrompt });

            // Extract messages from the ConversationSession via its BuildRequest method
            // We build an Anthropic request to get the messages, then translate
            var anthropicRequest = session.BuildRequest(
                _options.Model,
                _options.MaxTokens,
                temperature,
                new ContentBlock[] { new ContentBlock { Type = "text", Text = systemPrompt } });

            foreach (var msg in anthropicRequest.Messages)
            {
                messages.Add(new { role = msg.Role, content = msg.Content });
            }

            var request = new
            {
                model = _options.Model,
                max_tokens = _options.MaxTokens,
                temperature = temperature,
                messages = messages
            };
            return JsonConvert.SerializeObject(request);
        }

        // ── Response parsing (duplicated from AnthropicLlmAdapter) ────────

        /// <summary>
        /// Parses structured LLM output into DialogueOption array.
        /// Never throws — returns 4 options, padding with defaults if needed.
        /// </summary>
        internal static DialogueOption[] ParseDialogueOptions(string? llmResponse)
        {
            var parsed = new List<DialogueOption>();

            if (!string.IsNullOrWhiteSpace(llmResponse))
            {
                try
                {
                    var sections = OptionHeaderRegex.Split(llmResponse);

                    foreach (var section in sections)
                    {
                        if (string.IsNullOrWhiteSpace(section)) continue;
                        if (parsed.Count >= 3) break;

                        var statMatch = StatRegex.Match(section);
                        if (!statMatch.Success) continue;

                        var statStr = NormalizeStatName(statMatch.Groups[1].Value.Trim());
                        StatType stat;
                        try
                        {
                            stat = (StatType)Enum.Parse(typeof(StatType), statStr, true);
                        }
                        catch (ArgumentException)
                        {
                            continue;
                        }

                        var textMatch = QuotedTextRegex.Match(section);
                        if (!textMatch.Success) continue;

                        var text = textMatch.Groups[1].Value.Trim();
                        if (string.IsNullOrEmpty(text)) continue;

                        int? callbackTurn = null;
                        var callbackMatch = CallbackRegex.Match(section);
                        if (callbackMatch.Success)
                        {
                            var cbVal = callbackMatch.Groups[1].Value.Trim();
                            if (!string.Equals(cbVal, "none", StringComparison.OrdinalIgnoreCase))
                            {
                                if (int.TryParse(cbVal, out int turnNum))
                                    callbackTurn = turnNum;
                                else if (cbVal.StartsWith("turn_", StringComparison.OrdinalIgnoreCase) &&
                                         int.TryParse(cbVal.Substring(5), out int turnNum2))
                                    callbackTurn = turnNum2;
                            }
                        }

                        string? comboName = null;
                        var comboMatch = ComboRegex.Match(section);
                        if (comboMatch.Success)
                        {
                            var comboVal = comboMatch.Groups[1].Value.Trim();
                            if (!string.Equals(comboVal, "none", StringComparison.OrdinalIgnoreCase))
                                comboName = comboVal;
                        }

                        bool hasTellBonus = false;
                        var tellMatch = TellBonusRegex.Match(section);
                        if (tellMatch.Success)
                        {
                            hasTellBonus = string.Equals(
                                tellMatch.Groups[1].Value.Trim(), "yes", StringComparison.OrdinalIgnoreCase);
                        }

                        parsed.Add(new DialogueOption(
                            stat, text, callbackTurn, comboName, hasTellBonus, hasWeaknessWindow: false));
                    }
                }
                catch
                {
                    // Swallow — pad below
                }
            }

            return PadToFour(parsed);
        }

        /// <summary>
        /// Parses structured LLM output with optional [SIGNALS] blocks.
        /// Never throws — returns OpponentResponse with null signals on parse failure.
        /// </summary>
        internal static OpponentResponse ParseOpponentResponse(string? llmResponse)
        {
            if (string.IsNullOrWhiteSpace(llmResponse))
                return new OpponentResponse("", null, null);

            var response = llmResponse!;
            string messageText;
            Tell? tell = null;
            WeaknessWindow? weakness = null;

            try
            {
                var signalIdx = response.IndexOf("[SIGNALS]", StringComparison.OrdinalIgnoreCase);
                messageText = signalIdx >= 0
                    ? response.Substring(0, signalIdx).Trim()
                    : response.Trim();

                var responseTagIdx = messageText.IndexOf("[RESPONSE]", StringComparison.OrdinalIgnoreCase);
                if (responseTagIdx >= 0)
                    messageText = messageText.Substring(responseTagIdx + "[RESPONSE]".Length).Trim();

                if (messageText.Length >= 2 && messageText[0] == '"' && messageText[messageText.Length - 1] == '"')
                    messageText = messageText.Substring(1, messageText.Length - 2).Trim();

                var signalsIndex = response.IndexOf("[SIGNALS]", StringComparison.OrdinalIgnoreCase);
                if (signalsIndex >= 0)
                {
                    var signalsBlock = response.Substring(signalsIndex);

                    var tellMatch = TellSignalRegex.Match(signalsBlock);
                    if (tellMatch.Success)
                    {
                        var statStr = NormalizeStatName(tellMatch.Groups[1].Value.Trim());
                        try
                        {
                            var stat = (StatType)Enum.Parse(typeof(StatType), statStr, true);
                            var description = tellMatch.Groups[2].Value.Trim();
                            tell = new Tell(stat, description);
                        }
                        catch (ArgumentException) { }
                    }

                    var weaknessMatch = WeaknessSignalRegex.Match(signalsBlock);
                    if (weaknessMatch.Success)
                    {
                        var statStr = NormalizeStatName(weaknessMatch.Groups[1].Value.Trim());
                        try
                        {
                            var stat = (StatType)Enum.Parse(typeof(StatType), statStr, true);
                            var reduction = int.Parse(weaknessMatch.Groups[2].Value.Trim());
                            if (reduction > 0)
                                weakness = new WeaknessWindow(stat, reduction);
                        }
                        catch (Exception) { }
                    }
                }
            }
            catch
            {
                return new OpponentResponse(response.Trim(), null, null);
            }

            return new OpponentResponse(messageText, tell, weakness);
        }

        private static string NormalizeStatName(string raw)
        {
            if (string.Equals(raw, "SELF_AWARENESS", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(raw, "SELFAWARENESS", StringComparison.OrdinalIgnoreCase))
                return "SelfAwareness";
            return raw;
        }

        private static DialogueOption[] PadToFour(List<DialogueOption> parsed)
        {
            if (parsed.Count >= 4)
                return parsed.GetRange(0, 4).ToArray();

            var usedStats = new HashSet<StatType>();
            foreach (var opt in parsed)
                usedStats.Add(opt.Stat);

            var result = new List<DialogueOption>(parsed);
            foreach (var defaultStat in DefaultPaddingStats)
            {
                if (result.Count >= 4) break;
                if (usedStats.Contains(defaultStat)) continue;
                result.Add(new DialogueOption(defaultStat, "...",
                    callbackTurnNumber: null, comboName: null,
                    hasTellBonus: false, hasWeaknessWindow: false));
            }

            while (result.Count < 4)
            {
                result.Add(new DialogueOption(StatType.Charm, "...",
                    callbackTurnNumber: null, comboName: null,
                    hasTellBonus: false, hasWeaknessWindow: false));
            }

            return result.ToArray();
        }

        /// <summary>
        /// Apply a horniness overlay — returns input unchanged (OpenAI overlay not yet implemented).
        /// </summary>
        public Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? opponentContext = null)
        {
            return Task.FromResult(message);
        }

        /// <inheritdoc />
        public Task<string> ApplyShadowCorruptionAsync(string message, string instruction, ShadowStatType shadow)
        {
            // Shadow corruption via OpenAI transport — returns input unchanged (not yet implemented).
            return Task.FromResult(message);
        }
    }
}
