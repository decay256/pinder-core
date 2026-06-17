using System;
using Pinder.Tools.NarrativeHarness;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// Issue #1179: prove the archetypes_enabled ON path is wired through the
    /// narrative-harness load entry point (<see cref="HarnessCharacterLoader.Load"/>).
    ///
    /// #1174 added the flag plumbing; this verifies the new optional
    /// archetypesEnabled parameter threads through to
    /// CharacterDefinitionLoader.Assemble so the harness can produce the same
    /// archetype-injected system prompt the session-runner does when the flag is
    /// ON — while the default (1-arg) call still suppresses archetypes (OFF).
    /// </summary>
    public class Issue1179_HarnessArchetypesOnPathTests
    {
        // The real archetype zyx resolves with the production catalog (verified
        // by Pinder.Core.Tests.Issue1179_ArchetypesEnabledOnPathTests).
        private const string ZyxArchetypeName = "The Wall of Text";

        [Fact]
        public void Load_Zyx_ArchetypesEnabled_InjectsArchetypeContent()
        {
            var loaded = HarnessCharacterLoader.Load("zyx", archetypesEnabled: true);

            Assert.Contains("ACTIVE ARCHETYPE", loaded.AssembledSystemPrompt, StringComparison.Ordinal);
            Assert.Contains(ZyxArchetypeName, loaded.AssembledSystemPrompt, StringComparison.Ordinal);
        }

        [Fact]
        public void Load_Zyx_Default_SuppressesArchetypeContent()
        {
            // Default (1-arg) overload keeps the OFF behaviour.
            var loaded = HarnessCharacterLoader.Load("zyx");

            Assert.DoesNotContain("ACTIVE ARCHETYPE", loaded.AssembledSystemPrompt, StringComparison.Ordinal);
            Assert.DoesNotContain(ZyxArchetypeName, loaded.AssembledSystemPrompt, StringComparison.Ordinal);
        }
    }
}
