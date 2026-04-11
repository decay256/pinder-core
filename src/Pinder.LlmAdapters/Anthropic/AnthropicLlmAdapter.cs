using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;
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
    public class CallSummaryStat
    {
        [JsonProperty("turn")] public int Turn { get; set; }
        [JsonProperty("type")] public string Type { get; set; }
        [JsonProperty("cache_creation_input_tokens")] public int CacheCreationInputTokens { get; set; }
        [JsonProperty("cache_read_input_tokens")] public int CacheReadInputTokens { get; set; }
        [JsonProperty("input_tokens")] public int InputTokens { get; set; }
        [JsonProperty("output_tokens")] public int OutputTokens { get; set; }
    }

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
        private readonly List<CallSummaryStat> _callStats = new List<CallSummaryStat>();

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
        public AnthropicLlmAdapter(AnthropicOptions options, HttpClient httpClient)
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

            // Opponent profile is passed as informational context in the user message
            var userContent = SessionDocumentBuilder.BuildDialogueOptionsPrompt(context);


            // Only the player's identity in system — prevents voice bleed from opponent's register
            var fullPlayerPrompt = SessionSystemPromptBuilder.BuildPlayer(context.PlayerPrompt, _options.GameDefinition);
            var systemBlocks = CacheBlockBuilder.BuildPlayerOnlySystemBlocks(fullPlayerPrompt);

            var request = BuildRequest(systemBlocks, userContent,
                _options.DialogueOptionsTemperature ?? DefaultDialogueOptionsTemperature);
            AttachTool(request, ToolSchemas.DialogueOptions);

            var response = await _client.SendMessagesAsync(request).ConfigureAwait(false);
            LogDebug("options", context.CurrentTurn, request, response);

            // Try structured tool_use first, fall back to text parsing
            var toolInput = response.GetToolInput();
            if (toolInput != null)
            {
                var parsed = ParseDialogueOptionsFromTool(toolInput);
                if (parsed != null) return parsed;
            }

            var optionsDraft = response.GetText();
            // Skip improvement pass on T1 — conversation history is empty, improvement
            // loop incorrectly generates options assuming prior exchanges exist
            string optionsText = context.CurrentTurn <= 1
                ? optionsDraft
                : await ApplyImprovementAsync(
                    systemBlocks, userContent, optionsDraft,
                    _options.DialogueOptionsTemperature ?? DefaultDialogueOptionsTemperature).ConfigureAwait(false);
            return ParseDialogueOptions(optionsText);
        }

        /// <inheritdoc />
        public async Task<string> DeliverMessageAsync(DeliveryContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var deliveryRules = _options.GameDefinition?.DeliveryRules;
            var userContent = SessionDocumentBuilder.BuildDeliveryPrompt(context, deliveryRules: deliveryRules, statDeliveryInstructions: _options.StatDeliveryInstructions);


            var fullPlayerPrompt = SessionSystemPromptBuilder.BuildPlayer(context.PlayerPrompt, _options.GameDefinition);
            var systemBlocks = CacheBlockBuilder.BuildPlayerOnlySystemBlocks(fullPlayerPrompt);

            var request = BuildRequest(systemBlocks, userContent,
                _options.DeliveryTemperature ?? DefaultDeliveryTemperature);
            AttachTool(request, ToolSchemas.Delivery);

            var response = await _client.SendMessagesAsync(request).ConfigureAwait(false);
            LogDebug("delivery", context.CurrentTurn, request, response);

            // Try structured tool_use first
            var toolInput = response.GetToolInput();
            if (toolInput != null)
            {
                var delivered = toolInput.Value<string>("delivered");
                if (!string.IsNullOrWhiteSpace(delivered))
                    return delivered;
            }

            var deliveryDraft = response.GetText();

            // Only apply improvement on notable outcomes — not clean success or fumble.
            // Clean success means deliver as written; improvement would corrupt that intent.
            // Fumble is a minor stumble; catastrophe/nat1 have their own escalation logic.
            // Skip improvement at T1 — conversation history is empty and improvement loop
            // can invent imagined prior exchanges.
            bool applyImprovement = context.CurrentTurn > 1 && (
                context.Outcome == Pinder.Core.Rolls.FailureTier.None
                    ? context.BeatDcBy >= 5 || context.IsNat20  // strong, crit, exceptional, nat20
                    : context.Outcome >= Pinder.Core.Rolls.FailureTier.Misfire); // misfire+, not fumble

            return applyImprovement
                ? await ApplyImprovementAsync(systemBlocks, userContent, deliveryDraft, _options.DeliveryTemperature ?? DefaultDeliveryTemperature).ConfigureAwait(false)
                : deliveryDraft;
        }

        /// <inheritdoc />
        public async Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var userContent = SessionDocumentBuilder.BuildOpponentPrompt(context);


            // Per §3.5: only opponent prompt in system (opponent plays themselves)
            var fullOpponentPrompt = SessionSystemPromptBuilder.BuildOpponent(context.OpponentPrompt, _options.GameDefinition);
            var systemBlocks = CacheBlockBuilder.BuildOpponentOnlySystemBlocks(fullOpponentPrompt);

            MessagesRequest request;
            if (_opponentSession != null)
            {
                // Stateful path: append user message to persistent session, build request from accumulated history
                _opponentSession.AppendUser(userContent);
                request = _opponentSession.BuildRequest(
                    _options.Model,
                    _options.MaxTokens,
                    _options.OpponentResponseTemperature ?? DefaultOpponentResponseTemperature,
                    systemBlocks);
            }
            else
            {
                // Stateless fallback: single-message request as before
                request = BuildRequest(systemBlocks, userContent,
                    _options.OpponentResponseTemperature ?? DefaultOpponentResponseTemperature);
            }
            AttachTool(request, ToolSchemas.OpponentResponse);

            var response = await _client.SendMessagesAsync(request).ConfigureAwait(false);
            LogDebug("opponent", context.CurrentTurn, request, response);

            // Try structured tool_use first
            var toolInput = response.GetToolInput();
            if (toolInput != null)
            {
                var parsed = ParseOpponentResponseFromTool(toolInput);
                if (parsed != null)
                {
                    // Stateful path: append assistant response to persistent session
                    if (_opponentSession != null)
                    {
                        _opponentSession.AppendAssistant(parsed.MessageText);
                    }
                    return parsed;
                }
            }

            // Fallback: text parsing with improvement pass
            var responseText = response.GetText();
            responseText = await ApplyImprovementAsync(
                systemBlocks, userContent, responseText,
                _options.OpponentResponseTemperature ?? DefaultOpponentResponseTemperature).ConfigureAwait(false);

            // Stateful path: append assistant response to persistent session
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

            // Build steering prompt from game definition or default
            string template = _options.GameDefinition?.SteeringPrompt;
            if (string.IsNullOrWhiteSpace(template))
                template = GameDefinition.DefaultSteeringPrompt;

            string prompt = template
                .Replace("{player_name}", context.PlayerName)
                .Replace("{opponent_name}", context.OpponentName)
                .Replace("{delivered_message}", context.DeliveredMessage);

            // Build conversation context
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

            var request = BuildRequest(systemBlocks, sb.ToString(), 0.9);
            var response = await _client.SendMessagesAsync(request).ConfigureAwait(false);

            var question = response.GetText()?.Trim();
            if (string.IsNullOrWhiteSpace(question))
                return "so... when are we doing this?";

            // Strip surrounding quotes if present
            if (question.Length >= 2 && question[0] == '"' && question[question.Length - 1] == '"')
                question = question.Substring(1, question.Length - 2).Trim();

            return question;
        }

        /// <inheritdoc />
        public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            // Per Issue #573: The LLM call for NarrativeBeat has been removed.
            // Returning null avoids making an unnecessary API call.
            return Task.FromResult<string?>(null);
        }

        private static readonly object _debugLock = new object();
        private bool _debugHeaderWritten;

        /// <summary>Appends a markdown section for one LLM call to the debug transcript file.</summary>
        private void LogDebug(string callType, int turn, MessagesRequest request, MessagesResponse response)
        {
            if (string.IsNullOrEmpty(_options.DebugDirectory)) return;

            try
            {
                // Track token stats regardless of file write success
                if (response.Usage != null)
                {
                    _callStats.Add(new CallSummaryStat
                    {
                        Turn = turn,
                        Type = callType,
                        CacheCreationInputTokens = response.Usage.CacheCreationInputTokens,
                        CacheReadInputTokens = response.Usage.CacheReadInputTokens,
                        InputTokens = response.Usage.InputTokens,
                        OutputTokens = response.Usage.OutputTokens
                    });
                }

                var sb = new System.Text.StringBuilder();
                string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + " UTC";
                string callLabel = callType.ToUpperInvariant();

                // Turn header: emit before the first call of each turn (options)
                if (string.Equals(callType, "options", StringComparison.OrdinalIgnoreCase))
                {
                    if (turn > 1) sb.AppendLine();
                    sb.AppendLine($"## Turn {turn}");
                    sb.AppendLine();
                }

                // Full system prompt (all blocks)
                var systemBlocks = new System.Text.StringBuilder();
                if (request.System != null)
                {
                    foreach (var block in request.System)
                    {
                        if (!string.IsNullOrEmpty(block.Text))
                            systemBlocks.AppendLine(block.Text);
                    }
                }
                string fullSystemPrompt = systemBlocks.ToString().TrimEnd();

                // REQUEST section
                sb.AppendLine($"### {callLabel} REQUEST [{timestamp}]");
                if (!string.IsNullOrEmpty(fullSystemPrompt))
                {
                    sb.AppendLine("**System prompt:**");
                    sb.AppendLine("```");
                    sb.AppendLine(fullSystemPrompt);
                    sb.AppendLine("```");
                }
                sb.AppendLine();

                if (string.Equals(callType, "opponent", StringComparison.OrdinalIgnoreCase))
                {
                    // Opponent: show message count + only the last user message
                    int msgCount = request.Messages != null ? request.Messages.Length : 0;
                    sb.AppendLine($"**Context window:** {msgCount} messages accumulated");
                    sb.AppendLine();

                    string lastUserMsg = "";
                    if (request.Messages != null)
                    {
                        for (int i = request.Messages.Length - 1; i >= 0; i--)
                        {
                            if (string.Equals(request.Messages[i].Role, "user", StringComparison.OrdinalIgnoreCase))
                            {
                                lastUserMsg = request.Messages[i].Content;
                                break;
                            }
                        }
                    }
                    sb.AppendLine("**New user message (this turn):**");
                    sb.AppendLine("```");
                    sb.AppendLine(lastUserMsg);
                    sb.AppendLine("```");
                }
                else
                {
                    // Options/Delivery: show full user message
                    string userMsg = "";
                    if (request.Messages != null && request.Messages.Length > 0)
                        userMsg = request.Messages[0].Content ?? "";
                    sb.AppendLine("**User message:**");
                    sb.AppendLine("```");
                    sb.AppendLine(userMsg);
                    sb.AppendLine("```");
                }
                sb.AppendLine();

                // RESPONSE section
                string responseTimestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + " UTC";
                sb.AppendLine($"### {callLabel} RESPONSE [{responseTimestamp}]");
                sb.AppendLine("```");
                sb.AppendLine(response.GetText());
                sb.AppendLine("```");
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine();

                lock (_debugLock)
                {
                    // Ensure parent directory exists
                    string dir = Path.GetDirectoryName(_options.DebugDirectory);
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);

                    // Write header on first append
                    if (!_debugHeaderWritten)
                    {
                        File.WriteAllText(_options.DebugDirectory, $"# Session Debug Transcript\n\n---\n\n");
                        _debugHeaderWritten = true;
                    }

                    File.AppendAllText(_options.DebugDirectory, sb.ToString());
                }
            }
            catch
            {
                // Ignore logging errors
            }
        }

        /// <summary>Writes the token summary table to the end of the debug transcript.</summary>
        public void WriteDebugSummary()
        {
            if (string.IsNullOrEmpty(_options.DebugDirectory)) return;
            if (_callStats.Count == 0) return;

            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("## Token Summary");
                sb.AppendLine("| Turn | Call | Input | Output | Cache Read | Cache Write |");
                sb.AppendLine("|------|------|-------|--------|------------|-------------|");

                foreach (var stat in _callStats)
                {
                    sb.AppendLine($"| {stat.Turn} | {stat.Type} | {stat.InputTokens} | {stat.OutputTokens} | {stat.CacheReadInputTokens} | {stat.CacheCreationInputTokens} |");
                }

                int totalInput = _callStats.Sum(s => s.InputTokens);
                int totalOutput = _callStats.Sum(s => s.OutputTokens);
                int totalCacheRead = _callStats.Sum(s => s.CacheReadInputTokens);
                int totalCacheWrite = _callStats.Sum(s => s.CacheCreationInputTokens);
                sb.AppendLine($"| **Total** | | **{totalInput}** | **{totalOutput}** | **{totalCacheRead}** | **{totalCacheWrite}** |");
                sb.AppendLine();

                lock (_debugLock)
                {
                    File.AppendAllText(_options.DebugDirectory, sb.ToString());
                }
            }
            catch
            {
                // Ignore logging errors
            }
        }

        public void Dispose()
        {
            _client.Dispose();
        }

        /// <summary>
        /// Parses structured LLM output into DialogueOption array.
        /// Never throws — returns 3 options, padding with defaults if needed.
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

            // Pad to exactly 3 options with defaults
            return PadToThree(parsed);
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

                // Strip improvement-loop evaluation headers that leaked into the response.
                // Pattern: numbered evaluation lines followed by the actual content.
                // Detect by looking for the evaluation block ending marker.
                var evalEndMarkers = new[] {
                    "The content works as written.",
                    "content works as written",
                    "4. AUDIENCE:",
                    "4. Audience:"
                };
                foreach (var marker in evalEndMarkers)
                {
                    var markerIdx = messageText.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                    if (markerIdx >= 0)
                    {
                        // Content after the marker is the actual message
                        var afterMarker = messageText.Substring(markerIdx + marker.Length).Trim();
                        if (!string.IsNullOrWhiteSpace(afterMarker))
                            messageText = afterMarker;
                        break;
                    }
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

        /// <summary>
        /// Strips evaluation block headers from improvement output.
        /// The model sometimes outputs numbered evaluation then the content.
        /// Heuristic: if text starts with "1." or contains evaluation end markers,
        /// find where the actual content starts.
        /// </summary>
        private static string StripImprovementEvaluation(string text, string originalDraft)
        {
            // End markers that signal the transition from evaluation to content
            var endMarkers = new[]
            {
                "The content works as written.",
                "content works as written",
                "4. AUDIENCE:",
                "4. Audience:",
                "No changes needed.",
                "no changes needed"
            };

            foreach (var marker in endMarkers)
            {
                var idx = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    var after = text.Substring(idx + marker.Length).Trim();
                    // If there's content after the marker, that's the actual message
                    if (!string.IsNullOrWhiteSpace(after))
                        return after;
                    // If nothing after, the model declared no changes — return original draft
                    return originalDraft;
                }
            }

            // If text starts with evaluation-style numbering ("1. PROGRESSION", "1. "),
            // but doesn't contain an end marker, try to find where the actual content is
            // by looking for the last paragraph that doesn't start with a number.
            if (text.Length > 2 && text[0] == '1' && text[1] == '.')
            {
                // Split into lines and find the last substantive block not starting with N.
                var lines = text.Split('\n');
                int lastEvalLine = -1;
                for (int i = 0; i < lines.Length; i++)
                {
                    var trimmed = lines[i].Trim();
                    if (trimmed.Length > 1 && char.IsDigit(trimmed[0]) && trimmed[1] == '.')
                        lastEvalLine = i;
                }
                if (lastEvalLine >= 0 && lastEvalLine < lines.Length - 1)
                {
                    var after = string.Join("\n", lines, lastEvalLine + 1, lines.Length - lastEvalLine - 1).Trim();
                    if (!string.IsNullOrWhiteSpace(after))
                        return after;
                }
            }

            return text;
        }

        /// <summary>
        /// If an improvement prompt is configured, sends a second LLM call asking the model
        /// to self-critique and rewrite the draft. Returns the improved text, or the original
        /// draft if improvement is not configured or the call fails.
        /// </summary>
        private async Task<string> ApplyImprovementAsync(
            ContentBlock[] systemBlocks,
            string originalUserContent,
            string draft,
            double temperature)
        {
            var improvementPrompt = _options.GameDefinition?.ImprovementPrompt;
            if (string.IsNullOrWhiteSpace(improvementPrompt)) return draft;

            try
            {
                var improveRequest = new MessagesRequest
                {
                    Model = _options.Model,
                    MaxTokens = _options.MaxTokens,
                    Temperature = temperature,
                    System = systemBlocks,
                    Messages = new[]
                    {
                        new Message { Role = "user", Content = originalUserContent },
                        new Message { Role = "assistant", Content = draft },
                        new Message { Role = "user", Content = improvementPrompt }
                    }
                };
                AttachTool(improveRequest, ToolSchemas.Improvement);

                var improveResponse = await _client.SendMessagesAsync(improveRequest).ConfigureAwait(false);

                // Try structured tool_use first
                var toolInput = improveResponse.GetToolInput();
                if (toolInput != null)
                {
                    var improved = toolInput.Value<string>("improved");
                    if (!string.IsNullOrWhiteSpace(improved))
                        return improved;
                }

                // Fallback to text extraction
                var improvedText = improveResponse.GetText()?.Trim();
                if (string.IsNullOrWhiteSpace(improvedText)) return draft;

                // Strip evaluation block headers if the model included them in the output.
                // The improvement prompt asks for content only, but the model sometimes
                // outputs the self-evaluation before the rewritten content.
                improvedText = StripImprovementEvaluation(improvedText, draft);
                return string.IsNullOrWhiteSpace(improvedText) ? draft : improvedText;
            }
            catch
            {
                return draft; // fallback to original on any error
            }
        }

        /// <summary>
        /// Attaches a single tool definition with forced tool_choice to a request.
        /// </summary>
        private static void AttachTool(MessagesRequest request, ToolDefinition tool)
        {
            request.Tools = new[] { tool };
            request.ToolChoice = ToolSchemas.ForceAny();
        }

        /// <summary>
        /// Parses dialogue options from a tool_use JSON input.
        /// Returns null if the input is malformed (caller should fall back to text parsing).
        /// </summary>
        private static DialogueOption[] ParseDialogueOptionsFromTool(JObject toolInput)
        {
            try
            {
                var optionsArray = toolInput["options"] as JArray;
                if (optionsArray == null || optionsArray.Count == 0)
                    return null;

                var parsed = new List<DialogueOption>();
                foreach (var item in optionsArray)
                {
                    if (parsed.Count >= 3) break;

                    var statStr = NormalizeStatName(item.Value<string>("stat") ?? "");
                    StatType stat;
                    try
                    {
                        stat = (StatType)Enum.Parse(typeof(StatType), statStr, true);
                    }
                    catch (ArgumentException)
                    {
                        continue; // Invalid stat — skip
                    }

                    var text = item.Value<string>("text");
                    if (string.IsNullOrWhiteSpace(text)) continue;

                    // Parse callback
                    int? callbackTurn = null;
                    var callbackVal = item.Value<string>("callback");
                    if (!string.IsNullOrEmpty(callbackVal) &&
                        !string.Equals(callbackVal, "none", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(callbackVal, "null", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(callbackVal, out int turnNum))
                        {
                            callbackTurn = turnNum;
                        }
                        else if (callbackVal.StartsWith("turn_", StringComparison.OrdinalIgnoreCase) &&
                                 int.TryParse(callbackVal.Substring(5), out int turnNum2))
                        {
                            callbackTurn = turnNum2;
                        }
                    }

                    // Parse combo
                    string comboName = null;
                    var comboVal = item.Value<string>("combo");
                    if (!string.IsNullOrEmpty(comboVal) &&
                        !string.Equals(comboVal, "none", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(comboVal, "null", StringComparison.OrdinalIgnoreCase))
                    {
                        comboName = comboVal;
                    }

                    var tellBonus = item.Value<bool?>("tell_bonus") ?? false;
                    var weaknessWindow = item.Value<bool?>("weakness_window") ?? false;

                    parsed.Add(new DialogueOption(
                        stat, text, callbackTurn, comboName, tellBonus, weaknessWindow));
                }

                if (parsed.Count == 0) return null;
                return PadToThree(parsed);
            }
            catch
            {
                return null; // Malformed — fall back to text parsing
            }
        }

        /// <summary>
        /// Parses opponent response from a tool_use JSON input.
        /// Returns null if the input is malformed (caller should fall back to text parsing).
        /// </summary>
        private static OpponentResponse ParseOpponentResponseFromTool(JObject toolInput)
        {
            try
            {
                var message = toolInput.Value<string>("message");
                if (string.IsNullOrWhiteSpace(message))
                    return null;

                Tell tell = null;
                var tellObj = toolInput["tell"] as JObject;
                if (tellObj != null)
                {
                    var statStr = NormalizeStatName(tellObj.Value<string>("stat") ?? "");
                    try
                    {
                        var stat = (StatType)Enum.Parse(typeof(StatType), statStr, true);
                        var desc = tellObj.Value<string>("description") ?? "";
                        tell = new Tell(stat, desc);
                    }
                    catch (ArgumentException)
                    {
                        // Invalid stat — tell stays null
                    }
                }

                WeaknessWindow weakness = null;
                var weakObj = toolInput["weakness"] as JObject;
                if (weakObj != null)
                {
                    var statStr = NormalizeStatName(weakObj.Value<string>("defending_stat") ?? "");
                    try
                    {
                        var stat = (StatType)Enum.Parse(typeof(StatType), statStr, true);
                        var reduction = weakObj.Value<int?>("dc_reduction") ?? 0;
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

                return new OpponentResponse(message, tell, weakness);
            }
            catch
            {
                return null; // Malformed — fall back to text parsing
            }
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
        private static DialogueOption[] PadToThree(List<DialogueOption> parsed)
        {
            if (parsed.Count >= 3)
            {
                return parsed.GetRange(0, 3).ToArray();
            }

            var usedStats = new HashSet<StatType>();
            foreach (var opt in parsed)
            {
                usedStats.Add(opt.Stat);
            }

            var result = new List<DialogueOption>(parsed);
            foreach (var defaultStat in DefaultPaddingStats)
            {
                if (result.Count >= 3) break;
                if (usedStats.Contains(defaultStat)) continue;
                result.Add(new DialogueOption(defaultStat, "...",
                    callbackTurnNumber: null, comboName: null,
                    hasTellBonus: false, hasWeaknessWindow: false));
            }

            // If we still need more (e.g., all 3 default stats were used), just pad with Charm
            while (result.Count < 3)
            {
                result.Add(new DialogueOption(StatType.Charm, "...",
                    callbackTurnNumber: null, comboName: null,
                    hasTellBonus: false, hasWeaknessWindow: false));
            }

            return result.ToArray();
        }


    }
}
