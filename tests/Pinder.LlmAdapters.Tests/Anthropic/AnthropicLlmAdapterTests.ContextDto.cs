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
            // Old call site — no playerName/dateeName/currentTurn
            var ctx = new DialogueContext(
                "player", "datee",
                new List<(string, string)>(), "last",
                new string[0], 10);

            Assert.Equal("", ctx.PlayerName);
            Assert.Equal("", ctx.DateeName);
            Assert.Equal(0, ctx.CurrentTurn);
        }

        // #1138: DeliveryContext_defaults_backward_compatible removed — it only
        // exercised the removed DeliveryContext ctor; delivery is now the
        // deterministic DeliveryOverlay (#1125). DialogueContext/DateeContext
        // backward-compat tests above/below still guard the surviving DTOs.

        [Fact]
        public void DateeContext_defaults_backward_compatible()
        {
            var ctx = new DateeContext(
                "datee",
                new List<(string, string)>(), "last",
                new string[0], 10, "delivered",
                10, 12, 2.0);

            Assert.Equal("", ctx.PlayerName);
            Assert.Equal("", ctx.DateeName);
            Assert.Equal(0, ctx.CurrentTurn);
        }

        [Fact]
        public void DialogueContext_new_fields_settable()
        {
            var ctx = new DialogueContext(
                "player", "datee",
                new List<(string, string)>(), "last",
                new string[0], 10,
                playerName: "Thundercock",
                dateeName: "Velvet",
                currentTurn: 3);

            Assert.Equal("Thundercock", ctx.PlayerName);
            Assert.Equal("Velvet", ctx.DateeName);
            Assert.Equal(3, ctx.CurrentTurn);
        }
    }
}
