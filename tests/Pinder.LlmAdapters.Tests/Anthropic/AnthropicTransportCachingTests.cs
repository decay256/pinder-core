using System;
using System.Linq;
using Pinder.LlmAdapters.Anthropic;
using Pinder.LlmAdapters.Anthropic.Dto;
using Xunit;

namespace Pinder.LlmAdapters.Tests.Anthropic
{
    public class AnthropicTransportCachingTests
    {
        [Fact]
        public void BuildMessages_SingleTurn_ReturnsPlainStringMessage()
        {
            var userMessage = "Hello from player";
            var messages = AnthropicRequestBuilders.BuildMessages(userMessage);

            Assert.Single(messages);
            Assert.Equal("user", messages[0].Role);
            Assert.Equal("Hello from player", messages[0].Content);
        }

        [Fact]
        public void BuildMessages_MultiTurn_ParsesHistoryAndInjectsCacheControl()
        {
            var userMessage = "[PREVIOUS CONVERSATION CONTEXT]\n" +
                              "[PLAYER] Hello there\n" +
                              "[DATEE] Hey back!\n" +
                              "[PLAYER] What's up?\n" +
                              "[DATEE] Not much\n" +
                              "\n" +
                              "[CURRENT TURN]\n" +
                              "Let's play a game!";

            var messages = AnthropicRequestBuilders.BuildMessages(userMessage);

            // Expecting:
            // Message 0 (PLAYER/user): "Hello there"
            // Message 1 (DATEE/assistant): "Hey back!"
            // Message 2 (PLAYER/user): "What's up?" (with cache_control)
            // Message 3 (DATEE/assistant): "Not much"
            // Message 4 (PLAYER/user): "Let's play a game!" (with cache_control)
            Assert.Equal(5, messages.Length);

            Assert.Equal("user", messages[0].Role);
            Assert.Equal("Hello there", messages[0].Content);

            Assert.Equal("assistant", messages[1].Role);
            Assert.Equal("Hey back!", messages[1].Content);

            Assert.Equal("user", messages[2].Role);
            var blocksMsg2 = Assert.IsType<ContentBlock[]>(messages[2].Content);
            Assert.Single(blocksMsg2);
            Assert.Equal("What's up?", blocksMsg2[0].Text);
            Assert.NotNull(blocksMsg2[0].CacheControl);
            Assert.Equal("ephemeral", blocksMsg2[0].CacheControl!.Type);

            Assert.Equal("assistant", messages[3].Role);
            Assert.Equal("Not much", messages[3].Content);

            Assert.Equal("user", messages[4].Role);
            var blocksMsg4 = Assert.IsType<ContentBlock[]>(messages[4].Content);
            Assert.Single(blocksMsg4);
            Assert.Equal("Let's play a game!", blocksMsg4[0].Text);
            Assert.NotNull(blocksMsg4[0].CacheControl);
            Assert.Equal("ephemeral", blocksMsg4[0].CacheControl!.Type);
        }
    }
}
