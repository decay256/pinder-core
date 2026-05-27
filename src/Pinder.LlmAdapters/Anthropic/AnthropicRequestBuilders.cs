using System;
using System.Collections.Generic;
using System.Text;
using Pinder.LlmAdapters.Anthropic.Dto;

namespace Pinder.LlmAdapters.Anthropic
{
    /// <summary>
    /// Encapsulates the logic for constructing Anthropic API MessagesRequest objects
    /// and attaching tool definitions.
    /// </summary>
    internal static class AnthropicRequestBuilders
    {
        /// <summary>
        /// Builds a single-user-message MessagesRequest with the given system blocks,
        /// user content, model, max tokens, and temperature.
        /// </summary>
        public static MessagesRequest BuildMessagesRequest(
            string model,
            int maxTokens,
            ContentBlock[] systemBlocks,
            string userContent,
            double temperature)
        {
            return new MessagesRequest
            {
                Model = model,
                MaxTokens = maxTokens,
                Temperature = temperature,
                System = systemBlocks,
                Messages = BuildMessages(userContent)
            };
        }

        /// <summary>
        /// Parses a flattened user message context string and builds the array of Message objects
        /// with cache_control: ephemeral annotations on the system prompt and historical context inputs.
        /// </summary>
        public static Message[] BuildMessages(string userMessage)
        {
            if (userMessage == null) throw new ArgumentNullException(nameof(userMessage));

            if (!userMessage.StartsWith("[PREVIOUS CONVERSATION CONTEXT]"))
            {
                return new[]
                {
                    new Message { Role = "user", Content = userMessage }
                };
            }

            var lines = userMessage.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var messages = new List<Message>();
            Message? currentMsg = null;
            bool parsingCurrentTurn = false;
            var currentTurnContent = new StringBuilder();

            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i];
                if (parsingCurrentTurn)
                {
                    currentTurnContent.AppendLine(line);
                }
                else if (line.Equals("[CURRENT TURN]", StringComparison.OrdinalIgnoreCase))
                {
                    parsingCurrentTurn = true;
                }
                else if (line.StartsWith("[PLAYER] "))
                {
                    if (currentMsg != null)
                    {
                        currentMsg.Content = ((string)currentMsg.Content).TrimEnd('\r', '\n');
                        messages.Add(currentMsg);
                    }
                    currentMsg = new Message { Role = "user", Content = line.Substring("[PLAYER] ".Length) };
                }
                else if (line.StartsWith("[OPPONENT] "))
                {
                    if (currentMsg != null)
                    {
                        currentMsg.Content = ((string)currentMsg.Content).TrimEnd('\r', '\n');
                        messages.Add(currentMsg);
                    }
                    currentMsg = new Message { Role = "assistant", Content = line.Substring("[OPPONENT] ".Length) };
                }
                else
                {
                    if (currentMsg != null)
                    {
                        currentMsg.Content = (string)currentMsg.Content + "\n" + line;
                    }
                }
            }

            if (currentMsg != null)
            {
                currentMsg.Content = ((string)currentMsg.Content).TrimEnd('\r', '\n');
                messages.Add(currentMsg);
            }

            var currentTurnText = currentTurnContent.ToString().TrimEnd('\r', '\n');
            messages.Add(new Message { Role = "user", Content = currentTurnText });

            // Apply prompt-caching cache_control on historical context inputs
            var userIndices = new List<int>();
            for (int i = 0; i < messages.Count; i++)
            {
                if (messages[i].Role == "user")
                {
                    userIndices.Add(i);
                }
            }

            if (userIndices.Count >= 2)
            {
                // Cache the second-to-last user message (representing prior history checkpoint)
                int histUserIdx = userIndices[userIndices.Count - 2];
                var histMsg = messages[histUserIdx];
                messages[histUserIdx] = new Message
                {
                    Role = "user",
                    Content = new[]
                    {
                        new ContentBlock
                        {
                            Type = "text",
                            Text = (string)histMsg.Content,
                            CacheControl = new CacheControl { Type = "ephemeral" }
                        }
                    }
                };

                // Also cache the last user message (the current turn)
                int lastUserIdx = userIndices[userIndices.Count - 1];
                var lastMsg = messages[lastUserIdx];
                messages[lastUserIdx] = new Message
                {
                    Role = "user",
                    Content = new[]
                    {
                        new ContentBlock
                        {
                            Type = "text",
                            Text = (string)lastMsg.Content,
                            CacheControl = new CacheControl { Type = "ephemeral" }
                        }
                    }
                };
            }
            else if (userIndices.Count == 1)
            {
                // Only one user message, but we cache it anyway
                int lastUserIdx = userIndices[0];
                var lastMsg = messages[lastUserIdx];
                messages[lastUserIdx] = new Message
                {
                    Role = "user",
                    Content = new[]
                    {
                        new ContentBlock
                        {
                            Type = "text",
                            Text = (string)lastMsg.Content,
                            CacheControl = new CacheControl { Type = "ephemeral" }
                        }
                    }
                };
            }

            return messages.ToArray();
        }

        /// <summary>
        /// Attaches a single tool definition with forced tool_choice to a request.
        /// </summary>
        public static void AttachTool(MessagesRequest request, ToolDefinition tool)
        {
            request.Tools = new[] { tool };
            request.ToolChoice = ToolSchemas.ForceAny();
        }
    }
}
