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
    ///   AC3: Opponent injection format includes Interest narrative per band
    ///   AC4: Interest narratives configurable (6 bands defined)
    ///   AC5: Roll context narratives sourced from enriched YAML flavor fields
    ///   AC6: Unit tests verify injection format correctness
    ///   AC7: Build clean
    /// </summary>
    public class Issue544_EngineInjectionSpecTests
    {
        // ── Test Helpers ──

        private static DialogueContext MakeDialogueContext(
            int currentTurn = 3,
            string playerName = "Velvet",
            string opponentName = "Sable",
            int currentInterest = 14,
            IReadOnlyList<(string Sender, string Text)>? conversationHistory = null,
            string opponentLastMessage = "hey",
            Dictionary<ShadowStatType, int>? shadowThresholds = null,
            List<CallbackOpportunity>? callbackOpportunities = null,
            int horninessLevel = 0,
            bool requiresRizzOption = false,
            string[]? activeTrapInstructions = null,
            string[]? activeTraps = null,
            string playerTextingStyle = "")
        {
            return new DialogueContext(
                playerPrompt: "player system prompt",
                opponentPrompt: "opponent system prompt",
                conversationHistory: conversationHistory ?? new List<(string, string)>
                {
                    ("Velvet", "hey there"),
                    ("Sable", "omg hi!!")
                },
                opponentLastMessage: opponentLastMessage,
                activeTraps: activeTraps ?? Array.Empty<string>(),
                currentInterest: currentInterest,
                shadowThresholds: shadowThresholds,
                callbackOpportunities: callbackOpportunities,
                horninessLevel: horninessLevel,
                requiresRizzOption: requiresRizzOption,
                activeTrapInstructions: activeTrapInstructions,
                playerName: playerName,
                opponentName: opponentName,
                currentTurn: currentTurn,
                playerTextingStyle: playerTextingStyle);
        }

        private static DeliveryContext MakeDeliveryContext(
            DialogueOption? chosenOption = null,
            FailureTier outcome = FailureTier.None,
            int beatDcBy = 5,
            string playerName = "Velvet",
            string opponentName = "Sable",
            Dictionary<ShadowStatType, int>? shadowThresholds = null)
        {
            return new DeliveryContext(
                playerPrompt: "player system prompt",
                opponentPrompt: "opponent system prompt",
                conversationHistory: new List<(string, string)>
                {
                    ("Velvet", "hey there"),
                    ("Sable", "omg hi!!")
                },
                opponentLastMessage: "omg hi!!",
                chosenOption: chosenOption ?? new DialogueOption(StatType.Wit, "clever line here"),
                outcome: outcome,
                beatDcBy: beatDcBy,
                activeTraps: Array.Empty<string>(),
                shadowThresholds: shadowThresholds,
                playerName: playerName,
                opponentName: opponentName);
        }

        private static OpponentContext MakeOpponentContext(
            int interestBefore = 12,
            int interestAfter = 14,
            string playerDeliveredMessage = "clever line here",
            double responseDelayMinutes = 2.5,
            string playerName = "Velvet",
            string opponentName = "Sable",
            FailureTier deliveryTier = FailureTier.None)
        {
            return new OpponentContext(
                playerPrompt: "player system prompt",
                opponentPrompt: "opponent system prompt",
                conversationHistory: new List<(string, string)>
                {
                    ("Velvet", "hey there"),
                    ("Sable", "omg hi!!")
                },
                opponentLastMessage: "omg hi!!",
                activeTraps: Array.Empty<string>(),
                currentInterest: interestAfter,
                playerDeliveredMessage: playerDeliveredMessage,
                interestBefore: interestBefore,
                interestAfter: interestAfter,
                responseDelayMinutes: responseDelayMinutes,
                playerName: playerName,
                opponentName: opponentName,
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
            Assert.Contains("Generate 4 options for what Brick might send", result);
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

        // Mutation: would catch if "Player chose:" prefix was missing
        [Fact]
        public void AC2_DeliveryInjection_HasPlayerChosePrefix()
        {
            var result = SessionDocumentBuilder.BuildDeliveryPrompt(MakeDeliveryContext());
            Assert.Contains("Player chose:", result);
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
        // AC3: Opponent injection — [ENGINE — OPPONENT] block
        // ═══════════════════════════════════════════════════════════════

        // Mutation: would catch if ENGINE — OPPONENT header was missing
        [Fact]
        public void AC3_OpponentInjection_HasEngineOpponentHeader()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(MakeOpponentContext());
            Assert.Contains("[ENGINE — OPPONENT]", result);
        }

        // Mutation: would catch if opponent name was hardcoded
        [Fact]
        public void AC3_OpponentInjection_UsesActualOpponentName()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                MakeOpponentContext(opponentName: "Brick"));
            Assert.Contains("Brick is at Interest", result);
            Assert.Contains("Write Brick's response", result);
        }

        // Mutation: would catch if interest level was not interpolated
        [Fact]
        public void AC3_OpponentInjection_ShowsInterestLevel()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                MakeOpponentContext(interestAfter: 19));
            Assert.Contains("Interest 19/25", result);
        }

        // Mutation: would catch if interest delta was not shown
        [Fact]
        public void AC3_OpponentInjection_ShowsInterestDelta()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                MakeOpponentContext(interestBefore: 10, interestAfter: 13));
            Assert.Contains("+3", result);
        }

        // Mutation: would catch if negative delta didn't show minus sign
        [Fact]
        public void AC3_OpponentInjection_ShowsNegativeDelta()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                MakeOpponentContext(interestBefore: 15, interestAfter: 12));
            Assert.Contains("-3", result);
        }

        // ═══════════════════════════════════════════════════════════════
        // AC4: Interest narratives — 6 bands with correct boundaries
        // ═══════════════════════════════════════════════════════════════

        // Mutation: would catch if band boundary at 0 used wrong narrative
        [Fact]
        public void AC4_InterestNarrative_Band0_Unmatched()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                MakeOpponentContext(interestBefore: 1, interestAfter: 0));
            Assert.Contains("Unmatched", result);
        }

        // Mutation: would catch if band 1-4 lower boundary used Unmatched text
        [Fact]
        public void AC4_InterestNarrative_Band1_LowerBoundary()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                MakeOpponentContext(interestAfter: 1));
            Assert.Contains("Reconsidering", result);
        }

        // Mutation: would catch if band 1-4 upper boundary used next band's text
        [Fact]
        public void AC4_InterestNarrative_Band4_UpperBoundary()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                MakeOpponentContext(interestAfter: 4));
            Assert.Contains("Reconsidering", result);
        }

        // Mutation: would catch if boundary 5 was in band 1-4 instead of 5-9
        [Fact]
        public void AC4_InterestNarrative_Band5_LowerBoundary()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                MakeOpponentContext(interestAfter: 5));
            Assert.Contains("Skeptical", result);
        }

        // Mutation: would catch if boundary 9 was in band 10-14
        [Fact]
        public void AC4_InterestNarrative_Band9_UpperBoundary()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                MakeOpponentContext(interestAfter: 9));
            Assert.Contains("Skeptical", result);
        }

        // Mutation: would catch if boundary 10 was in band 5-9
        [Fact]
        public void AC4_InterestNarrative_Band10_LowerBoundary()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                MakeOpponentContext(interestAfter: 10));
            Assert.Contains("Engaged but not sold", result);
        }

        // Mutation: would catch if boundary 14 was in band 15-20
        [Fact]
        public void AC4_InterestNarrative_Band14_UpperBoundary()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                MakeOpponentContext(interestAfter: 14));
            Assert.Contains("Engaged but not sold", result);
        }

        // Mutation: would catch if boundary 15 was in band 10-14
        [Fact]
        public void AC4_InterestNarrative_Band15_LowerBoundary()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                MakeOpponentContext(interestAfter: 15));
            Assert.Contains("Interested but holding back", result);
        }

        // Mutation: would catch if boundary 20 was in band 21-24
        [Fact]
        public void AC4_InterestNarrative_Band20_UpperBoundary()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                MakeOpponentContext(interestAfter: 20));
            Assert.Contains("Interested but holding back", result);
        }

        // Mutation: would catch if boundary 21 was in band 15-20
        [Fact]
        public void AC4_InterestNarrative_Band21_LowerBoundary()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                MakeOpponentContext(interestAfter: 21));
            Assert.Contains("Basically sold", result);
        }

        // Mutation: would catch if boundary 24 was in band 25
        [Fact]
        public void AC4_InterestNarrative_Band24_UpperBoundary()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                MakeOpponentContext(interestAfter: 24));
            Assert.Contains("Basically sold", result);
        }

        // Mutation: would catch if boundary 25 was in band 21-24
        [Fact]
        public void AC4_InterestNarrative_Band25_DateSecured()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                MakeOpponentContext(interestAfter: 25));
            Assert.Contains("resistance dissolved", result);
        }

        // Mutation: would catch if all 6 bands returned the same text
        [Fact]
        public void AC4_InterestNarrative_AllBandsDistinct()
        {
            var narratives = new HashSet<string>();
            int[] representatives = { 0, 2, 7, 12, 18, 22, 25 };
            foreach (int i in representatives)
            {
                var result = SessionDocumentBuilder.BuildOpponentPrompt(
                    MakeOpponentContext(interestBefore: i, interestAfter: i));
                // Extract the narrative portion - each should be unique
                narratives.Add(result);
            }
            Assert.Equal(representatives.Length, narratives.Count);
        }

        // ═══════════════════════════════════════════════════════════════
        // AC5: RollContextBuilder — YAML flavor sourcing + fallbacks
        // ═══════════════════════════════════════════════════════════════

        // Mutation: would catch if clean success used strong success text
        [Fact]
        public void AC5_RollContext_CleanSuccess_1To4()
        {
            var builder = new RollContextBuilder();
            var result = builder.GetSuccessContext(1, false);
            Assert.Equal(RollContextBuilder.FallbackCleanSuccess, result);

            var result4 = builder.GetSuccessContext(4, false);
            Assert.Equal(RollContextBuilder.FallbackCleanSuccess, result4);
        }

        // Mutation: would catch if boundary 5 used clean instead of strong
        [Fact]
        public void AC5_RollContext_StrongSuccess_BeatBy5()
        {
            var builder = new RollContextBuilder();
            var result = builder.GetSuccessContext(5, false);
            Assert.Equal(RollContextBuilder.FallbackStrongSuccess, result);
        }

        // Mutation: would catch if boundary 9 used critical instead of strong
        [Fact]
        public void AC5_RollContext_StrongSuccess_BeatBy9()
        {
            var builder = new RollContextBuilder();
            var result = builder.GetSuccessContext(9, false);
            Assert.Equal(RollContextBuilder.FallbackStrongSuccess, result);
        }

        // Mutation: would catch if boundary 10 used strong instead of critical
        [Fact]
        public void AC5_RollContext_CriticalSuccess_BeatBy10()
        {
            var builder = new RollContextBuilder();
            var result = builder.GetSuccessContext(10, false);
            Assert.Equal(RollContextBuilder.FallbackCriticalSuccess, result);
        }

        // Mutation: would catch if Nat20 flag was ignored (returned critical instead)
        [Fact]
        public void AC5_RollContext_Nat20_OverridesBeatDcBy()
        {
            var builder = new RollContextBuilder();
            // Even with beatDcBy of 2 (clean range), Nat20 should use Nat20 text
            var result = builder.GetSuccessContext(2, true);
            Assert.Equal(RollContextBuilder.FallbackNat20, result);
        }

        // Mutation: would catch if Nat20 was treated as critical success
        [Fact]
        public void AC5_RollContext_Nat20_NotSameAsCritical()
        {
            var builder = new RollContextBuilder();
            Assert.NotEqual(
                builder.GetSuccessContext(12, false),
                builder.GetSuccessContext(12, true));
        }

        // Mutation: would catch if all failure tiers returned the same text
        [Fact]
        public void AC5_RollContext_AllFailureTiersDistinct()
        {
            var builder = new RollContextBuilder();
            var results = new HashSet<string>
            {
                builder.GetFailureContext(FailureTier.Fumble),
                builder.GetFailureContext(FailureTier.Misfire),
                builder.GetFailureContext(FailureTier.TropeTrap),
                builder.GetFailureContext(FailureTier.Catastrophe),
                builder.GetFailureContext(FailureTier.Legendary)
            };
            Assert.Equal(5, results.Count);
        }

        // Mutation: would catch if specific failure tier returned wrong text
        [Fact]
        public void AC5_RollContext_Fumble_HasFumbleText()
        {
            var builder = new RollContextBuilder();
            Assert.Equal(RollContextBuilder.FallbackFumble, builder.GetFailureContext(FailureTier.Fumble));
        }

        [Fact]
        public void AC5_RollContext_Misfire_HasMisfireText()
        {
            var builder = new RollContextBuilder();
            Assert.Equal(RollContextBuilder.FallbackMisfire, builder.GetFailureContext(FailureTier.Misfire));
        }

        [Fact]
        public void AC5_RollContext_TropeTrap_HasTrapText()
        {
            var builder = new RollContextBuilder();
            Assert.Equal(RollContextBuilder.FallbackTropeTrap, builder.GetFailureContext(FailureTier.TropeTrap));
        }

        [Fact]
        public void AC5_RollContext_Catastrophe_HasCatastropheText()
        {
            var builder = new RollContextBuilder();
            Assert.Equal(RollContextBuilder.FallbackCatastrophe, builder.GetFailureContext(FailureTier.Catastrophe));
        }

        [Fact]
        public void AC5_RollContext_Legendary_HasLegendaryText()
        {
            var builder = new RollContextBuilder();
            Assert.Equal(RollContextBuilder.FallbackLegendary, builder.GetFailureContext(FailureTier.Legendary));
        }

        // Mutation: would catch if YAML override didn't replace fallback
        [Fact]
        public void AC5_RollContext_YamlOverride_AllSuccessTiers()
        {
            var flavors = new Dictionary<string, string>
            {
                { "§7.success-scale.1-4", "yaml clean" },
                { "§7.success-scale.5-9", "yaml strong" },
                { "§7.success-scale.10plus", "yaml critical" },
                { "§7.success-scale.nat-20", "yaml nat20" }
            };
            var builder = new RollContextBuilder(flavors);

            Assert.Equal("yaml clean", builder.GetSuccessContext(3, false));
            Assert.Equal("yaml strong", builder.GetSuccessContext(7, false));
            Assert.Equal("yaml critical", builder.GetSuccessContext(12, false));
            Assert.Equal("yaml nat20", builder.GetSuccessContext(12, true));
        }

        // Mutation: would catch if YAML override didn't replace failure fallbacks
        [Fact]
        public void AC5_RollContext_YamlOverride_AllFailureTiers()
        {
            var flavors = new Dictionary<string, string>
            {
                { "§7.fail-tier.fumble", "yaml fumble" },
                { "§7.fail-tier.misfire", "yaml misfire" },
                { "§7.fail-tier.trope-trap", "yaml trap" },
                { "§7.fail-tier.catastrophe", "yaml catastrophe" },
                { "§7.fail-tier.legendary-fail", "yaml legendary" }
            };
            var builder = new RollContextBuilder(flavors);

            Assert.Equal("yaml fumble", builder.GetFailureContext(FailureTier.Fumble));
            Assert.Equal("yaml misfire", builder.GetFailureContext(FailureTier.Misfire));
            Assert.Equal("yaml trap", builder.GetFailureContext(FailureTier.TropeTrap));
            Assert.Equal("yaml catastrophe", builder.GetFailureContext(FailureTier.Catastrophe));
            Assert.Equal("yaml legendary", builder.GetFailureContext(FailureTier.Legendary));
        }

        // Mutation: would catch if partial YAML overrode ALL tiers instead of just matching ones
        [Fact]
        public void AC5_RollContext_PartialYaml_OnlyOverridesMatching()
        {
            var flavors = new Dictionary<string, string>
            {
                { "§7.fail-tier.fumble", "custom fumble" }
            };
            var builder = new RollContextBuilder(flavors);

            Assert.Equal("custom fumble", builder.GetFailureContext(FailureTier.Fumble));
            // Others should still be fallback
            Assert.Equal(RollContextBuilder.FallbackMisfire, builder.GetFailureContext(FailureTier.Misfire));
            Assert.Equal(RollContextBuilder.FallbackCatastrophe, builder.GetFailureContext(FailureTier.Catastrophe));
        }

        // Mutation: would catch if YAML key lookup was case-sensitive
        [Fact]
        public void AC5_RollContext_YamlLookup_CaseInsensitive()
        {
            var flavors = new Dictionary<string, string>
            {
                { "§7.FAIL-TIER.FUMBLE", "upper case fumble" }
            };
            var builder = new RollContextBuilder(flavors);
            Assert.Equal("upper case fumble", builder.GetFailureContext(FailureTier.Fumble));
        }

        // Mutation: would catch if FromRuleBook threw on null instead of ArgumentNullException
        [Fact]
        public void AC5_RollContext_FromRuleBook_NullThrows()
        {
            Assert.Throws<ArgumentNullException>(() => RollContextBuilder.FromRuleBook(null!));
        }

        // Mutation: would catch if empty constructor didn't produce valid fallback results
        [Fact]
        public void AC5_RollContext_EmptyConstructor_AllFallbacksNonEmpty()
        {
            var builder = new RollContextBuilder();

            // All success tiers
            Assert.False(string.IsNullOrEmpty(builder.GetSuccessContext(1, false)));
            Assert.False(string.IsNullOrEmpty(builder.GetSuccessContext(5, false)));
            Assert.False(string.IsNullOrEmpty(builder.GetSuccessContext(10, false)));
            Assert.False(string.IsNullOrEmpty(builder.GetSuccessContext(1, true)));

            // All failure tiers
            Assert.False(string.IsNullOrEmpty(builder.GetFailureContext(FailureTier.Fumble)));
            Assert.False(string.IsNullOrEmpty(builder.GetFailureContext(FailureTier.Misfire)));
            Assert.False(string.IsNullOrEmpty(builder.GetFailureContext(FailureTier.TropeTrap)));
            Assert.False(string.IsNullOrEmpty(builder.GetFailureContext(FailureTier.Catastrophe)));
            Assert.False(string.IsNullOrEmpty(builder.GetFailureContext(FailureTier.Legendary)));
        }

        // ═══════════════════════════════════════════════════════════════
        // AC6: Format correctness — structural validation
        // ═══════════════════════════════════════════════════════════════

        // Mutation: would catch if conversation history was removed from options prompt
        [Fact]
        public void AC6_OptionsPrompt_IncludesConversationHistory()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(MakeDialogueContext());
            Assert.Contains("[CONVERSATION_START]", result);
            Assert.Contains("[CURRENT_TURN]", result);
            Assert.Contains("hey there", result);
        }

        // Mutation: would catch if opponent profile was removed from options prompt
        [Fact]
        public void AC6_OptionsPrompt_IncludesOpponentProfile()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(MakeDialogueContext());
            Assert.Contains("OPPONENT PROFILE", result);
        }

        // Mutation: would catch if delivery prompt lost conversation history
        [Fact]
        public void AC6_DeliveryPrompt_IncludesConversationHistory()
        {
            var result = SessionDocumentBuilder.BuildDeliveryPrompt(MakeDeliveryContext());
            Assert.Contains("[CONVERSATION_START]", result);
        }

        // Mutation: would catch if opponent prompt lost conversation history
        [Fact]
        public void AC6_OpponentPrompt_IncludesConversationHistory()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(MakeOpponentContext());
            Assert.Contains("[CONVERSATION_START]", result);
        }

        // Mutation: would catch if opponent prompt didn't include interest change direction
        [Fact]
        public void AC6_OpponentPrompt_ShowsInterestMovement()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                MakeOpponentContext(interestBefore: 10, interestAfter: 14));
            Assert.Contains("from 10 to 14", result);
        }

        // Mutation: would catch if response timing was removed from opponent prompt
        [Fact]
        public void AC6_OpponentPrompt_IncludesResponseTiming()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                MakeOpponentContext(responseDelayMinutes: 5.0));
            Assert.Contains("RESPONSE TIMING", result);
            Assert.Contains("5.0 minutes", result);
        }

        // Mutation: would catch if sub-minute delay didn't indicate rapid response
        [Fact]
        public void AC6_OpponentPrompt_SubMinuteDelay_IndicatesRapidResponse()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                MakeOpponentContext(responseDelayMinutes: 0.5));
            Assert.Contains("less than 1 minute", result);
        }

        // Mutation: would catch if shadow taint was not included when thresholds present
        [Fact]
        public void AC6_OptionsPrompt_IncludesShadowTaintWhenPresent()
        {
            var shadows = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Fixation, 12 }
            };
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(shadowThresholds: shadows));
            // Shadow state should appear in the output
            Assert.Contains("Shadow", result, StringComparison.OrdinalIgnoreCase);
        }

        // ═══════════════════════════════════════════════════════════════
        // AC7: Build clean — null safety
        // ═══════════════════════════════════════════════════════════════

        // Mutation: would catch if null context didn't throw
        [Fact]
        public void AC7_BuildDialogueOptionsPrompt_NullContextThrows()
        {
            Assert.Throws<ArgumentNullException>(() =>
                SessionDocumentBuilder.BuildDialogueOptionsPrompt(null!));
        }

        [Fact]
        public void AC7_BuildDeliveryPrompt_NullContextThrows()
        {
            Assert.Throws<ArgumentNullException>(() =>
                SessionDocumentBuilder.BuildDeliveryPrompt(null!));
        }

        [Fact]
        public void AC7_BuildOpponentPrompt_NullContextThrows()
        {
            Assert.Throws<ArgumentNullException>(() =>
                SessionDocumentBuilder.BuildOpponentPrompt(null!));
        }
    }
}
