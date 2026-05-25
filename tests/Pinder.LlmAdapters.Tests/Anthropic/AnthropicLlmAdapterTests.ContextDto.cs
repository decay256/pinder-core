using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.LlmAdapters.Tests.Anthropic
{
    public partial class AnthropicLlmAdapterTests
    {
        [Fact]
        public void DialogueContext_defaults_backward_compatible()
        {
            // Old call site — no playerName/opponentName/currentTurn
            var ctx = new DialogueContext(
                "player", "opponent",
                new List<(string, string)>(), "last",
                new string[0], 10);

            Assert.Equal("", ctx.PlayerName);
            Assert.Equal("", ctx.OpponentName);
            Assert.Equal(0, ctx.CurrentTurn);
        }

        [Fact]
        public void DeliveryContext_defaults_backward_compatible()
        {
            var ctx = new DeliveryContext(
                "player", "opponent",
                new List<(string, string)>(), "last",
                new DialogueOption(StatType.Charm, "test"),
                FailureTier.None, 5,
                new string[0]);

            Assert.Equal("", ctx.PlayerName);
            Assert.Equal("", ctx.OpponentName);
            Assert.Equal(0, ctx.CurrentTurn);
        }

        [Fact]
        public void OpponentContext_defaults_backward_compatible()
        {
            var ctx = new OpponentContext(
                "player", "opponent",
                new List<(string, string)>(), "last",
                new string[0], 10, "delivered",
                10, 12, 2.0);

            Assert.Equal("", ctx.PlayerName);
            Assert.Equal("", ctx.OpponentName);
            Assert.Equal(0, ctx.CurrentTurn);
        }

        [Fact]
        public void DialogueContext_new_fields_settable()
        {
            var ctx = new DialogueContext(
                "player", "opponent",
                new List<(string, string)>(), "last",
                new string[0], 10,
                playerName: "Thundercock",
                opponentName: "Velvet",
                currentTurn: 3);

            Assert.Equal("Thundercock", ctx.PlayerName);
            Assert.Equal("Velvet", ctx.OpponentName);
            Assert.Equal(3, ctx.CurrentTurn);
        }
    }
}
