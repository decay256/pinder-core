using System;
using Pinder.LlmAdapters;
using Pinder.LlmAdapters.Anthropic.Dto;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public class ConversationSessionTests
    {
        // ============================================================
        // Construction
        // ============================================================

        [Fact]
        public void Constructor_sets_system_blocks_with_cache_control()
        {
            var session = new ConversationSession("You are Velvet");

            Assert.Single(session.SystemBlocks);
            Assert.Equal("text", session.SystemBlocks[0].Type);
            Assert.Equal("You are Velvet", session.SystemBlocks[0].Text);
            Assert.NotNull(session.SystemBlocks[0].CacheControl);
            Assert.Equal("ephemeral", session.SystemBlocks[0].CacheControl!.Type);
        }

        [Fact]
        public void Constructor_starts_with_empty_messages()
        {
            var session = new ConversationSession("prompt");
            Assert.Empty(session.Messages);
        }

        [Fact]
        public void Constructor_throws_on_null()
        {
            Assert.Throws<ArgumentException>(() => new ConversationSession(null!));
        }

        [Fact]
        public void Constructor_throws_on_empty()
        {
            Assert.Throws<ArgumentException>(() => new ConversationSession(""));
        }

        [Fact]
        public void Constructor_throws_on_whitespace()
        {
            Assert.Throws<ArgumentException>(() => new ConversationSession("   "));
        }

        // ============================================================
        // AppendUser
        // ============================================================

        [Fact]
        public void AppendUser_adds_user_message()
        {
            var session = new ConversationSession("prompt");
            session.AppendUser("hello");

            Assert.Single(session.Messages);
            Assert.Equal("user", session.Messages[0].Role);
            Assert.Equal("hello", session.Messages[0].Content);
        }

        [Fact]
        public void AppendUser_throws_on_null()
        {
            var session = new ConversationSession("prompt");
            Assert.Throws<ArgumentNullException>(() => session.AppendUser(null!));
        }

        [Fact]
        public void AppendUser_allows_empty_string()
        {
            var session = new ConversationSession("prompt");
            session.AppendUser("");
            Assert.Single(session.Messages);
            Assert.Equal("", session.Messages[0].Content);
        }

        // ============================================================
        // AppendAssistant
        // ============================================================

        [Fact]
        public void AppendAssistant_adds_assistant_message()
        {
            var session = new ConversationSession("prompt");
            session.AppendAssistant("response");

            Assert.Single(session.Messages);
            Assert.Equal("assistant", session.Messages[0].Role);
            Assert.Equal("response", session.Messages[0].Content);
        }

        [Fact]
        public void AppendAssistant_throws_on_null()
        {
            var session = new ConversationSession("prompt");
            Assert.Throws<ArgumentNullException>(() => session.AppendAssistant(null!));
        }

        // ============================================================
        // Message accumulation ordering
        // ============================================================

        [Fact]
        public void Messages_accumulate_in_order()
        {
            var session = new ConversationSession("prompt");
            session.AppendUser("u1");
            session.AppendAssistant("a1");
            session.AppendUser("u2");
            session.AppendAssistant("a2");

            Assert.Equal(4, session.Messages.Count);
            Assert.Equal("user", session.Messages[0].Role);
            Assert.Equal("u1", session.Messages[0].Content);
            Assert.Equal("assistant", session.Messages[1].Role);
            Assert.Equal("a1", session.Messages[1].Content);
            Assert.Equal("user", session.Messages[2].Role);
            Assert.Equal("u2", session.Messages[2].Content);
            Assert.Equal("assistant", session.Messages[3].Role);
            Assert.Equal("a2", session.Messages[3].Content);
        }

        // ============================================================
        // BuildRequest
        // ============================================================

        [Fact]
        public void BuildRequest_returns_correct_structure()
        {
            var session = new ConversationSession("system text");
            session.AppendUser("u1");
            session.AppendAssistant("a1");

            var request = session.BuildRequest("claude-sonnet-4-20250514", 2048, 0.7);

            Assert.Equal("claude-sonnet-4-20250514", request.Model);
            Assert.Equal(2048, request.MaxTokens);
            Assert.Equal(0.7, request.Temperature);
            Assert.Same(session.SystemBlocks, request.System);
            Assert.Equal(2, request.Messages.Length);
            Assert.Equal("user", request.Messages[0].Role);
            Assert.Equal("u1", request.Messages[0].Content);
            Assert.Equal("assistant", request.Messages[1].Role);
            Assert.Equal("a1", request.Messages[1].Content);
        }

        [Fact]
        public void BuildRequest_returns_snapshot_not_live_reference()
        {
            var session = new ConversationSession("system");
            session.AppendUser("u1");

            var request = session.BuildRequest("model", 1024, 0.9);
            Assert.Single(request.Messages);

            // Append more after building
            session.AppendAssistant("a1");
            session.AppendUser("u2");

            // Original request must not have changed
            Assert.Single(request.Messages);
        }

        [Fact]
        public void BuildRequest_with_no_messages()
        {
            var session = new ConversationSession("system");
            var request = session.BuildRequest("model", 1024, 0.9);
            Assert.Empty(request.Messages);
        }
    }
}
