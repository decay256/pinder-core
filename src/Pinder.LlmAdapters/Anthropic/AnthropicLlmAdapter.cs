using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
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
    /// </summary>
    public sealed class AnthropicLlmAdapter : IStatefulLlmAdapter, IDisposable
    {
        // Default temperatures per method (used when AnthropicOptions override is null)
        private const double DefaultDialogueOptionsTemperature = 0.9;
        private const double DefaultDeliveryTemperature = 0.7;
        private const double DefaultOpponentResponseTemperature = 0.85;
        private const double DefaultInterestChangeBeatTemperature = 0.8;

        // Regex patterns for parsing LLM responses
        private static readonly Regex OptionHeaderRegex = new Regex(
            @"OPTION_\d+",
            RegexOptions.Compiled);

        private static readonly Regex StatRegex = new Regex(
            @"\[STAT:\s*(\w+)\]",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex CallbackRegex = new Regex(
            @"\[CALLBACK:\s*([^\]]+)\]",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex ComboRegex = new Regex(
            @"\[COMBO:\s*([^\]]+)\]",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex TellBonusRegex = new Regex(
            @"\[TELL_BONUS:\s*(\w+)\]",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex QuotedTextRegex = new Regex(
            @"""([^""]+)""",
            RegexOptions.Compiled);

        private static readonly Regex TellSignalRegex = new Regex(
            @"TELL:\s*(\w+)\s*\(([^)]+)\)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex WeaknessSignalRegex = new Regex(
            @"WEAKNESS:\s*(\w+)\s*-(\d+)\s*\(([^)]+)\)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Default padding stats for ParseDialogueOptions fallback
        private static readonly StatType[] DefaultPaddingStats = new[]
        {
            StatType.Charm, StatType.Honesty, StatType.Wit, StatType.Chaos
        };

        private readonly AnthropicClient _client;
        private readonly AnthropicOptions _options;
        private ConversationSession? _session;

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
        public AnthropicLlmAdapter(AnthropicOptions options, HttpClient httpClient)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            if (httpClient == null) throw new ArgumentNullException(nameof(httpClient));
            _client = new AnthropicClient(options.ApiKey, httpClient);
        }

        /// <summary>
        /// Whether a stateful conversation session is currently active.
        /// When true, all ILlmAdapter calls route through the accumulated session.
        /// When false, behavior is identical to current stateless mode.
        /// </summary>
        public bool HasActiveConversation => _session != null;

        /// <summary>
        /// Start a stateful conversation session with the given system prompt.
        /// After calling this, all ILlmAdapter method calls will append to and
        /// read from the accumulated message history instead of building
        /// fresh single-message requests.
        ///
        /// Calling this when a session is already active replaces it (no error).
        /// </summary>
        /// <param name="systemPrompt">
        /// Full system prompt (both character profiles, game vision, etc.).
        /// Must not be null or empty.
        /// </param>
        /// <exception cref="ArgumentException">If systemPrompt is null or whitespace.</exception>
        public void StartConversation(string systemPrompt)
        {
            _session = new ConversationSession(systemPrompt);
        }

        /// <inheritdoc />
        public async Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            // Opponent profile is passed as informational context in the user message
            var userContent = SessionDocumentBuilder.BuildDialogueOptionsPrompt(context);

            if (_session != null)
            {
                _session.AppendUser(userContent);
                var statefulRequest = _session.BuildRequest(
                    _options.Model, _options.MaxTokens,
                    _options.DialogueOptionsTemperature ?? DefaultDialogueOptionsTemperature);
                var statefulResponse = await _client.SendMessagesAsync(statefulRequest).ConfigureAwait(false);
                var statefulText = statefulResponse.GetText();
                _session.AppendAssistant(statefulText);
                return ParseDialogueOptions(statefulText);
            }

            // Only the player's identity in system — prevents voice bleed from opponent's register
            var systemBlocks = CacheBlockBuilder.BuildPlayerOnlySystemBlocks(context.PlayerPrompt);

            var request = BuildRequest(systemBlocks, userContent,
                _options.DialogueOptionsTemperature ?? DefaultDialogueOptionsTemperature);

            var response = await _client.SendMessagesAsync(request).ConfigureAwait(false);
            return ParseDialogueOptions(response.GetText());
        }

        /// <inheritdoc />
        public async Task<string> DeliverMessageAsync(DeliveryContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var userContent = SessionDocumentBuilder.BuildDeliveryPrompt(context);

            if (_session != null)
            {
                _session.AppendUser(userContent);
                var statefulRequest = _session.BuildRequest(
                    _options.Model, _options.MaxTokens,
                    _options.DeliveryTemperature ?? DefaultDeliveryTemperature);
                var statefulResponse = await _client.SendMessagesAsync(statefulRequest).ConfigureAwait(false);
                var statefulText = statefulResponse.GetText();
                _session.AppendAssistant(statefulText);
                return statefulText;
            }

            var systemBlocks = CacheBlockBuilder.BuildPlayerOnlySystemBlocks(context.PlayerPrompt);

            var request = BuildRequest(systemBlocks, userContent,
                _options.DeliveryTemperature ?? DefaultDeliveryTemperature);

            var response = await _client.SendMessagesAsync(request).ConfigureAwait(false);
            return response.GetText();
        }

        /// <inheritdoc />
        public async Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var userContent = SessionDocumentBuilder.BuildOpponentPrompt(context);

            if (_session != null)
            {
                _session.AppendUser(userContent);
                var statefulRequest = _session.BuildRequest(
                    _options.Model, _options.MaxTokens,
                    _options.OpponentResponseTemperature ?? DefaultOpponentResponseTemperature);
                var statefulResponse = await _client.SendMessagesAsync(statefulRequest).ConfigureAwait(false);
                var statefulText = statefulResponse.GetText();
                _session.AppendAssistant(statefulText);
                return ParseOpponentResponse(statefulText);
            }

            // Per §3.5: only opponent prompt in system (opponent plays themselves)
            var systemBlocks = CacheBlockBuilder.BuildOpponentOnlySystemBlocks(context.OpponentPrompt);

            var request = BuildRequest(systemBlocks, userContent,
                _options.OpponentResponseTemperature ?? DefaultOpponentResponseTemperature);

            var response = await _client.SendMessagesAsync(request).ConfigureAwait(false);
            return ParseOpponentResponse(response.GetText());
        }

        /// <inheritdoc />
        public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            // Per Issue #573: The LLM call for NarrativeBeat has been removed.
            // Returning null avoids making an unnecessary API call.
            return Task.FromResult<string?>(null);
        }

        /// <summary>Disposes the internal AnthropicClient.</summary>
        public void Dispose()
        {
            _client.Dispose();
        }

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
                    // Split by OPTION_N headers
                    var sections = OptionHeaderRegex.Split(llmResponse);

                    foreach (var section in sections)
                    {
                        if (string.IsNullOrWhiteSpace(section)) continue;
                        if (parsed.Count >= 4) break;

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
                            continue; // Invalid stat — skip this option
                        }

                        var textMatch = QuotedTextRegex.Match(section);
                        if (!textMatch.Success) continue; // No text = invalid option

                        var text = textMatch.Groups[1].Value.Trim();
                        if (string.IsNullOrEmpty(text)) continue;

                        // Parse optional metadata
                        int? callbackTurn = null;
                        var callbackMatch = CallbackRegex.Match(section);
                        if (callbackMatch.Success)
                        {
                            var cbVal = callbackMatch.Groups[1].Value.Trim();
                            if (!string.Equals(cbVal, "none", StringComparison.OrdinalIgnoreCase))
                            {
                                // Try extracting numeric value
                                if (int.TryParse(cbVal, out int turnNum))
                                {
                                    callbackTurn = turnNum;
                                }
                                // Also try "turn_N" pattern
                                else if (cbVal.StartsWith("turn_", StringComparison.OrdinalIgnoreCase) &&
                                         int.TryParse(cbVal.Substring(5), out int turnNum2))
                                {
                                    callbackTurn = turnNum2;
                                }
                                // Non-numeric callback reference — cannot resolve to int
                            }
                        }

                        string? comboName = null;
                        var comboMatch = ComboRegex.Match(section);
                        if (comboMatch.Success)
                        {
                            var comboVal = comboMatch.Groups[1].Value.Trim();
                            if (!string.Equals(comboVal, "none", StringComparison.OrdinalIgnoreCase))
                            {
                                comboName = comboVal;
                            }
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
                    // Swallow any unexpected parse error — we'll pad with defaults below
                }
            }

            // Pad to exactly 4 options with defaults
            return PadToFour(parsed);
        }

        /// <summary>
        /// Parses structured LLM output with optional [SIGNALS] blocks.
        /// Never throws — returns OpponentResponse with null signals on parse failure.
        /// </summary>
        internal static OpponentResponse ParseOpponentResponse(string? llmResponse)
        {
            if (string.IsNullOrWhiteSpace(llmResponse))
            {
                return new OpponentResponse("", null, null);
            }

            // llmResponse is guaranteed non-null after the above check
            var response = llmResponse!;
            string messageText;
            Tell? tell = null;
            WeaknessWindow? weakness = null;

            try
            {
                // Extract message text (everything before [SIGNALS])
                var signalIdx = response.IndexOf("[SIGNALS]", StringComparison.OrdinalIgnoreCase);
                messageText = signalIdx >= 0
                    ? response.Substring(0, signalIdx).Trim()
                    : response.Trim();

                // Strip [RESPONSE] tag if the LLM still generates it
                var responseTagIdx = messageText.IndexOf("[RESPONSE]", StringComparison.OrdinalIgnoreCase);
                if (responseTagIdx >= 0)
                {
                    messageText = messageText.Substring(responseTagIdx + "[RESPONSE]".Length).Trim();
                }

                // Strip surrounding quotes if present
                if (messageText.Length >= 2 && messageText[0] == '"' && messageText[messageText.Length - 1] == '"')
                {
                    messageText = messageText.Substring(1, messageText.Length - 2).Trim();
                }

                // Parse optional [SIGNALS] block
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
                        catch (ArgumentException)
                        {
                            // Invalid stat — tell stays null
                        }
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
                            {
                                weakness = new WeaknessWindow(stat, reduction);
                            }
                        }
                        catch (Exception)
                        {
                            // Invalid stat or reduction — weakness stays null
                        }
                    }
                }
            }
            catch
            {
                // Any unexpected error — return empty response
                return new OpponentResponse(response.Trim(), null, null);
            }

            return new OpponentResponse(messageText, tell, weakness);
        }

        private MessagesRequest BuildRequest(ContentBlock[] systemBlocks, string userContent, double temperature)
        {
            return new MessagesRequest
            {
                Model = _options.Model,
                MaxTokens = _options.MaxTokens,
                Temperature = temperature,
                System = systemBlocks,
                Messages = new[]
                {
                    new Message { Role = "user", Content = userContent }
                }
            };
        }

        /// <summary>
        /// Normalizes LLM stat names like "SELF_AWARENESS" to C# enum names like "SelfAwareness".
        /// </summary>
        private static string NormalizeStatName(string raw)
        {
            if (string.Equals(raw, "SELF_AWARENESS", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(raw, "SELFAWARENESS", StringComparison.OrdinalIgnoreCase))
            {
                return "SelfAwareness";
            }
            return raw;
        }

        /// <summary>Pads parsed options to exactly 4 using default stats not already present.</summary>
        private static DialogueOption[] PadToFour(List<DialogueOption> parsed)
        {
            if (parsed.Count >= 4)
            {
                return parsed.GetRange(0, 4).ToArray();
            }

            var usedStats = new HashSet<StatType>();
            foreach (var opt in parsed)
            {
                usedStats.Add(opt.Stat);
            }

            var result = new List<DialogueOption>(parsed);
            foreach (var defaultStat in DefaultPaddingStats)
            {
                if (result.Count >= 4) break;
                if (usedStats.Contains(defaultStat)) continue;
                result.Add(new DialogueOption(defaultStat, "...",
                    callbackTurnNumber: null, comboName: null,
                    hasTellBonus: false, hasWeaknessWindow: false));
            }

            // If we still need more (e.g., all 4 default stats were used), just pad with Charm
            while (result.Count < 4)
            {
                result.Add(new DialogueOption(StatType.Charm, "...",
                    callbackTurnNumber: null, comboName: null,
                    hasTellBonus: false, hasWeaknessWindow: false));
            }

            return result.ToArray();
        }


    }
}
