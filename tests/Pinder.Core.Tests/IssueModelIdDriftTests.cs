using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Pinder.Core.Tests
{
    [Trait("Category", "Core")]
    public class IssueModelIdDriftTests
    {
        [Fact]
        public void SessionRunner_ExposesSeparateModelKnobs()
        {
            string cli = File.ReadAllText(FindRepoFile("session-runner", "Program.Cli.cs"));
            string setup = File.ReadAllText(FindRepoFile("session-runner", "Program.Setup.cs"));
            string helpers = File.ReadAllText(FindRepoFile("session-runner", "Program.Setup.Helpers.cs"));

            Assert.Contains("--model", cli);
            Assert.Contains("--setup-model", cli);
            Assert.Contains("--player-agent-model", cli);

            Assert.Contains("ParseGameModelArg(args)", helpers);
            Assert.Contains("ParseSetupModelArg(args, result.ModelSpec)", helpers);
            Assert.Contains("ParsePlayerAgentModelArg(args)", setup);
        }

        [Fact]
        public void SessionRunner_SetupModel_DoesNotReusePlayerAgentEnvVar()
        {
            string helpers = File.ReadAllText(FindRepoFile("session-runner", "Program.Setup.Helpers.cs"));
            string setup = File.ReadAllText(FindRepoFile("session-runner", "Program.Setup.cs"));

            Assert.DoesNotContain("Environment.GetEnvironmentVariable(\"PLAYER_AGENT_MODEL\")", helpers);
            Assert.DoesNotContain("Environment.GetEnvironmentVariable(\"PLAYER_AGENT_MODEL\")", setup);
            Assert.Contains("SESSION_SETUP_MODEL", File.ReadAllText(FindRepoFile("session-runner", "Program.Cli.cs")));
        }

        private static string FindRepoFile(params string[] parts)
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
                if (File.Exists(candidate))
                    return candidate;

                dir = dir.Parent;
            }

            throw new FileNotFoundException("Could not locate repo file: " + Path.Combine(parts));
        }
    }
}
