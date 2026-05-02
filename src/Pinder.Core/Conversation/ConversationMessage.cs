using System;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Single entry in a stateful opponent conversation history (#788).
    /// Plain value type carried as <c>IReadOnlyList&lt;ConversationMessage&gt;</c>
    /// across the stateless <see cref="Pinder.Core.Interfaces.IStatefulLlmAdapter"/>
    /// boundary so the engine, not the adapter, owns the conversation state.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Role"/> is one of <c>"user"</c> or <c>"assistant"</c> (lower-case,
    /// matching the OpenAI / Anthropic wire conventions). The constructor normalises
    /// arbitrary casing.
    /// </para>
    /// </remarks>
    public sealed class ConversationMessage
    {
        public const string UserRole = "user";
        public const string AssistantRole = "assistant";

        /// <summary>Role string. Always <c>"user"</c> or <c>"assistant"</c>.</summary>
        public string Role { get; }

        /// <summary>Raw text content of the message. Never null (empty string allowed).</summary>
        public string Content { get; }

        public ConversationMessage(string role, string content)
        {
            if (role == null) throw new ArgumentNullException(nameof(role));
            string normalized = role.Trim().ToLowerInvariant();
            if (normalized != UserRole && normalized != AssistantRole)
                throw new ArgumentException(
                    $"Role must be '{UserRole}' or '{AssistantRole}'; got '{role}'.",
                    nameof(role));
            Role = normalized;
            Content = content ?? string.Empty;
        }

        /// <summary>Creates a user-role message.</summary>
        public static ConversationMessage User(string content) =>
            new ConversationMessage(UserRole, content);

        /// <summary>Creates an assistant-role message.</summary>
        public static ConversationMessage Assistant(string content) =>
            new ConversationMessage(AssistantRole, content);
    }
}
