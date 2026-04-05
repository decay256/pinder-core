using Pinder.Core.Interfaces;
using Pinder.LlmAdapters.Anthropic;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// Tests for Issue #542: AnthropicLlmAdapter implements IStatefulLlmAdapter.
    /// </summary>
    public class Issue542_IStatefulLlmAdapterTests
    {
        [Fact]
        public void AnthropicLlmAdapter_Implements_IStatefulLlmAdapter()
        {
            Assert.True(typeof(IStatefulLlmAdapter).IsAssignableFrom(typeof(AnthropicLlmAdapter)));
        }

        [Fact]
        public void AnthropicLlmAdapter_StillImplements_ILlmAdapter()
        {
            // IStatefulLlmAdapter extends ILlmAdapter, so this should also be true
            Assert.True(typeof(ILlmAdapter).IsAssignableFrom(typeof(AnthropicLlmAdapter)));
        }

        [Fact]
        public void IStatefulLlmAdapter_HasStartConversation()
        {
            var method = typeof(IStatefulLlmAdapter).GetMethod("StartConversation");
            Assert.NotNull(method);
        }

        [Fact]
        public void IStatefulLlmAdapter_HasHasActiveConversation()
        {
            var prop = typeof(IStatefulLlmAdapter).GetProperty("HasActiveConversation");
            Assert.NotNull(prop);
        }

        [Fact]
        public void IStatefulLlmAdapter_Extends_ILlmAdapter()
        {
            Assert.True(typeof(ILlmAdapter).IsAssignableFrom(typeof(IStatefulLlmAdapter)));
        }

        [Fact]
        public void AnthropicLlmAdapter_CastToIStatefulLlmAdapter_Works()
        {
            var options = new AnthropicOptions { ApiKey = "test-key", Model = "test-model" };
            using var adapter = new AnthropicLlmAdapter(options);

            ILlmAdapter llm = adapter;
            Assert.True(llm is IStatefulLlmAdapter);

            var stateful = (IStatefulLlmAdapter)llm;
            Assert.False(stateful.HasActiveConversation);

            stateful.StartConversation("Test system prompt");
            Assert.True(stateful.HasActiveConversation);
        }
    }
}
