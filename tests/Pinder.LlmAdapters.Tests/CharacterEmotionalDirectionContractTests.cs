using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public class CharacterEmotionalDirectionContractTests
    {
        private static readonly IReadOnlyList<string> Allowed = new[]
        {
            "joy", "shame", "fear", "anger", "relief",
        };

        [Theory]
        [InlineData("schema_version", "The contract schema version string. Must be exactly 'emotional_director.v2'.")]
        [InlineData("primary_emotion", "The single dominant concrete felt emotion chosen from the configured vocabulary.")]
        [InlineData("secondary_emotion", "A distinct configured concrete emotion, or literal 'none'.")]
        [InlineData("regulatory_state", "The character's regulatory state.")]
        [InlineData("activation", "Emotional activation from 1 through 5.")]
        [InlineData("trajectory", "The movement of the emotional beat.")]
        [InlineData("core_threat_or_desire", "Concise vulnerable threat or desire driving the reaction.")]
        [InlineData("interpretation", "How the latest visible message lands for this character.")]
        [InlineData("impulse", "Immediate behavioral urge, never drafted dialogue.")]
        [InlineData("restraint", "What prevents full expression.")]
        [InlineData("response_posture", "Actionable performance direction, never drafted dialogue.")]
        public void BuildJsonSchema_IncludesExpectedPropertyDescriptions(string propertyName, string expectedDescription)
        {
            var request = CharacterEmotionalDirectionContract.CreateRequest(
                systemPrompt: "system",
                userMessage: "user",
                temperature: 0.7,
                maxTokens: 500,
                metadata: new Dictionary<string, string>(),
                phase: LlmPhase.EmotionalDirector,
                allowedEmotions: Allowed);

            var schema = JObject.Parse(request.JsonSchema);
            var properties = schema["properties"] as JObject;
            Assert.NotNull(properties);
            Assert.True(properties!.ContainsKey(propertyName), $"Property '{propertyName}' missing from schema.");
            Assert.Equal(expectedDescription, properties[propertyName]?["description"]?.Value<string>());
        }

        [Fact]
        public void TryParse_AcceptsConfiguredEmotionNamedInPosture()
        {
            const string json = "{\"schema_version\":\"emotional_director.v2\",\"primary_emotion\":\"shame\",\"secondary_emotion\":\"none\",\"regulatory_state\":\"controlled\",\"activation\":4,\"trajectory\":\"escalating\",\"core_threat_or_desire\":\"fear of exposure\",\"interpretation\":\"reads the moment as risky but meaningful\",\"impulse\":\"wants to risk a sincere admission\",\"restraint\":\"hedges before becoming fully vulnerable\",\"response_posture\":\"hedges before risking a sincere admission\"}";

            bool accepted = CharacterEmotionalDirectionContract.TryParse(
                json, true, Allowed, out var direction, out string error);

            Assert.True(accepted, error);
            Assert.Equal("shame", direction!.PrimaryEmotion);
            Assert.Equal("hedges before risking a sincere admission", direction.ResponsePosture);
        }

        [Fact]
        public void TryParse_RejectsEmotionOutsideConfiguredVocabulary()
        {
            const string json = "{\"schema_version\":\"emotional_director.v2\",\"primary_emotion\":\"contempt\",\"secondary_emotion\":\"none\",\"regulatory_state\":\"controlled\",\"activation\":4,\"trajectory\":\"escalating\",\"core_threat_or_desire\":\"fear of disrespect\",\"interpretation\":\"reads the moment as dismissive\",\"impulse\":\"wants to retaliate\",\"restraint\":\"keeps the answer controlled\",\"response_posture\":\"sharpens every observation\"}";

            bool accepted = CharacterEmotionalDirectionContract.TryParse(
                json, true, Allowed, out _, out string error);

            Assert.False(accepted);
            Assert.Equal("unsupported_primary_emotion", error);
        }

        [Fact]
        public void TryParse_RejectsConflictedWithoutSecondaryEmotion()
        {
            const string json = "{\"schema_version\":\"emotional_director.v2\",\"primary_emotion\":\"anger\",\"secondary_emotion\":\"none\",\"regulatory_state\":\"conflicted\",\"activation\":4,\"trajectory\":\"volatile\",\"core_threat_or_desire\":\"fear of disrespect\",\"interpretation\":\"reads the moment as dismissive\",\"impulse\":\"wants to challenge them\",\"restraint\":\"keeps the answer controlled\",\"response_posture\":\"becomes clipped and confrontational\"}";

            bool accepted = CharacterEmotionalDirectionContract.TryParse(
                json, true, Allowed, out _, out string error);

            Assert.False(accepted);
            Assert.Equal("conflicted_requires_secondary_emotion", error);
        }
    }
}
