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
            "joy", "shame", "fear", "anger", "ambivalence",
        };

        [Theory]
        [InlineData("schema_version", "The contract schema version string. Must be exactly 'emotional_director.v1'.")]
        [InlineData("primary_emotion", "The single dominant primary emotion chosen from the configured vocabulary.")]
        [InlineData("intensity", "The strength, movement, and trajectory of the primary emotion.")]
        [InlineData("underlying_feeling", "The deeper, more vulnerable feeling or subtext beneath the primary emotion.")]
        [InlineData("interpretation", "How the subject character interprets the counterpart's message and intention.")]
        [InlineData("impulse", "A behavioral urge or instinct in third-person or infinitive form (e.g. 'pull back and test their sincerity').")]
        [InlineData("restraint", "What holds the subject character back from fully acting on their impulse.")]
        [InlineData("response_posture", "Natural-language prose describing the character's behavioral stance/posture in response to the moment. Must explicitly mention or include the chosen primary_emotion.")]
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
            const string json = "{\"schema_version\":\"emotional_director.v1\",\"primary_emotion\":\"shame\",\"intensity\":\"strong and rising\",\"underlying_feeling\":\"fear of exposure\",\"interpretation\":\"reads the moment as risky but meaningful\",\"impulse\":\"wants to risk a sincere admission\",\"restraint\":\"hedges before becoming fully vulnerable\",\"response_posture\":\"Writing from shame, they hedge before risking a sincere admission.\"}";

            bool accepted = CharacterEmotionalDirectionContract.TryParse(
                json, true, Allowed, out var direction, out string error);

            Assert.True(accepted, error);
            Assert.Equal("shame", direction!.PrimaryEmotion);
            Assert.Contains("shame", direction.ResponsePosture);
        }

        [Fact]
        public void TryParse_RejectsEmotionOutsideConfiguredVocabulary()
        {
            const string json = "{\"schema_version\":\"emotional_director.v1\",\"primary_emotion\":\"contempt\",\"intensity\":\"strong and rising\",\"underlying_feeling\":\"fear of disrespect\",\"interpretation\":\"reads the moment as dismissive\",\"impulse\":\"wants to retaliate\",\"restraint\":\"keeps the answer controlled\",\"response_posture\":\"Writing from contempt, they sharpen every observation.\"}";

            bool accepted = CharacterEmotionalDirectionContract.TryParse(
                json, true, Allowed, out _, out string error);

            Assert.False(accepted);
            Assert.Equal("unsupported_primary_emotion", error);
        }

        [Fact]
        public void TryParse_RejectsPostureThatDoesNotNameEmotion()
        {
            const string json = "{\"schema_version\":\"emotional_director.v1\",\"primary_emotion\":\"anger\",\"intensity\":\"strong and rising\",\"underlying_feeling\":\"fear of disrespect\",\"interpretation\":\"reads the moment as dismissive\",\"impulse\":\"wants to challenge them\",\"restraint\":\"keeps the answer controlled\",\"response_posture\":\"They become clipped and confrontational.\"}";

            bool accepted = CharacterEmotionalDirectionContract.TryParse(
                json, true, Allowed, out _, out string error);

            Assert.False(accepted);
            Assert.Equal("response_posture_omits_primary_emotion", error);
        }
    }
}
