using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// #372 / #375 \u2014 the active archetype directive must be injected into
    /// the <c>delivery</c> LLM user prompt for any character with a non-null
    /// ActiveArchetype. Before this fix, the directive only reached
    /// <c>dialogue_options</c> + <c>opponent_response</c>, so the rewrite that
    /// produces the actually-sent message scrubbed the archetype voice.
    /// </summary>
    [Trait("Category", "LlmAdapters")]
    public class Issue372_ArchetypeDirectiveDeliveryTests
    {
        private const string SampleDirective =
            "ACTIVE ARCHETYPE: The Peacock (clear)\nUses the opening message to establish status.";

        private static DeliveryContext MakeDeliveryContext(
            string activeArchetypeDirective = null,
            FailureTier outcome = FailureTier.None,
            int beatDcBy = 0)
        {
            return new DeliveryContext(
                playerPrompt: "player prompt",
                opponentPrompt: "opponent prompt",
                conversationHistory: new List<(string Sender, string Text)>(),
                opponentLastMessage: "",
                chosenOption: new DialogueOption(StatType.Charm, "ok cool"),
                outcome: outcome,
                beatDcBy: beatDcBy,
                activeTraps: Array.Empty<string>(),
                playerName: "P",
                opponentName: "O",
                activeArchetypeDirective: activeArchetypeDirective);
        }

        [Fact]
        public void BuildDeliveryPrompt_Success_ContainsActiveArchetypeDirective()
        {
            // Arrange
            var ctx = MakeDeliveryContext(
                activeArchetypeDirective: SampleDirective,
                outcome: FailureTier.None,
                beatDcBy: 7);

            // Act
            string prompt = SessionDocumentBuilder.BuildDeliveryPrompt(ctx);

            // Assert
            Assert.Contains("ACTIVE ARCHETYPE: The Peacock (clear)", prompt);
            Assert.Contains("Uses the opening message to establish status.", prompt);
        }

        [Theory]
        [InlineData(FailureTier.Fumble)]
        [InlineData(FailureTier.Misfire)]
        [InlineData(FailureTier.TropeTrap)]
        [InlineData(FailureTier.Catastrophe)]
        [InlineData(FailureTier.Legendary)]
        public void BuildDeliveryPrompt_Failure_ContainsActiveArchetypeDirective(FailureTier tier)
        {
            // Arrange
            var ctx = MakeDeliveryContext(
                activeArchetypeDirective: SampleDirective,
                outcome: tier,
                beatDcBy: -5);

            // Act
            string prompt = SessionDocumentBuilder.BuildDeliveryPrompt(ctx);

            // Assert: directive must reach BOTH success and failure branches.
            // Otherwise a Fumble/Catastrophe rewrite scrubs the archetype voice.
            Assert.Contains("ACTIVE ARCHETYPE: The Peacock (clear)", prompt);
        }

        [Fact]
        public void BuildDeliveryPrompt_NullDirective_DoesNotInjectAnything()
        {
            var ctx = MakeDeliveryContext(activeArchetypeDirective: null, beatDcBy: 4);
            string prompt = SessionDocumentBuilder.BuildDeliveryPrompt(ctx);
            Assert.DoesNotContain("ACTIVE ARCHETYPE", prompt);
        }

        [Fact]
        public void BuildDeliveryPrompt_EmptyDirective_DoesNotInjectAnything()
        {
            var ctx = MakeDeliveryContext(activeArchetypeDirective: "", beatDcBy: 4);
            string prompt = SessionDocumentBuilder.BuildDeliveryPrompt(ctx);
            Assert.DoesNotContain("ACTIVE ARCHETYPE", prompt);
        }
    }
}
