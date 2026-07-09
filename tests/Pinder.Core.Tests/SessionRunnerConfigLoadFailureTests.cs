using System;
using System.IO;
using Xunit;

namespace Pinder.Core.Tests
{
    [Trait("Category", "SessionRunner")]
    public sealed class SessionRunnerConfigLoadFailureTests
    {
        [Fact]
        public void LoadGameDefinitionOrExit_WithMalformedExistingFile_FailsSetup()
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, "{{invalid yaml");
                var result = new GameSetupResult();
                var diagnostics = new StringWriter();

                var gameDefinition = Program.LoadGameDefinitionOrExit(path, result, diagnostics);

                Assert.Null(gameDefinition);
                Assert.True(result.ShouldExit);
                Assert.Equal(1, result.ExitCode);
                string output = diagnostics.ToString();
                Assert.Contains("[ERROR] Failed to load game-definition.yaml", output);
                Assert.Contains(path, output);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void LoadStatDeliveryInstructionsOrExit_WithMalformedExistingFile_FailsSetup()
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, "delivery: [unterminated");
                var result = new GameSetupResult();
                var diagnostics = new StringWriter();

                var instructions = Program.LoadStatDeliveryInstructionsOrExit(path, result, diagnostics);

                Assert.Null(instructions);
                Assert.True(result.ShouldExit);
                Assert.Equal(1, result.ExitCode);
                string output = diagnostics.ToString();
                Assert.Contains("[ERROR] Failed to load delivery-instructions.yaml", output);
                Assert.Contains(path, output);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
