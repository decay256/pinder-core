using System;
using Pinder.Core.Conversation;
using Pinder.Rules;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.Core.Tests
{
    public class AssemblyVersioningTests
    {
        [Fact]
        public void CoreAssembly_HasCorrectVersion()
        {
            var version = typeof(Pinder.Core.Conversation.TurnResult).Assembly.GetName().Version;
            Assert.Equal(Version.Parse("0.1.2.0"), version);
        }

        [Fact]
        public void RulesAssembly_HasCorrectVersion()
        {
            var version = typeof(Pinder.Rules.RuleBook).Assembly.GetName().Version;
            Assert.Equal(Version.Parse("0.1.2.0"), version);
        }

        [Fact]
        public void LlmAdaptersAssembly_HasCorrectVersion()
        {
            var version = typeof(Pinder.LlmAdapters.PinderLlmAdapter).Assembly.GetName().Version;
            Assert.Equal(Version.Parse("0.1.2.0"), version);
        }
    }
}
