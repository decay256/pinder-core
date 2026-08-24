using System.Collections.Generic;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public class AvatarEmotionalDirectionContractTests
    {
        private static readonly IReadOnlyList<string> Allowed = new[]
        {
            "joy", "shame", "fear", "anger", "ambivalence",
        };

        [Fact]
        public void TryParse_AcceptsConfiguredEmotionNamedInPosture()
        {
            const string json = "{\"schema_version\":\"avatar_emotional_direction.v1\",\"primary_emotion\":\"shame\",\"response_posture\":\"Writing from shame, they hedge before risking a sincere admission.\"}";

            bool accepted = AvatarEmotionalDirectionContract.TryParse(
                json, true, Allowed, out var direction, out string error);

            Assert.True(accepted, error);
            Assert.Equal("shame", direction!.PrimaryEmotion);
            Assert.Contains("shame", direction.ResponsePosture);
        }

        [Fact]
        public void TryParse_RejectsEmotionOutsideConfiguredVocabulary()
        {
            const string json = "{\"schema_version\":\"avatar_emotional_direction.v1\",\"primary_emotion\":\"contempt\",\"response_posture\":\"Writing from contempt, they sharpen every observation.\"}";

            bool accepted = AvatarEmotionalDirectionContract.TryParse(
                json, true, Allowed, out _, out string error);

            Assert.False(accepted);
            Assert.Equal("unsupported_primary_emotion", error);
        }

        [Fact]
        public void TryParse_RejectsPostureThatDoesNotNameEmotion()
        {
            const string json = "{\"schema_version\":\"avatar_emotional_direction.v1\",\"primary_emotion\":\"anger\",\"response_posture\":\"They become clipped and confrontational.\"}";

            bool accepted = AvatarEmotionalDirectionContract.TryParse(
                json, true, Allowed, out _, out string error);

            Assert.False(accepted);
            Assert.Equal("response_posture_omits_primary_emotion", error);
        }
    }
}
