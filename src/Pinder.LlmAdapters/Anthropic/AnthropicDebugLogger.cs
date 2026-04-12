using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pinder.LlmAdapters.Anthropic.Dto;

namespace Pinder.LlmAdapters.Anthropic
{
    /// <summary>
    /// Handles all debug logging of LLM requests and responses, and collects/summarizes
    /// token usage statistics for a session.
    /// </summary>
    internal sealed class AnthropicDebugLogger
    {
        private static readonly object DebugLock = new object();
        private bool _debugHeaderWritten;
        private readonly List<CallSummaryStat> _callStats = new List<CallSummaryStat>();

        /// <summary>Returns a read-only view of all per-call token stats collected during the session.</summary>
        public IReadOnlyList<CallSummaryStat> GetCallStats() => _callStats.AsReadOnly();

        /// <summary>Appends a markdown section for one LLM call to the debug transcript file.</summary>
        public void LogDebug(string callType, int turn, MessagesRequest request, MessagesResponse response, string? debugDirectory)
        {
            try
            {
                // Track token stats always (not gated on DebugDirectory)
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

                if (!string.IsNullOrEmpty(debugDirectory))
                {
                    lock (DebugLock)
                    {
                        // Ensure parent directory exists
                        string dir = Path.GetDirectoryName(debugDirectory);
                        if (!string.IsNullOrEmpty(dir))
                            Directory.CreateDirectory(dir);

                        // Write header on first append
                        if (!_debugHeaderWritten)
                        {
                            File.WriteAllText(debugDirectory, $"# Session Debug Transcript\n\n---\n\n");
                            _debugHeaderWritten = true;
                        }

                        File.AppendAllText(debugDirectory, sb.ToString());
                    }
                }
            }
            catch
            {
                // Ignore logging errors
            }
        }

        /// <summary>Writes the token summary table to the end of the debug transcript.</summary>
        public void WriteDebugSummary(string? debugDirectory)
        {
            if (string.IsNullOrEmpty(debugDirectory)) return;
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

                lock (DebugLock)
                {
                    File.AppendAllText(debugDirectory, sb.ToString());
                }
            }
            catch
            {
                // Ignore logging errors
            }
        }
    }
}
