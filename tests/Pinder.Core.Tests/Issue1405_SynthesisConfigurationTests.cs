using System;
using System.IO;
using Pinder.Core.TestCommon;
using Xunit;

namespace Pinder.Core.Tests
{
    public sealed class Issue1405_SynthesisConfigurationTests
    {
        [Theory]
        [InlineData("LlmTherapistDiagnosisGenerator.cs")]
        [InlineData("LlmPersonalityConsolidator.cs")]
        [InlineData("LlmBioGenerator.cs")]
        [InlineData("LlmBackstoryConsolidator.cs")]
        public void SynthesisGeneratorsUseRequiredCatalogTemperatureWithoutAnonymousFallback(string fileName)
        {
            string synthesisDirectory = TestRepoLocator.FindRepoSubdir(
                Path.Combine("src", "Pinder.SessionSetup", "Synthesis"));
            string source = File.ReadAllText(Path.Combine(synthesisDirectory, fileName));

            Assert.Contains("entry.Temperature!.Value", source);
            Assert.DoesNotContain("entry.Temperature ??", source);
        }

        [Fact]
        public void SessionRunnerCapabilityRoutes_CliOverridesEnvironment_AndBuildFinalAnthropicOptions()
        {
            string[] args =
            {
                "--model-max-output-tokens", "8192",
                "--setup-model-max-output-tokens", "4096",
            };
            var routes = Program.ResolveModelMaxOutputTokens(
                args,
                "claude-sonnet-4.6",
                "claude-haiku-4.5",
                gameEnvironmentValue: "2048",
                setupEnvironmentValue: "1024");

            Assert.Equal(8192, routes.GameModel);
            Assert.Equal(4096, routes.SetupModel);

            var gameOptions = Program.BuildPiProviderTransportOptions(
                "claude-sonnet-4.6",
                "test-key",
                routes.GameModel,
                out string gameProvider,
                out string gameModel);
            var setupOptions = Program.BuildPiProviderTransportOptions(
                "claude-haiku-4.5",
                "test-key",
                routes.SetupModel,
                out string setupProvider,
                out string setupModel);

            AssertAnthropicOptions(gameOptions, gameProvider, gameModel, "claude-sonnet-4.6", 8192);
            AssertAnthropicOptions(setupOptions, setupProvider, setupModel, "claude-haiku-4.5", 4096);
        }

        [Fact]
        public void SessionRunnerCapabilityRoutes_UseRouteSpecificEnvironmentValues()
        {
            var routes = Program.ResolveModelMaxOutputTokens(
                Array.Empty<string>(),
                "claude-sonnet-4.6",
                "claude-haiku-4.5",
                gameEnvironmentValue: "2048",
                setupEnvironmentValue: "1024");

            Assert.Equal(2048, routes.GameModel);
            Assert.Equal(1024, routes.SetupModel);
        }

        [Theory]
        [InlineData("0")]
        [InlineData("not-a-number")]
        public void SessionRunnerRejectsInvalidModelCapability(string value)
        {
            Assert.Throws<ArgumentException>(() =>
                Program.ParseModelMaxOutputTokens(Array.Empty<string>(), value));
        }

        [Fact]
        public void SessionRunnerCapabilityRoutes_SameModelInheritsGameMaximum()
        {
            var routes = Program.ResolveModelMaxOutputTokens(
                new[] { "--model-max-output-tokens", "4096" },
                "claude-sonnet-4.6",
                "CLAUDE-SONNET-4.6",
                gameEnvironmentValue: null,
                setupEnvironmentValue: null);

            Assert.Equal(4096, routes.GameModel);
            Assert.Equal(4096, routes.SetupModel);
        }

        [Fact]
        public void SessionRunnerCapabilityRoutes_DifferentSetupModelDoesNotInheritGameMaximum()
        {
            var routes = Program.ResolveModelMaxOutputTokens(
                new[] { "--model-max-output-tokens", "4096" },
                "claude-sonnet-4.6",
                "claude-haiku-4.5",
                gameEnvironmentValue: null,
                setupEnvironmentValue: null);

            Assert.Equal(4096, routes.GameModel);
            Assert.Null(routes.SetupModel);
        }

        [Fact]
        public void SessionRunnerCapabilityRoutes_SetupEnvironmentOverridesSharedInheritance()
        {
            var routes = Program.ResolveModelMaxOutputTokens(
                Array.Empty<string>(),
                "claude-sonnet-4.6",
                "claude-sonnet-4.6",
                gameEnvironmentValue: "4096",
                setupEnvironmentValue: "2048");

            Assert.Equal(4096, routes.GameModel);
            Assert.Equal(2048, routes.SetupModel);
        }

        private static void AssertAnthropicOptions(
            Pinder.LlmAdapters.Pi.PiProviderTransportOptions options,
            string provider,
            string model,
            string expectedModel,
            int expectedMaximum)
        {
            Assert.Equal("anthropic", provider);
            Assert.Equal(expectedModel, model);
            Assert.Equal("anthropic", options.Provider);
            Assert.Equal(expectedModel, options.Model);
            Assert.Equal("test-key", options.ApiKey);
            Assert.NotNull(options.Fetch);
            Assert.NotNull(options.ModelCapabilities);
            Assert.Equal(expectedMaximum, options.ModelCapabilities!.MaxOutputTokens);
        }
    }
}
