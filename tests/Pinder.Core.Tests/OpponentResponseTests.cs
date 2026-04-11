using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Tests for OpponentResponse, Tell, WeaknessWindow, and CallbackOpportunity types.
    /// Prototype maturity: happy-path construction and property tests.
    /// </summary>
    public class OpponentResponseTests
    {
        // --- OpponentResponse ---

        [Fact]
        public void OpponentResponse_Stores_MessageText()
        {
            var response = new OpponentResponse("Hello back!");
            Assert.Equal("Hello back!", response.MessageText);
        }

        [Fact]
        public void OpponentResponse_Defaults_Optional_Fields_To_Null()
        {
            var response = new OpponentResponse("Hi");
            Assert.Null(response.DetectedTell);
            Assert.Null(response.WeaknessWindow);
        }

        [Fact]
        public void OpponentResponse_Stores_DetectedTell()
        {
            var tell = new Tell(StatType.Charm, "They blushed");
            var response = new OpponentResponse("Hi", detectedTell: tell);

            Assert.NotNull(response.DetectedTell);
            Assert.Equal(StatType.Charm, response.DetectedTell!.Stat);
            Assert.Equal("They blushed", response.DetectedTell.Description);
        }

        [Fact]
        public void OpponentResponse_Stores_WeaknessWindow()
        {
            var window = new WeaknessWindow(StatType.Wit, 3);
            var response = new OpponentResponse("Hmm", weaknessWindow: window);

            Assert.NotNull(response.WeaknessWindow);
            Assert.Equal(StatType.Wit, response.WeaknessWindow!.DefendingStat);
            Assert.Equal(3, response.WeaknessWindow.DcReduction);
        }

        [Fact]
        public void OpponentResponse_Stores_Both_Tell_And_WeaknessWindow()
        {
            var tell = new Tell(StatType.Honesty, "Nervous laugh");
            var window = new WeaknessWindow(StatType.Honesty, 2);
            var response = new OpponentResponse("Uh...", tell, window);

            Assert.NotNull(response.DetectedTell);
            Assert.NotNull(response.WeaknessWindow);
        }

        [Fact]
        public void OpponentResponse_Throws_On_Null_MessageText()
        {
            Assert.Throws<ArgumentNullException>(() => new OpponentResponse(null!));
        }

        // --- Tell ---

        [Fact]
        public void Tell_Stores_Properties()
        {
            var tell = new Tell(StatType.Rizz, "Fidgeting");
            Assert.Equal(StatType.Rizz, tell.Stat);
            Assert.Equal("Fidgeting", tell.Description);
        }

        [Fact]
        public void Tell_Throws_On_Null_Description()
        {
            Assert.Throws<ArgumentNullException>(() => new Tell(StatType.Charm, null!));
        }

        // --- WeaknessWindow ---

        [Fact]
        public void WeaknessWindow_Stores_Properties()
        {
            var window = new WeaknessWindow(StatType.Chaos, 5);
            Assert.Equal(StatType.Chaos, window.DefendingStat);
            Assert.Equal(5, window.DcReduction);
        }

        // --- CallbackOpportunity ---

        [Fact]
        public void CallbackOpportunity_Stores_Properties()
        {
            var cb = new CallbackOpportunity("pizza_story", 3);
            Assert.Equal("pizza_story", cb.TopicKey);
            Assert.Equal(3, cb.TurnIntroduced);
        }

        [Fact]
        public void CallbackOpportunity_Throws_On_Null_TopicKey()
        {
            Assert.Throws<ArgumentNullException>(() => new CallbackOpportunity(null!, 1));
        }

        // --- Context type new optional fields ---

        [Fact]
        public void DialogueContext_New_Fields_Default_To_Null_Or_Zero()
        {
            var ctx = new DialogueContext(
                playerPrompt: "p",
                opponentPrompt: "o",
                conversationHistory: new List<(string, string)>(),
                opponentLastMessage: "",
                activeTraps: new List<string>(),
                currentInterest: 10);

            Assert.Null(ctx.ShadowThresholds);
            Assert.Null(ctx.CallbackOpportunities);
            Assert.Equal(0, ctx.HorninessLevel);
            Assert.False(ctx.RequiresRizzOption);
            Assert.Null(ctx.ActiveTrapInstructions);
        }

        [Fact]
        public void DialogueContext_Accepts_New_Optional_Fields()
        {
            var shadows = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Despair, 7 }
            };
            var callbacks = new List<CallbackOpportunity>
            {
                new CallbackOpportunity("topic1", 2)
            };

            var ctx = new DialogueContext(
                playerPrompt: "p",
                opponentPrompt: "o",
                conversationHistory: new List<(string, string)>(),
                opponentLastMessage: "",
                activeTraps: new List<string>(),
                currentInterest: 10,
                shadowThresholds: shadows,
                callbackOpportunities: callbacks,
                horninessLevel: 7,
                requiresRizzOption: true,
                activeTrapInstructions: new[] { "Taint: say something weird" });

            Assert.NotNull(ctx.ShadowThresholds);
            Assert.Single(ctx.ShadowThresholds!);
            Assert.NotNull(ctx.CallbackOpportunities);
            Assert.Single(ctx.CallbackOpportunities!);
            Assert.Equal(7, ctx.HorninessLevel);
            Assert.True(ctx.RequiresRizzOption);
            Assert.NotNull(ctx.ActiveTrapInstructions);
            Assert.Single(ctx.ActiveTrapInstructions!);
        }

        [Fact]
        public void DeliveryContext_New_Fields_Default_To_Null()
        {
            var option = new DialogueOption(StatType.Charm, "Hi");
            var ctx = new DeliveryContext(
                playerPrompt: "p",
                opponentPrompt: "o",
                conversationHistory: new List<(string, string)>(),
                opponentLastMessage: "",
                chosenOption: option,
                outcome: Rolls.FailureTier.None,
                beatDcBy: 3,
                activeTraps: new List<string>());

            Assert.Null(ctx.ShadowThresholds);
            Assert.Null(ctx.ActiveTrapInstructions);
        }

        [Fact]
        public void DeliveryContext_Accepts_New_Optional_Fields()
        {
            var shadows = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Madness, 4 }
            };
            var option = new DialogueOption(StatType.Charm, "Hi");
            var ctx = new DeliveryContext(
                playerPrompt: "p",
                opponentPrompt: "o",
                conversationHistory: new List<(string, string)>(),
                opponentLastMessage: "",
                chosenOption: option,
                outcome: Rolls.FailureTier.None,
                beatDcBy: 3,
                activeTraps: new List<string>(),
                shadowThresholds: shadows,
                activeTrapInstructions: new[] { "Be creepy" });

            Assert.NotNull(ctx.ShadowThresholds);
            Assert.NotNull(ctx.ActiveTrapInstructions);
        }

        [Fact]
        public void OpponentContext_New_Fields_Default_To_Null()
        {
            var ctx = new OpponentContext(
                playerPrompt: "p",
                opponentPrompt: "o",
                conversationHistory: new List<(string, string)>(),
                opponentLastMessage: "",
                activeTraps: new List<string>(),
                currentInterest: 10,
                playerDeliveredMessage: "Hello!",
                interestBefore: 10,
                interestAfter: 11,
                responseDelayMinutes: 1.5);

            Assert.Null(ctx.ShadowThresholds);
            Assert.Null(ctx.ActiveTrapInstructions);
        }

        [Fact]
        public void OpponentContext_Accepts_New_Optional_Fields()
        {
            var shadows = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Dread, 6 }
            };
            var ctx = new OpponentContext(
                playerPrompt: "p",
                opponentPrompt: "o",
                conversationHistory: new List<(string, string)>(),
                opponentLastMessage: "",
                activeTraps: new List<string>(),
                currentInterest: 10,
                playerDeliveredMessage: "Hello!",
                interestBefore: 10,
                interestAfter: 11,
                responseDelayMinutes: 1.5,
                shadowThresholds: shadows,
                activeTrapInstructions: new[] { "Add dread" });

            Assert.NotNull(ctx.ShadowThresholds);
            Assert.NotNull(ctx.ActiveTrapInstructions);
            Assert.Single(ctx.ActiveTrapInstructions!);
        }
    }
}
