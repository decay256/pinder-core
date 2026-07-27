using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.TestCommon;
using Pinder.Core.Text;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public sealed class EmotionalPromptCompilerParityTests
    {
        static EmotionalPromptCompilerParityTests()
        {
            PromptCatalogInitializer.Initialize();
        }

        [Fact]
        public void CompileScenario_MatchesRuntimePhaseCompilationAndRebasesWhitespaceTrimmedSpans()
        {
            string promptsRoot = CopyPromptsToTemp(FindPromptsRoot());
            try
            {
                string promptPath = Path.Combine(promptsRoot, "emotional-reactions.yaml");
                string yaml = File.ReadAllText(promptPath)
                    .Replace("\r\n", "\n", StringComparison.Ordinal);
                yaml = yaml.Replace(
                    "system_prompt: |-\n",
                    "system_prompt: |-\n\n",
                    StringComparison.Ordinal);
                yaml = yaml.Replace(
                    "user_template: |-\n",
                    "user_template: |-\n\n",
                    StringComparison.Ordinal);
                File.WriteAllText(promptPath, yaml);

                var catalog = PromptCatalog.LoadFromDirectory(promptsRoot);
                var compiler = new EmotionalPromptCompiler(catalog);
                DateeContext context = MakeContext();
                var direction = ValidDirection();

                CompiledEmotionalPrompts scenario = compiler.CompileScenario(context, direction);
                CompiledEmotionalDirectorPrompt director = compiler.CompileDirector(context);
                PromptTraceResult performance = compiler.CompilePerformance(context, direction);

                AssertTraceEqual(director.CompiledReactionInput, scenario.CompiledReactionInput);
                AssertTraceEqual(director.SystemPrompt, scenario.Director.SystemPrompt);
                AssertTraceEqual(director.UserPrompt, scenario.Director.UserPrompt);
                AssertTraceEqual(performance, scenario.PerformancePrompt);
                Assert.Equal(scenario.Director.SystemPrompt.Text.Trim(), scenario.Director.SystemPrompt.Text);
                Assert.Equal(scenario.Director.UserPrompt.Text.Trim(), scenario.Director.UserPrompt.Text);
                AssertValidSpans(scenario.Director.SystemPrompt);
                AssertValidSpans(scenario.Director.UserPrompt);
                Assert.Contains(
                    scenario.Director.UserPrompt.Spans,
                    span => span.Key == "emotional-reaction-compiled-wrapper");
            }
            finally
            {
                Directory.Delete(promptsRoot, recursive: true);
            }
        }

        private static void AssertTraceEqual(PromptTraceResult expected, PromptTraceResult actual)
        {
            Assert.Equal(expected.Text, actual.Text);
            Assert.Equal(
                expected.Spans.Select(SpanTuple),
                actual.Spans.Select(SpanTuple));
        }

        private static void AssertValidSpans(PromptTraceResult trace)
        {
            Assert.All(
                trace.Spans,
                span =>
                {
                    Assert.InRange(span.Start, 0, trace.Text.Length);
                    Assert.InRange(span.End, span.Start, trace.Text.Length);
                    Assert.True(span.End > span.Start);
                });
        }

        private static (int Start, int End, string? SourceFile, string? Key) SpanTuple(AnnotatedSpan span)
            => (span.Start, span.End, span.SourceFile, span.Key);

        private static EmotionalPrivateDirection ValidDirection()
            => new(
                "relieved but cautious",
                "moderate and steadily rising",
                "fear of being dismissed",
                "reads the message as specific warmth that is probably meant for them",
                "leans in with a careful question",
                "keeps the reply tentative but available",
                "turns warmer while still checking sincerity");

        private static DateeContext MakeContext()
            => new(
                dateePrompt: "datee prompt",
                conversationHistory: Array.Empty<(string Sender, string Text)>(),
                dateeLastMessage: string.Empty,
                activeTraps: Array.Empty<string>(),
                currentInterest: 18,
                playerDeliveredMessage: "I meant that more warmly than it sounded.",
                interestBefore: 13,
                interestAfter: 18,
                responseDelayMinutes: 0,
                playerName: "Player",
                dateeName: "Datee",
                currentTurn: 7,
                deliveryTier: FailureTier.Success,
                interestBeforeState: InterestState.Interested,
                interestAfterState: InterestState.VeryIntoIt,
                emotionalTurnEvent: new DateeEmotionalTurnEvent(
                    StatType.Honesty,
                    RollOutcomeIntensity.Strong,
                    Diagnosis()));

        private static Dictionary<string, string> Diagnosis()
            => new()
            {
                [TherapistDiagnosisContract.DerivedFeelingKey] = "Concrete detail makes emotional meaning feel safer.",
                [TherapistDiagnosisContract.DefenseReactionKey] = "Precision protects against being dismissed.",
                [TherapistDiagnosisContract.SafeConnectionKey] = "Safety permits warmer short replies.",
                [TherapistDiagnosisContract.HurtProtectionKey] = "Hurt prompts a test for honest repair.",
                [TherapistDiagnosisContract.RepairRequirementKey] = "Repair requires specific ownership.",
                [TherapistDiagnosisContract.CharmReactionKey] = "Charm can feel easy or evasive.",
                [TherapistDiagnosisContract.RizzReactionKey] = "Rizz can feel wanted or handled.",
                [TherapistDiagnosisContract.HonestyReactionKey] = "Honesty is read through concrete accountability.",
                [TherapistDiagnosisContract.ChaosReactionKey] = "Chaos can feel alive or unstable.",
                [TherapistDiagnosisContract.WitReactionKey] = "Wit can relax or deflect.",
                [TherapistDiagnosisContract.SelfAwarenessReactionKey] = "Self-awareness can feel accurate or rehearsed.",
            };

        private static string FindPromptsRoot()
        {
            string? directory = AppDomain.CurrentDomain.BaseDirectory;
            while (directory != null)
            {
                string candidate = Path.Combine(directory, "data", "prompts");
                if (Directory.Exists(candidate)) return candidate;
                directory = Path.GetDirectoryName(directory);
            }

            throw new DirectoryNotFoundException("Could not locate bundled data/prompts.");
        }

        private static string CopyPromptsToTemp(string source)
        {
            string destination = Path.Combine(
                Path.GetTempPath(),
                "emotional-prompt-compiler-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.EnumerateFiles(source, "*.yaml"))
            {
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            }

            return destination;
        }
    }
}
