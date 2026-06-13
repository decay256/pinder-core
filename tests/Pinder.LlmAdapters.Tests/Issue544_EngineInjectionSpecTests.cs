using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// Spec-driven tests for Issue #544: [ENGINE] injection blocks in LLM calls.
    /// Tests verify all 7 acceptance criteria from the spec:
    ///   AC1: Options injection format replaces current BuildDialogueOptionsPrompt user content
    ///   AC2: Delivery injection format includes roll context from rule YAML
    ///   AC3: Datee injection format includes Interest narrative per band
    ///   AC4: Interest narratives configurable (6 bands defined)
    ///   AC5: Roll context narratives sourced from enriched YAML flavor fields
    ///   AC6: Unit tests verify injection format correctness
    ///   AC7: Build clean
    /// </summary>
    public partial class Issue544_EngineInjectionSpecTests
    {
        // ── Test Helpers ──

        private static DialogueContext MakeDialogueContext(
            int currentTurn = 3,
            string playerName = "Velvet",
            string dateeName = "Sable",
            int currentInterest = 14,
            IReadOnlyList<(string Sender, string Text)>? conversationHistory = null,
            string dateeLastMessage = "hey",
            Dictionary<ShadowStatType, int>? shadowThresholds = null,
            List<CallbackOpportunity>? callbackOpportunities = null,
            int horninessLevel = 0,
            bool requiresRizzOption = false,
            string[]? activeTrapInstructions = null,
            string[]? activeTraps = null,
            string playerTextingStyle = "")
        {
            return new DialogueContext(
                playerAvatarPrompt: "player system prompt",
                dateePrompt: "datee system prompt",
                conversationHistory: conversationHistory ?? new List<(string, string)>
                {
                    ("Velvet", "hey there"),
                    ("Sable", "omg hi!!")
                },
                dateeLastMessage: dateeLastMessage,
                activeTraps: activeTraps ?? Array.Empty<string>(),
                currentInterest: currentInterest,
                shadowThresholds: shadowThresholds,
                callbackOpportunities: callbackOpportunities,
                horninessLevel: horninessLevel,
                requiresRizzOption: requiresRizzOption,
                activeTrapInstructions: activeTrapInstructions,
                playerName: playerName,
                dateeName: dateeName,
                currentTurn: currentTurn,
                playerTextingStyle: playerTextingStyle);
        }

        // #1138: MakeDeliveryContext() removed — the delivery prompt builder it
        // fed (BuildDeliveryPrompt) no longer exists; delivery is now the
        // deterministic DeliveryOverlay (#1125).

        private static DateeContext MakeDateeContext(
            int interestBefore = 12,
            int interestAfter = 14,
            string playerDeliveredMessage = "clever line here",
            double responseDelayMinutes = 2.5,
            string playerName = "Velvet",
            string dateeName = "Sable",
            FailureTier deliveryTier = FailureTier.None)
        {
            return new DateeContext(
                dateePrompt: "datee system prompt",
                conversationHistory: new List<(string, string)>
                {
                    ("Velvet", "hey there"),
                    ("Sable", "omg hi!!")
                },
                dateeLastMessage: "omg hi!!",
                activeTraps: Array.Empty<string>(),
                currentInterest: interestAfter,
                playerDeliveredMessage: playerDeliveredMessage,
                interestBefore: interestBefore,
                interestAfter: interestAfter,
                responseDelayMinutes: responseDelayMinutes,
                playerName: playerName,
                dateeName: dateeName,
                deliveryTier: deliveryTier);
        }

        // ═══════════════════════════════════════════════════════════════
        // AC1: Options injection format — [ENGINE — Turn N] block
        // ═══════════════════════════════════════════════════════════════

        // Mutation: would catch if [ENGINE] header was hardcoded to a fixed turn number
        [Theory]
        [InlineData(1)]
        [InlineData(7)]
        [InlineData(20)]
        public void AC1_OptionsInjection_TurnNumberInterpolated(int turn)
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(currentTurn: turn));
            Assert.Contains($"[ENGINE — Turn {turn}]", result);
        }

        // Mutation: would catch if player name was hardcoded instead of using context
        [Fact]
        public void AC1_OptionsInjection_UsesActualPlayerName()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(playerName: "Brick"));
            Assert.Contains("Brick is deciding what to send next", result);
            Assert.Contains("Generate 3 options for what Brick might send", result);
        }

        // Mutation: would catch if interest value was omitted or hardcoded
        [Fact]
        public void AC1_OptionsInjection_ShowsCurrentInterest()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(currentInterest: 22));
            Assert.Contains("Interest: 22/25", result);
        }

        // Mutation: would catch if OPTION_A/B/C/D format instruction was missing
        [Fact]
        public void AC1_OptionsInjection_HasOptionFormatInstruction()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext());
            Assert.Contains("OPTION_A", result);
            Assert.Contains("OPTION_B", result);
        }

        // Mutation: would catch if horniness was not included when >= 6
        [Fact]
        public void AC1_OptionsInjection_IncludesHorninessWhenHigh()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(horninessLevel: 8));
            Assert.Contains("Horniness: 8/10", result);
        }

        // Mutation: would catch if horniness was shown when below 6
        [Fact]
        public void AC1_OptionsInjection_NoHorninessWhenLow()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(horninessLevel: 4));
            Assert.DoesNotContain("Horniness", result);
        }

        // Mutation: would catch if required Rizz was not injected when flag is true
        [Fact]
        public void AC1_OptionsInjection_ShowsRizzRequirement()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(requiresRizzOption: true));
            Assert.Contains("Rizz option", result);
        }

        // Mutation: would catch if callbacks were omitted from ENGINE block
        [Fact]
        public void AC1_OptionsInjection_IncludesCallbackOpportunities()
        {
            var callbacks = new List<CallbackOpportunity>
            {
                new CallbackOpportunity("music taste", 1)
            };
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(currentTurn: 5, callbackOpportunities: callbacks));
            Assert.Contains("music taste", result);
        }

        // Mutation: would catch if callback bonus calculation was wrong (4 turns ago = +2)
        [Fact]
        public void AC1_OptionsInjection_CallbackBonusText_4TurnsAgo()
        {
            var callbacks = new List<CallbackOpportunity>
            {
                new CallbackOpportunity("weather", 1)
            };
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(currentTurn: 5, callbackOpportunities: callbacks));
            Assert.Contains("+2 hidden", result);
        }

        // Mutation: would catch if callback bonus for 2 turns ago was wrong
        [Fact]
        public void AC1_OptionsInjection_CallbackBonusText_2TurnsAgo()
        {
            var callbacks = new List<CallbackOpportunity>
            {
                new CallbackOpportunity("topic", 1)
            };
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(currentTurn: 3, callbackOpportunities: callbacks));
            Assert.Contains("+1 hidden", result);
        }

        // Mutation: would catch if texting style was not injected before ENGINE block
        [Fact]
        public void AC1_OptionsInjection_TextingStyleBeforeEngineBlock()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(playerTextingStyle: "lowercase, ironic, precise"));

            int styleIdx = result.IndexOf("lowercase, ironic, precise");
            int engineIdx = result.IndexOf("[ENGINE — Turn");
            Assert.True(styleIdx >= 0, "Texting style must appear");
            Assert.True(engineIdx >= 0, "ENGINE block must appear");
            Assert.True(styleIdx < engineIdx, "Texting style must precede ENGINE block");
        }

        // Mutation: would catch if active trap instructions were omitted
        [Fact]
        public void AC1_OptionsInjection_IncludesActiveTrapInstructions()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(activeTrapInstructions: new[] { "Your messages all sound desperate" }));
            Assert.Contains("Your messages all sound desperate", result);
        }

        // ═══════════════════════════════════════════════════════════════
        // AC1 Interest Labels in Options Block
        // ═══════════════════════════════════════════════════════════════

        // Mutation: would catch if interest label used wrong emoji or text for Bored range
        [Fact]
        public void AC1_OptionsInjection_InterestLabel_Bored()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(currentInterest: 3));
            Assert.Contains("Bored", result);
            Assert.Contains("disadvantage", result);
        }

        // Mutation: would catch if interest label didn't show advantage for VeryIntoIt
        [Fact]
        public void AC1_OptionsInjection_InterestLabel_VeryIntoIt()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(currentInterest: 18));
            Assert.Contains("Very Into It", result);
            Assert.Contains("advantage", result);
        }

        // Mutation: would catch if Lukewarm label was missing for interest 5-9
        [Fact]
        public void AC1_OptionsInjection_InterestLabel_Lukewarm()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(currentInterest: 7));
            Assert.Contains("Lukewarm", result);
        }

        // Mutation: would catch if "Interested" label used for wrong range
        [Fact]
        public void AC1_OptionsInjection_InterestLabel_Interested()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(currentInterest: 12));
            Assert.Contains("Interested", result);
        }

        // ═══════════════════════════════════════════════════════════════
        // AC2: Delivery injection — [ENGINE — DELIVERY] block
        //
        // #1138: the entire AC2_DeliveryInjection_* block was removed — it
        // asserted on SessionDocumentBuilder.BuildDeliveryPrompt output, but the
        // creative delivery LLM call / prompt builder no longer exists (delivery
        // was collapsed into the deterministic, non-LLM DeliveryOverlay in
        // #1125, and #1138 removed the builder). Overlay parity is covered by
        // the Issue1125 regression tests in Pinder.Core.Tests. RollContextBuilder
        // narrative selection itself is still covered directly by the
        // RollContextBuilder fallback/override tests in EngineInjectionBlockTests.
        // ═══════════════════════════════════════════════════════════════

        // ═══════════════════════════════════════════════════════════════
        // AC3: Datee injection — [ENGINE — DATEE] block
        // ═══════════════════════════════════════════════════════════════

        // Mutation: would catch if ENGINE — DATEE header was missing
        [Fact]
        public void AC3_DateeInjection_HasEngineDateeHeader()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(MakeDateeContext());
            Assert.Contains("[ENGINE — DATEE]", result);
        }

        // Mutation: would catch if datee name was hardcoded
        [Fact]
        public void AC3_DateeInjection_UsesActualDateeName()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(dateeName: "Brick"));
            Assert.Contains("Brick is at Interest", result);
            Assert.Contains("Write Brick's response", result);
        }

        // Mutation: would catch if interest level was not interpolated
        [Fact]
        public void AC3_DateeInjection_ShowsInterestLevel()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(interestAfter: 19));
            Assert.Contains("Interest 19/25", result);
        }

        // Mutation: would catch if interest delta was not shown
        [Fact]
        public void AC3_DateeInjection_ShowsInterestDelta()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(interestBefore: 10, interestAfter: 13));
            Assert.Contains("+3", result);
        }

        // Mutation: would catch if negative delta didn't show minus sign
        [Fact]
        public void AC3_DateeInjection_ShowsNegativeDelta()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(interestBefore: 15, interestAfter: 12));
            Assert.Contains("-3", result);
        }
    }
}
