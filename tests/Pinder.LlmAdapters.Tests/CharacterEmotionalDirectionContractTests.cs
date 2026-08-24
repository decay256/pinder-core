using System.Collections.Generic;
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
