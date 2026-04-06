using Xunit;
using Pinder.LlmAdapters.Anthropic;
using Pinder.LlmAdapters.Anthropic.Dto;

namespace Pinder.LlmAdapters.Tests.Anthropic
{
    public class ConversationSessionTests
    {
        [Fact]
        public void NewSession_HasZeroMessages()
        {
            var session = new ConversationSession();
            Assert.Equal(0, session.MessageCount);
        }

        [Fact]
        public void AppendUser_IncreasesMessageCount()
        {
            var session = new ConversationSession();
            session.AppendUser("Hello");
            Assert.Equal(1, session.MessageCount);
        }

        [Fact]
        public void AppendAssistant_IncreasesMessageCount()
        {
            var session = new ConversationSession();
            session.AppendAssistant("Hi there");
            Assert.Equal(1, session.MessageCount);
        }

        [Fact]
        public void AccumulatesMessagesAcrossMultipleTurns()
        {
            var session = new ConversationSession();
            session.AppendUser("Turn 1 user");
            session.AppendAssistant("Turn 1 assistant");
            session.AppendUser("Turn 2 user");
            session.AppendAssistant("Turn 2 assistant");

            Assert.Equal(4, session.MessageCount);
        }

        [Fact]
        public void BuildRequest_ContainsAllAccumulatedMessages()
        {
            var session = new ConversationSession();
            session.AppendUser("msg1");
            session.AppendAssistant("reply1");
            session.AppendUser("msg2");

            var systemBlocks = new[] { new ContentBlock { Type = "text", Text = "System prompt" } };
            var request = session.BuildRequest("claude-3", 1024, 0.8, systemBlocks);

            Assert.Equal("claude-3", request.Model);
            Assert.Equal(1024, request.MaxTokens);
            Assert.Equal(0.8, request.Temperature);
            Assert.Single(request.System);
            Assert.Equal(3, request.Messages.Length);
            Assert.Equal("user", request.Messages[0].Role);
            Assert.Equal("msg1", request.Messages[0].Content);
            Assert.Equal("assistant", request.Messages[1].Role);
            Assert.Equal("reply1", request.Messages[1].Content);
            Assert.Equal("user", request.Messages[2].Role);
            Assert.Equal("msg2", request.Messages[2].Content);
        }

        [Fact]
        public void BuildRequest_ReturnsFreshArrayNotInternalReference()
        {
            var session = new ConversationSession();
            session.AppendUser("first");

            var systemBlocks = new ContentBlock[0];
            var request1 = session.BuildRequest("model", 100, 0.5, systemBlocks);

            session.AppendAssistant("second");
            var request2 = session.BuildRequest("model", 100, 0.5, systemBlocks);

            // First request should still have 1 message (not mutated by subsequent append)
            Assert.Single(request1.Messages);
            Assert.Equal(2, request2.Messages.Length);
        }
    }
}
