using System;
using System.Collections.Generic;
using System.Reflection;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Prompts;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.Core.Tests
{
    [Trait("Category", "Characters")]
    [Collection("StaticWiring")]
    public sealed class Issue1396_TextingStyleSoftInfluenceTests
    {
        [Fact]
        public void DeliveryVoiceDirective_TreatsTextingStyleAsSoftInfluence()
        {
            var profile = new CharacterProfile(
                new StatBlock(
                    new Dictionary<StatType, int>(),
                    new Dictionary<ShadowStatType, int>()),
                assembledSystemPrompt: "prompt",
                displayName: "Player",
                timing: new TimingProfile(0, 1.0f, 0.0f, "neutral"),
                level: 1,
                textingStyleFragment: "length: lets the sentence breathe",
                characterId: Guid.Parse("14310000-0000-4000-8000-000000000061"));

            string directive = InvokeDeliveryVoiceDirective(profile);

            Assert.Contains("loose expressive influences", directive);
            Assert.Contains("length: lets the sentence breathe", directive);
            Assert.DoesNotContain("preserve this exactly", directive, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("follow this exactly", directive, StringComparison.OrdinalIgnoreCase);
        }

        private static string InvokeDeliveryVoiceDirective(CharacterProfile profile)
        {
            var method = typeof(DeliveryStage).GetMethod(
                "BuildPlayerVoiceDirective",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            return (string)method!.Invoke(null, new object[] { profile })!;
        }
    }
}
