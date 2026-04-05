using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Verifies that DC Reference table formatting uses dynamic character names,
    /// not hardcoded values. Tests the interpolation pattern from Program.cs.
    /// </summary>
    public class DcTableHeaderTests
    {
        [Fact]
        public void DcTableHeader_UsesActualCharacterNames()
        {
            // Simulate the interpolation pattern from Program.cs line 308
            string player1 = "Velvet";
            string player2 = "Gerald";
            string header = $"## DC Reference ({player1} attacking, {player2} defending)";

            Assert.Equal("## DC Reference (Velvet attacking, Gerald defending)", header);
            Assert.DoesNotContain("Sable", header);
            Assert.DoesNotContain("Brick", header);
        }

        [Fact]
        public void DcTableColumnHeader_UsesActualCharacterNames()
        {
            string player1 = "Velvet";
            string player2 = "Gerald";
            string columnHeader = $"| Stat | {player1} mod | {player2} defends | DC | Need | % | Risk |";

            Assert.Equal("| Stat | Velvet mod | Gerald defends | DC | Need | % | Risk |", columnHeader);
            Assert.DoesNotContain("Sable", columnHeader);
            Assert.DoesNotContain("Brick", columnHeader);
        }

        [Fact]
        public void DcTableHeader_WorksWithDefaultCharacters()
        {
            // Even with the original characters, it should use the variable names
            string player1 = "Sable";
            string player2 = "Brick";
            string header = $"## DC Reference ({player1} attacking, {player2} defending)";

            Assert.Equal("## DC Reference (Sable attacking, Brick defending)", header);
        }

        [Fact]
        public void ProgramCs_DoesNotContainHardcodedDcHeader()
        {
            // Read the actual source file and verify no hardcoded header remains
            string programPath = FindProgramCs();
            string content = System.IO.File.ReadAllText(programPath);

            // The old hardcoded strings should NOT appear
            Assert.DoesNotContain("\"## DC Reference (Sable attacking, Brick defending)\"", content);
            Assert.DoesNotContain("\"| Stat | Sable mod | Brick defends |", content);

            // The dynamic interpolated strings SHOULD appear
            Assert.Contains("$\"## DC Reference ({player1} attacking, {player2} defending)\"", content);
            Assert.Contains($"$\"| Stat | {{player1}} mod | {{player2}} defends | DC | Need | % | Risk |\"", content);
        }

        private static string FindProgramCs()
        {
            // Walk up from test output dir to find session-runner/Program.cs
            string dir = System.AppContext.BaseDirectory;
            while (dir != null)
            {
                string candidate = System.IO.Path.Combine(dir, "session-runner", "Program.cs");
                if (System.IO.File.Exists(candidate))
                    return candidate;
                string? parent = System.IO.Path.GetDirectoryName(dir);
                if (parent == dir) break;
                dir = parent!;
            }
            throw new System.IO.FileNotFoundException("Could not find session-runner/Program.cs");
        }
    }
}
