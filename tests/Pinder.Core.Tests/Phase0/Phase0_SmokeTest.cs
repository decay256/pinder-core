using System;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.Core.Tests.Phase0
{
    [Trait("Category", "Phase0")]
    public class Phase0_SmokeTest
    {
        // Smoke test: prove the wiring through PinderLlmAdapter + RecordingLlmTransport
        // actually completes a full ResolveTurnAsync without throwing. If this fails,
        // the rest of the Phase 0 suite is fiction.
        [Fact]
        public async Task EndToEndTurn_WithRecordingTransport_Completes()
        {
            var transport = new RecordingLlmTransport
            {
                DefaultResponse = "Maybe."
            };
            transport.QueueDialogueOptions(Phase0Fixtures.CannedDialogueOptions);
            transport.QueueDelivery(Phase0Fixtures.CannedDelivery);
            transport.QueueOpponent(Phase0Fixtures.CannedOpponent);

            var adapter = Phase0Fixtures.MakeAdapter(transport);
            var dice = new PlaybackDiceRoller(
                5,        // ctor: horniness d10
                15, 50    // turn 1: d20 main, d100 timing
            );

            var session = new GameSession(
                Phase0Fixtures.MakeProfile("Player"),
                Phase0Fixtures.MakeProfile("Opponent"),
                adapter, dice, new NullTrapRegistry(), Phase0Fixtures.MakeConfig());

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.NotNull(result);
            Assert.True(transport.Exchanges.Count >= 3,
                $"Expected at least 3 LLM exchanges (options/delivery/opponent), got {transport.Exchanges.Count}.");
        }
    }
}
