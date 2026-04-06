using System;
using System.Reflection;
using Xunit;
using Pinder.LlmAdapters.Anthropic;
using Pinder.Core.Interfaces;

namespace Pinder.LlmAdapters.Tests.Anthropic
{
    public class Issue536_AnthropicAdapterTests
    {
        // What: 2. Remove Adapter Implementation & 3. Remove Adapter Methods
        // Mutation: Fails if AnthropicLlmAdapter still implements IStatefulLlmAdapter (if it existed) or has StartConversation.
        [Fact]
        public void AnthropicLlmAdapter_ShouldNotHaveStatefulMethods()
        {
            var adapterType = typeof(AnthropicLlmAdapter);

            // Should only implement ILlmAdapter and IDisposable
            var interfaces = adapterType.GetInterfaces();
            Assert.Contains(typeof(ILlmAdapter), interfaces);
            Assert.Contains(typeof(IDisposable), interfaces);
            
            // StartConversation should not exist
            var startMethod = adapterType.GetMethod("StartConversation", BindingFlags.Public | BindingFlags.Instance);
            Assert.Null(startMethod);

            // HasActiveConversation should not exist
            var hasActiveProperty = adapterType.GetProperty("HasActiveConversation", BindingFlags.Public | BindingFlags.Instance);
            Assert.Null(hasActiveProperty);
        }

        // What: 4. Remove Internal State
        // Mutation: Fails if the internal _session field of type ConversationSession still exists.
        [Fact]
        public void AnthropicLlmAdapter_ShouldNotHaveSessionField()
        {
            var adapterType = typeof(AnthropicLlmAdapter);
            
            var sessionField = adapterType.GetField("_session", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.Null(sessionField);
            
            // Also ensure ConversationSession class doesn't exist
            var conversationSessionType = Type.GetType("Pinder.LlmAdapters.ConversationSession, Pinder.LlmAdapters");
            Assert.Null(conversationSessionType);
        }
    }
}
