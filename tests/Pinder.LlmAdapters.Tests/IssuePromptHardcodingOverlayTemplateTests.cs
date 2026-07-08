using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    [Collection("PromptTraceSingleton")]
    public class IssuePromptHardcodingOverlayTemplateTests
    {
        private sealed class CapturingTransport : ILlmTransport
        {
            private readonly string _response;

            public CapturingTransport(string response)
            {
                _response = response;
            }

            public List<(string SystemPrompt, string UserMessage, string? Phase)> Calls { get; } = new List<(string, string, string?)>();

            public Task<string> SendAsync(
                string systemPrompt,
                string userMessage,
                double temperature = 0.9,
                int maxTokens = 1024,
                string? phase = null,
                CancellationToken ct = default)
            {
                Calls.Add((systemPrompt, userMessage, phase));
                return Task.FromResult(_response);
            }
        }

        [Fact]
        public void StatDeliveryInstructions_LoadFrom_LoadsVersionedOverlayPromptTemplates()
        {
            var instructions = StatDeliveryInstructions.LoadFrom(TemplateYaml);

            var template = instructions.GetOverlayPromptTemplate("trap_overlay");

            Assert.NotNull(template);
            Assert.Contains("SYS trap", template!.System);
            Assert.Contains("{trap_name}", template.User);
            Assert.Contains("{archetype_directive}", template.UserWithArchetype!);
        }

        [Fact]
        public async Task PinderLlmAdapter_SubstitutesOverlayPromptTemplates()
        {
            var transport = new CapturingTransport("rewritten");
            var adapter = CreateAdapter(transport);

            await adapter.ApplyHorninessOverlayAsync(
                "hello there",
                "make it warm",
                dateeContext: "datee bio",
                archetypeDirective: "ARCH voice");
            await adapter.ApplyTrapOverlayAsync(
                "trap message",
                "spiral the line",
                "Spiral",
                dateeContext: "trap datee",
                archetypeDirective: "TRAP ARCH");
            await adapter.ApplyFailureCorruptionAsync(
                "failure message",
                "botch it",
                StatType.Charm,
                FailureTier.Catastrophe,
                archetypeDirective: "FAIL ARCH");
            await adapter.ApplyShadowCorruptionAsync(
                "shadow message",
                "unhinge it",
                ShadowStatType.Madness,
                archetypeDirective: "SHADOW ARCH");

            Assert.Equal(4, transport.Calls.Count);

            Assert.Contains("SYS horn datee bio", transport.Calls[0].SystemPrompt);
            Assert.Contains("ARCH horn ARCH voice make it warm hello there", transport.Calls[0].UserMessage);

            Assert.Contains("SYS trap trap datee", transport.Calls[1].SystemPrompt);
            Assert.Contains("ARCH trap Spiral TRAP ARCH spiral the line trap message", transport.Calls[1].UserMessage);

            Assert.Contains("SYS failure Charm Catastrophe", transport.Calls[2].SystemPrompt);
            Assert.Contains("ARCH failure Charm Catastrophe FAIL ARCH botch it failure message", transport.Calls[2].UserMessage);

            Assert.Contains("SYS shadow Madness", transport.Calls[3].SystemPrompt);
            Assert.Contains("ARCH shadow Madness SHADOW ARCH unhinge it shadow message", transport.Calls[3].UserMessage);
        }

        [Fact]
        public void PinderLlmAdapter_SourceDoesNotInlineOverlayWrapperProse()
        {
            string source = File.ReadAllText(FindRepoFile("src", "Pinder.LlmAdapters", "PinderLlmAdapter.cs"));

            Assert.DoesNotContain("You are editing dialogue for Pinder", source);
            Assert.DoesNotContain("OVERLAY INSTRUCTION:", source);
            Assert.DoesNotContain("TRAP INSTRUCTION", source);
            Assert.DoesNotContain("FAILURE CORRUPTION INSTRUCTION", source);
            Assert.DoesNotContain("SHADOW CORRUPTION INSTRUCTION", source);
            Assert.DoesNotContain("absolute lunatic", source);
        }

        private static PinderLlmAdapter CreateAdapter(ILlmTransport transport)
        {
            return new PinderLlmAdapter(
                transport,
                new PinderLlmAdapterOptions
                {
                    GameDefinition = GameDefinition.PinderDefaults,
                    StatDeliveryInstructions = StatDeliveryInstructions.LoadFrom(TemplateYaml),
                });
        }

        private static string FindRepoFile(params string[] relativeParts)
        {
            string dir = Directory.GetCurrentDirectory();
            for (int i = 0; i < 12; i++)
            {
                string candidate = Path.Combine(Prepend(dir, relativeParts));
                if (File.Exists(candidate))
                    return candidate;

                string? parent = Directory.GetParent(dir)?.FullName;
                if (parent == null)
                    break;

                dir = parent;
            }

            throw new FileNotFoundException("Could not locate repo file.", Path.Combine(relativeParts));
        }

        private static string[] Prepend(string first, string[] rest)
        {
            var parts = new string[rest.Length + 1];
            parts[0] = first;
            Array.Copy(rest, 0, parts, 1, rest.Length);
            return parts;
        }

        private const string TemplateYaml = @"
delivery_instructions:
  charm:
    strong: ""stat instruction""
  horniness_overlay:
    fumble: ""horniness instruction""
shadow_corruption:
  madness:
    fumble: ""shadow instruction""
overlay_prompt_templates:
  version: 1
  horniness_overlay:
    system: ""SYS horn {datee_context}""
    user: ""USER horn {instruction} {message}""
    user_with_archetype: ""ARCH horn {archetype_directive} {instruction} {message}""
  trap_overlay:
    system: ""SYS trap {datee_context}""
    user: ""USER trap {trap_name} {instruction} {message}""
    user_with_archetype: ""ARCH trap {trap_name} {archetype_directive} {instruction} {message}""
  failure_corruption:
    system: ""SYS failure {stat} {tier}""
    user: ""USER failure {stat} {tier} {instruction} {message}""
    user_with_archetype: ""ARCH failure {stat} {tier} {archetype_directive} {instruction} {message}""
  shadow_corruption:
    system: ""SYS shadow {shadow}""
    user: ""USER shadow {shadow} {instruction} {message}""
    user_with_archetype: ""ARCH shadow {shadow} {archetype_directive} {instruction} {message}""
";
    }
}
