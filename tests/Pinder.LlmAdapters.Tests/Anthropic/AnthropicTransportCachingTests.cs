using System;
using System.Linq;
using Pinder.LlmAdapters.Anthropic;
using Pinder.LlmAdapters.Anthropic.Dto;
using Xunit;

namespace Pinder.LlmAdapters.Tests.Anthropic
{
    public class AnthropicTransportCachingTests
    {
        // ── #1123: avatar stateful path caching ────────────────────────────
        //
        // The avatar (delivery) session is now stateful + cached, structurally
        // identical to the datee session. Per CACHE-PREFIX-STABILITY, the GM
        // system prompt + avatar character spec must form a STABLE cacheable
        // prefix (cache_control: ephemeral) while the running transcript is the
        // volatile suffix carried on the user/assistant messages. These tests
        // exercise the exact request-building machinery the Anthropic avatar
        // stateful overload uses (CacheBlockBuilder.BuildPlayerAvatarOnlySystemBlocks
        // + ConversationSession replay), proving the cached prefix is reused
        // byte-for-byte on repeated turns.

        [Fact]
        public void AvatarSystemBlocks_AreCacheable_StaticPrefix()
        {
            const string avatarSpec = "You are Avery. §3.1 full avatar spec.";
            var blocks = CacheBlockBuilder.BuildPlayerAvatarOnlySystemBlocks(avatarSpec);

            Assert.Single(blocks);
            Assert.Equal("text", blocks[0].Type);
            Assert.Equal(avatarSpec, blocks[0].Text);
            Assert.NotNull(blocks[0].CacheControl);
            Assert.Equal("ephemeral", blocks[0].CacheControl!.Type);
        }

        [Fact]
        public void AvatarStatefulPath_ReusesCachedPrefix_AcrossRepeatedTurns()
        {
            const string avatarSpec = "You are Avery. §3.1 full avatar spec (cacheable).";

            // Turn 1 (first delivery): no prior history -> system blocks only.
            var systemBlocksTurn1 = CacheBlockBuilder.BuildPlayerAvatarOnlySystemBlocks(avatarSpec);
            var requestTurn1 = AnthropicRequestBuilders.BuildMessagesRequest(
                model: "claude", maxTokens: 1024, systemBlocks: systemBlocksTurn1,
                userContent: "deliver turn 1", temperature: 0.7);

            // Turn 2 (repeated delivery): prior avatar history replayed via
            // ConversationSession, SAME cached system prefix re-supplied.
            var systemBlocksTurn2 = CacheBlockBuilder.BuildPlayerAvatarOnlySystemBlocks(avatarSpec);
            var session = new ConversationSession();
            session.AppendUser("deliver turn 1");
            session.AppendAssistant("delivered 1");
            session.AppendUser("deliver turn 2");
            var requestTurn2 = session.BuildRequest("claude", 1024, 0.7, systemBlocksTurn2);

            // The cacheable system prefix is identical (same text, same
            // ephemeral cache_control) on both turns — the prefix is stable, so
            // turn 2 reads it from cache rather than re-sending fresh tokens.
            Assert.Single(requestTurn1.System);
            Assert.Single(requestTurn2.System);
            Assert.Equal(requestTurn1.System[0].Text, requestTurn2.System[0].Text);
            Assert.Equal(avatarSpec, requestTurn2.System[0].Text);
            Assert.NotNull(requestTurn1.System[0].CacheControl);
            Assert.NotNull(requestTurn2.System[0].CacheControl);
            Assert.Equal("ephemeral", requestTurn1.System[0].CacheControl!.Type);
            Assert.Equal("ephemeral", requestTurn2.System[0].CacheControl!.Type);

            // The transcript (volatile suffix) GROWS turn-over-turn: turn 1 has
            // one user message; turn 2 replays the full prior exchange + the new
            // turn's user message.
            Assert.Single(requestTurn1.Messages);
            Assert.Equal(3, requestTurn2.Messages.Length);
            Assert.Equal("user", requestTurn2.Messages[0].Role);
            Assert.Equal("assistant", requestTurn2.Messages[1].Role);
            Assert.Equal("user", requestTurn2.Messages[2].Role);
        }

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
