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

        private static DeliveryContext MakeDeliveryContext(
            DialogueOption? chosenOption = null,
            FailureTier outcome = FailureTier.None,
            int beatDcBy = 5,
            string playerName = "Velvet",
            string dateeName = "Sable",
            Dictionary<ShadowStatType, int>? shadowThresholds = null)
        {
            return new DeliveryContext(
                playerAvatarPrompt: "player system prompt",
                dateePrompt: "datee system prompt",
                conversationHistory: new List<(string, string)>
                {
                    ("Velvet", "hey there"),
                    ("Sable", "omg hi!!")
                },
                dateeLastMessage: "omg hi!!",
                chosenOption: chosenOption ?? new DialogueOption(StatType.Wit, "clever line here"),
                outcome: outcome,
                beatDcBy: beatDcBy,
                activeTraps: Array.Empty<string>(),
                shadowThresholds: shadowThresholds,
                playerName: playerName,
                dateeName: dateeName);
        }

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
                playerAvatarPrompt: "player system prompt",
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
        // ═══════════════════════════════════════════════════════════════

        // Mutation: would catch if ENGINE — DELIVERY block was missing
        [Fact]
        public void AC2_DeliveryInjection_HasEngineDeliveryHeader()
        {
            var result = SessionDocumentBuilder.BuildDeliveryPrompt(MakeDeliveryContext());
            Assert.Contains("[ENGINE — DELIVERY]", result);
        }

        // Mutation: would catch if chosen option text was not included verbatim
        [Fact]
        public void AC2_DeliveryInjection_ContainsChosenOptionText()
        {
            var option = new DialogueOption(StatType.Charm, "you have great taste in music");
            var result = SessionDocumentBuilder.BuildDeliveryPrompt(
                MakeDeliveryContext(chosenOption: option));
            Assert.Contains("you have great taste in music", result);
        }

        // Mutation: would catch if "PLAYER AVATAR chose:" prefix was missing
        [Fact]
        public void AC2_DeliveryInjection_HasPlayerChosePrefix()
        {
            var result = SessionDocumentBuilder.BuildDeliveryPrompt(MakeDeliveryContext());
            Assert.Contains("PLAYER AVATAR chose:", result);
        }

        // Mutation: would catch if "Dice result:" prefix was missing
        [Fact]
        public void AC2_DeliveryInjection_HasDiceResultPrefix()
        {
            var result = SessionDocumentBuilder.BuildDeliveryPrompt(MakeDeliveryContext());
            Assert.Contains("Dice result:", result);
        }

        // Mutation: would catch if "Write the message" instruction was missing
        [Fact]
        public void AC2_DeliveryInjection_HasWriteInstruction()
        {
            var result = SessionDocumentBuilder.BuildDeliveryPrompt(
                MakeDeliveryContext(playerName: "Gerald"));
            Assert.Contains("Write the message Gerald actually sends", result);
        }

        // Mutation: would catch if success roll context used wrong tier text
        [Fact]
        public void AC2_DeliveryInjection_CleanSuccess_HasLandedText()
        {
            var result = SessionDocumentBuilder.BuildDeliveryPrompt(
                MakeDeliveryContext(beatDcBy: 2, outcome: FailureTier.None));
            Assert.Contains("landed", result);
        }

        // Mutation: would catch if strong success (5-9) used clean success text
        [Fact]
        public void AC2_DeliveryInjection_StrongSuccess_DifferentFromClean()
        {
            var clean = SessionDocumentBuilder.BuildDeliveryPrompt(
                MakeDeliveryContext(beatDcBy: 3, outcome: FailureTier.None));
            var strong = SessionDocumentBuilder.BuildDeliveryPrompt(
                MakeDeliveryContext(beatDcBy: 7, outcome: FailureTier.None));
            // The roll context text should differ between clean and strong
            Assert.NotEqual(clean, strong);
        }

        // Mutation: would catch if critical success (10+) text was same as strong
        [Fact]
        public void AC2_DeliveryInjection_CriticalSuccess_HasBestVersionText()
        {
            var result = SessionDocumentBuilder.BuildDeliveryPrompt(
                MakeDeliveryContext(beatDcBy: 12, outcome: FailureTier.None));
            Assert.Contains("best version", result);
        }

        // Mutation: would catch if failure tier name was not present in delivery
        [Theory]
        [InlineData(FailureTier.Fumble, "FUMBLE")]
        [InlineData(FailureTier.Misfire, "MISFIRE")]
        [InlineData(FailureTier.TropeTrap, "TROPE")]
        [InlineData(FailureTier.Catastrophe, "CATASTROPHE")]
        [InlineData(FailureTier.Legendary, "LEGENDARY")]
        public void AC2_DeliveryInjection_FailureTier_ShowsTierName(FailureTier tier, string expectedFragment)
        {
            var result = SessionDocumentBuilder.BuildDeliveryPrompt(
                MakeDeliveryContext(outcome: tier, beatDcBy: -5));
            Assert.Contains(expectedFragment, result, StringComparison.OrdinalIgnoreCase);
        }

        // Mutation: would catch if RollContextBuilder was ignored when provided
        [Fact]
        public void AC2_DeliveryInjection_WithRollContextBuilder_UsesCustomNarrative()
        {
            var flavors = new Dictionary<string, string>
            {
                { "§7.success-scale.1-4", "CUSTOM: clean shot" }
            };
            var builder = new RollContextBuilder(flavors);
            var result = SessionDocumentBuilder.BuildDeliveryPrompt(
                MakeDeliveryContext(beatDcBy: 2), builder);
            Assert.Contains("CUSTOM: clean shot", result);
        }

        // Mutation: would catch if null rollContextBuilder caused exception instead of fallback
        [Fact]
        public void AC2_DeliveryInjection_NullRollContextBuilder_UsesFallback()
        {
            var result = SessionDocumentBuilder.BuildDeliveryPrompt(
                MakeDeliveryContext(beatDcBy: 3), null);
            Assert.Contains(RollContextBuilder.FallbackCleanSuccess, result);
        }

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
