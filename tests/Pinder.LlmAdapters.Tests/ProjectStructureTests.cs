using System;
using System.Linq;
using Pinder.LlmAdapters.Anthropic;
using Pinder.LlmAdapters.Pi;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public sealed class ProjectStructureTests
    {
        [Fact]
        public void AssemblyRetainsExpectedBoundaryDependencies()
        {
            var assembly = typeof(PiProviderTransportFactory).Assembly;

            Assert.Equal("Pinder.LlmAdapters", assembly.GetName().Name);
            Assert.Equal("Pinder.Core", typeof(Pinder.Core.Interfaces.ILlmAdapter).Assembly.GetName().Name);
            Assert.Contains(assembly.GetReferencedAssemblies(), reference => reference.Name == "Newtonsoft.Json");
            Assert.Contains(assembly.GetReferencedAssemblies(), reference => reference.Name == "Pi.AI");
            Assert.Contains(assembly.GetReferencedAssemblies(), reference => reference.Name == "Pi.Agent.Core");
        }

        [Fact]
        public void ProviderCompositionIsPiOwnedAndLegacyTransportsAreAbsent()
        {
            var assembly = typeof(PiProviderTransportFactory).Assembly;
            string[] removedTypes =
            {
                "Pinder.LlmAdapters.Anthropic.AnthropicClient",
                "Pinder.LlmAdapters.Anthropic.AnthropicTransport",
                "Pinder.LlmAdapters.Anthropic.AnthropicStreamingTransport",
                "Pinder.LlmAdapters.Anthropic.ConversationSession",
                "Pinder.LlmAdapters.OpenAi.OpenAiClient",
                "Pinder.LlmAdapters.OpenAi.OpenAiTransport",
                "Pinder.LlmAdapters.OpenAi.OpenAiStreamingTransport",
            };

            Assert.All(removedTypes, typeName => Assert.Null(assembly.GetType(typeName)));
            Assert.Equal("Pinder.LlmAdapters.Pi", typeof(PiProviderTransportFactory).Namespace);
            Assert.Equal("Pinder.LlmAdapters.Pi", typeof(PiLlmTransport).Namespace);
            Assert.Equal("Pinder.LlmAdapters.Anthropic", typeof(AnthropicModelIds).Namespace);
        }
    }
}
