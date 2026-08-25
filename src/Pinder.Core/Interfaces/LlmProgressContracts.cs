using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Conversation;

namespace Pinder.Core.Interfaces
{
    public enum LlmProgressKind
    {
        ResponseStarted,
        Reasoning,
        Text,
        ToolCall,
        Completion
    }

    /// <summary>
    /// Semantic progress marker for buffered LLM calls. Carries classification
    /// and time only; provider payloads stay inside the transport.
    /// </summary>
    public sealed class LlmProgressEvent
    {
        public LlmProgressEvent(LlmProgressKind kind, DateTimeOffset timestamp)
        {
            Kind = kind;
            Timestamp = timestamp;
        }

        public LlmProgressKind Kind { get; }

        public DateTimeOffset Timestamp { get; }
    }

    public interface IProgressAwareLlmTransport
    {
        Task<string> SendWithProgressAsync(
            string systemPrompt,
            string userMessage,
            IProgress<LlmProgressEvent>? progress = null,
            double temperature = 0.9,
            int? maxTokens = null,
            string? phase = null,
            CancellationToken ct = default);
    }

    public interface IProgressAwareConversationLlmTransport : IProgressAwareLlmTransport
    {
        Task<string> SendConversationWithProgressAsync(
            string systemPrompt,
            IReadOnlyList<ConversationMessage> priorMessages,
            string userMessage,
            IProgress<LlmProgressEvent>? progress = null,
            double temperature = 0.9,
            int? maxTokens = null,
            string? phase = null,
            CancellationToken cancellationToken = default);
    }

    public interface IProgressAwareStructuredLlmTransport
    {
        Task<StructuredLlmResponse> SendStructuredWithProgressAsync(
            StructuredLlmRequest request,
            IProgress<LlmProgressEvent>? progress = null,
            CancellationToken ct = default);
    }

    public interface IProgressAwareStructuredConversationLlmTransport : IProgressAwareStructuredLlmTransport
    {
        Task<StructuredLlmResponse> SendStructuredConversationWithProgressAsync(
            StructuredLlmRequest request,
            IReadOnlyList<ConversationMessage> priorMessages,
            IProgress<LlmProgressEvent>? progress = null,
            CancellationToken cancellationToken = default);
    }
}
