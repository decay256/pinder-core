using System.Collections.Generic;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Tests for NullLlmAdapter and LLM context types.
    /// Prototype maturity: happy-path tests for all interface methods.
    /// </summary>
    [Trait("Category", "Core")]
    public class LlmAdapterTests
    {
        private readonly NullLlmAdapter _adapter = new NullLlmAdapter();

        private DialogueContext MakeDialogueContext()
        {
            return new DialogueContext(
                playerAvatarPrompt: "You are a charming penis.",
                dateePrompt: "You are a shy penis.",
                conversationHistory: new List<(string, string)>(),
                dateeLastMessage: "",
                activeTraps: new List<string>(),
                currentInterest: 10
            );
        }

        // --- GetDialogueOptionsAsync ---

        [Fact]
        public async Task GetDialogueOptionsAsync_Returns_Four_Options()
        {
            var ctx = MakeDialogueContext();
            var options = await _adapter.GetDialogueOptionsAsync(ctx);

            Assert.NotNull(options);
            Assert.Equal(4, options.Length);
        }

        [Fact]
        public async Task GetDialogueOptionsAsync_Options_Have_NonEmpty_Text()
        {
            var ctx = MakeDialogueContext();
            var options = await _adapter.GetDialogueOptionsAsync(ctx);

            foreach (var option in options)
            {
                Assert.NotNull(option.IntendedText);
                Assert.NotEmpty(option.IntendedText);
            }
        }

        [Fact]
        public async Task GetDialogueOptionsAsync_Options_Cover_Four_Stats()
        {
            var ctx = MakeDialogueContext();
            var options = await _adapter.GetDialogueOptionsAsync(ctx);

            var stats = new HashSet<StatType>();
            foreach (var option in options)
            {
                stats.Add(option.Stat);
            }
            // Should have 4 distinct stats (Charm, Honesty, Wit, Chaos)
            Assert.Equal(4, stats.Count);
            Assert.Contains(StatType.Charm, stats);
            Assert.Contains(StatType.Honesty, stats);
            Assert.Contains(StatType.Wit, stats);
            Assert.Contains(StatType.Chaos, stats);
        }

        // --- DeliverMessageAsync (REMOVED in #1125) ---
        // The delivery LLM surface (NullLlmAdapter.DeliverMessageAsync) was
        // collapsed into the deterministic, non-LLM DeliveryOverlay commit step.
        // The old "echo intended text on success / [tier]-prefix on failure"
        // behaviour these tests pinned no longer exists; DeliveryOverlay's own
        // determinism + parity is covered by Issue1125_CollapseDeliveryTests and
        // DeliveryOverlayTests. Coverage retained here for the post-#1125
        // contract: a successful commit is verbatim, a failed commit is mutated.

        [Fact]
        public void DeliveryOverlay_Success_CommitsVerbatim()
        {
            string committed = DeliveryOverlay.Apply("Hello there!", FailureTier.Success, missMargin: 0);
            Assert.Equal("Hello there!", committed);
        }

        [Theory]
        [InlineData(FailureTier.Fumble)]
        [InlineData(FailureTier.Misfire)]
        [InlineData(FailureTier.TropeTrap)]
        [InlineData(FailureTier.Catastrophe)]
        [InlineData(FailureTier.Legendary)]
        public void DeliveryOverlay_Failure_MutatesCommittedLine(FailureTier tier)
        {
            string committed = DeliveryOverlay.Apply("Clever line goes here.", tier, missMargin: 2);
            Assert.NotEqual("Clever line goes here.", committed);
        }

        // --- GetDateeResponseAsync ---

        [Fact]
        public async Task GetDateeResponseAsync_Returns_NonNull()
        {
            var ctx = new DateeContext(
                dateePrompt: "o",
                conversationHistory: new List<(string, string)>(),
                dateeLastMessage: "",
                activeTraps: new List<string>(),
                currentInterest: 10,
                playerDeliveredMessage: "Hello!",
                interestBefore: 10,
                interestAfter: 11,
                responseDelayMinutes: 1.5
            );

            var result = await _adapter.GetDateeResponseAsync(ctx);

            Assert.NotNull(result);
            Assert.Equal("...", result.MessageText);
            Assert.Null(result.DetectedTell);
            Assert.Null(result.WeaknessWindow);
        }

        // --- GetInterestChangeBeatAsync ---

        [Fact]
        public async Task GetInterestChangeBeatAsync_Returns_Null()
        {
            var ctx = new InterestChangeContext(
                dateeName: "TestDatee",
                interestBefore: 10,
                interestAfter: 16,
                newState: InterestState.VeryIntoIt
            );

            var result = await _adapter.GetInterestChangeBeatAsync(ctx);

            Assert.Null(result);
        }

        // --- DialogueOption construction ---

        [Fact]
        public void DialogueOption_Stores_All_Properties()
        {
            var option = new DialogueOption(
                StatType.Rizz,
                "Smooth move.",
                callbackTurnNumber: 3,
                comboName: "DoubleRizz",
                hasTellBonus: true
            );

            Assert.Equal(StatType.Rizz, option.Stat);
            Assert.Equal("Smooth move.", option.IntendedText);
            Assert.Equal(3, option.CallbackTurnNumber);
            Assert.Equal("DoubleRizz", option.ComboName);
            Assert.True(option.HasTellBonus);
        }

        [Fact]
        public void DialogueOption_Defaults_Optional_Fields_To_Null_And_False()
        {
            var option = new DialogueOption(StatType.Charm, "Hello");

            Assert.Null(option.CallbackTurnNumber);
            Assert.Null(option.ComboName);
            Assert.False(option.HasTellBonus);
        }

        // --- Context type construction ---

        [Fact]
        public void InterestChangeContext_Stores_All_Properties()
        {
            var ctx = new InterestChangeContext("Bob", 5, 16, InterestState.VeryIntoIt);

            Assert.Equal("Bob", ctx.DateeName);
            Assert.Equal(5, ctx.InterestBefore);
            Assert.Equal(16, ctx.InterestAfter);
            Assert.Equal(InterestState.VeryIntoIt, ctx.NewState);
        }
    }
}
