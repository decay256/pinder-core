using System.Threading;
using System.Threading.Tasks;
using Pinder.LlmAdapters.Anthropic.Dto;

namespace Pinder.LlmAdapters.Anthropic
{
    /// <summary>
    /// Manages the self-critique and improvement pass for LLM-generated text,
    /// including stripping evaluation headers.
    /// </summary>
    internal static class AnthropicResponseImprover
    {
        /// <summary>
        /// If an improvement prompt is configured, sends a second LLM call asking the model
        /// to self-critique and rewrite the draft. Returns the improved text, or the original
        /// draft if improvement is not configured or the call fails.
        /// </summary>
        public static async Task<string> ApplyImprovementAsync(
            AnthropicClient client,
            AnthropicOptions options,
            ContentBlock[] systemBlocks,
            string originalUserContent,
            string draft,
            double temperature,
            CancellationToken ct = default)
        {
            var improvementPrompt = options.GameDefinition?.ImprovementPrompt;
            if (string.IsNullOrWhiteSpace(improvementPrompt)) return draft;

            try
            {
                var improveRequest = new MessagesRequest
                {
                    Model = options.Model,
                    MaxTokens = options.MaxTokens,
                    Temperature = temperature,
                    System = systemBlocks,
                    Messages = new[]
                    {
                        new Message { Role = "user", Content = originalUserContent },
                        new Message { Role = "assistant", Content = draft },
                        new Message { Role = "user", Content = improvementPrompt }
                    }
                };
                AnthropicRequestBuilders.AttachTool(improveRequest, ToolSchemas.Improvement);

                var improveResponse = await client.SendMessagesAsync(improveRequest, ct).ConfigureAwait(false);

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
                improvedText = StripImprovementEvaluation(improvedText, draft);
                return string.IsNullOrWhiteSpace(improvedText) ? draft : improvedText;
            }
            catch (System.OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // #794: cancellation must propagate.
            }
            catch
            {
                return draft; // fallback to original on any error
            }
        }

        /// <summary>
        /// Strips evaluation block headers from improvement output.
        /// The model sometimes outputs numbered evaluation then the content.
        /// Heuristic: if text starts with "1." or contains evaluation end markers,
        /// find where the actual content starts.
        /// </summary>
        public static string StripImprovementEvaluation(string text, string originalDraft)
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
                var idx = text.IndexOf(marker, System.StringComparison.OrdinalIgnoreCase);
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
            if (text.Length > 2 && text[0] == '1' && text[1] == '.')
            {
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
    }
}
