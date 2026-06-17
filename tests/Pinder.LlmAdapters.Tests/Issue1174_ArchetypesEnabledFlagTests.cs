using System;
using System.Collections.Generic;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Prompts;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    [Collection("PromptTraceSingleton")]
    public class Issue1174_ArchetypesEnabledFlagTests
    {
        private const string BaseYaml = @"
name: ""TestGame""
game_master_prompt: |
  A test game master prompt.
  With writing rules text.
player_avatar_role_description: |
  Player role.
datee_role_description: |
  Datee role.
global_dc_bias: 0
horniness_time_modifiers:
  morning: 3
  afternoon: 0
  evening: 2
  overnight: 5
";

        [Fact]
        public void LoadFrom_ArchetypesEnabled_Absent_DefaultsToFalse()
        {
            var gd = GameDefinition.LoadFrom(BaseYaml);
            Assert.False(gd.ArchetypesEnabled);
        }

        [Fact]
        public void LoadFrom_ArchetypesEnabled_ExplicitTrue_LoadsCorrectly()
        {
            var yaml = BaseYaml + "archetypes_enabled: true\n";
            var gd = GameDefinition.LoadFrom(yaml);
            Assert.True(gd.ArchetypesEnabled);
        }

        [Fact]
        public void LoadFrom_ArchetypesEnabled_ExplicitFalse_LoadsCorrectly()
        {
            var yaml = BaseYaml + "archetypes_enabled: false\n";
            var gd = GameDefinition.LoadFrom(yaml);
            Assert.False(gd.ArchetypesEnabled);
        }

        [Fact]
        public void BuildSystemPrompt_ArchetypesDisabled_SuppressArchetypeContent()
        {
            // Set up a deterministic fragment collection with a resolved active archetype
            var activeArchetype = new ActiveArchetype(
                "The Peacock",
                "Loud, expensive flex.\n*Sample lines:* \"check my watch\" · \"weekend trip booked\"",
                4, 5);

            var fragments = new FragmentCollection(
                personalityFragments: new List<string> { "warm but guarded" },
                backstoryFragments:   new List<string> { "grew up by the sea" },
                textingStyleFragments: new List<string>(),
                rankedArchetypes:     new List<(string, int)> { ("The Peacock", 4) },
                timing: new TimingProfile(5, 1.0f, 0.0f, "neutral"),
                stats: new StatBlock(
                    new Dictionary<StatType, int> { { StatType.Charm, 3 } },
                    new Dictionary<ShadowStatType, int>()
                ),
                activeArchetype: activeArchetype);

            // Build system prompt with archetypesEnabled = false
            string prompt = PromptBuilder.BuildSystemPrompt(
                "Velvet", "she/her", "just here for the vibes",
                fragments, new TrapState(), characterIdSeed: "fixed-seed",
                archetypesEnabled: false);

            // Assert that there is ZERO archetype content
            Assert.DoesNotContain("The Peacock", prompt);
            Assert.DoesNotContain("Loud, expensive flex", prompt);
            Assert.DoesNotContain("ACTIVE ARCHETYPE", prompt);
            Assert.DoesNotContain("(none resolved)", prompt);
        }

        [Fact]
        public void BuildSystemPrompt_ArchetypesEnabled_EmitsArchetypeContent()
        {
            // Set up a deterministic fragment collection with a resolved active archetype
            var activeArchetype = new ActiveArchetype(
                "The Peacock",
                "Loud, expensive flex.\n*Sample lines:* \"check my watch\" · \"weekend trip booked\"",
                4, 5);

            var fragments = new FragmentCollection(
                personalityFragments: new List<string> { "warm but guarded" },
                backstoryFragments:   new List<string> { "grew up by the sea" },
                textingStyleFragments: new List<string>(),
                rankedArchetypes:     new List<(string, int)> { ("The Peacock", 4) },
                timing: new TimingProfile(5, 1.0f, 0.0f, "neutral"),
                stats: new StatBlock(
                    new Dictionary<StatType, int> { { StatType.Charm, 3 } },
                    new Dictionary<ShadowStatType, int>()
                ),
                activeArchetype: activeArchetype);

            // Build system prompt with archetypesEnabled = true
            string prompt = PromptBuilder.BuildSystemPrompt(
                "Velvet", "she/her", "just here for the vibes",
                fragments, new TrapState(), characterIdSeed: "fixed-seed",
                archetypesEnabled: true);

            // Assert that the archetype directive + char-card block ARE present
            Assert.Contains("ACTIVE ARCHETYPE", prompt);
            Assert.Contains("The Peacock (dominant)", prompt);
            Assert.Contains("Loud, expensive flex", prompt);
        }
    }
}
