using System;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Versioned serialized state for one provider-neutral LLM conversation.
    /// The engine treats the payload as immutable; Pi.Agent.Core owns its schema
    /// and reconstruction semantics.
    /// </summary>
    public sealed class LlmConversationSessionSnapshot
    {
        public const string PiAgentSessionV1 = "pi-agent-session.v1";

        public string Format { get; }
        public string Payload { get; }

        public LlmConversationSessionSnapshot(string format, string payload)
        {
            if (string.IsNullOrWhiteSpace(format))
                throw new ArgumentException("Session snapshot format is required.", nameof(format));
            if (string.IsNullOrWhiteSpace(payload))
                throw new ArgumentException("Session snapshot payload is required.", nameof(payload));
            Format = format;
            Payload = payload;
        }
    }
}
