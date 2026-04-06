using System.IO;
using Xunit;
using Pinder.SessionRunner;

namespace Pinder.Core.Tests
{
    public class Issue526_ComboExplanationTests
    {
        // What: Returns expected description for known combos (AC 3)
        // Mutation: Would catch if description string is altered or incorrect combo mapped
        [Theory]
        [InlineData("The Setup", "You played Wit last turn, then Charm this turn \u2014 the sequence earns +1 bonus interest.")]
        [InlineData("The Reveal", "You played Charm last turn, then Honesty this turn \u2014 the sequence earns +1 bonus interest.")]
        [InlineData("The Read", "You played SA last turn, then Honesty this turn \u2014 the sequence earns +1 bonus interest.")]
        [InlineData("The Pivot", "You played Honesty last turn, then Chaos this turn \u2014 the sequence earns +1 bonus interest.")]
        [InlineData("The Escalation", "You played Chaos last turn, then Rizz this turn \u2014 the sequence earns +1 bonus interest.")]
        [InlineData("The Disarm", "You played Wit last turn, then Honesty this turn \u2014 the sequence earns +1 bonus interest.")]
        [InlineData("The Recovery", "You failed a roll last turn, then played SA this turn \u2014 the sequence earns +2 bonus interest.")]
        [InlineData("The Triple", "You played 3 different stats in 3 consecutive turns \u2014 your next roll gains +1 bonus.")]
        public void GetComboSequenceDescription_KnownCombos_ReturnsExpectedString(string comboName, string expected)
        {
            var result = PlaytestFormatter.GetComboSequenceDescription(comboName);
            Assert.Equal(expected, result);
        }

        // What: Returns fallback string for unknown or null combo (Edge Cases)
        // Mutation: Would catch if method throws NullReferenceException or returns an empty string instead of fallback
        [Theory]
        [InlineData("NonExistentCombo")]
        [InlineData(null)]
        [InlineData("")]
        public void GetComboSequenceDescription_UnknownOrNullCombo_ReturnsFallbackString(string comboName)
        {
            var result = PlaytestFormatter.GetComboSequenceDescription(comboName);
            Assert.Equal("Unknown combo sequence.", result);
        }

        // What: Returns reward summary for combos
        // Mutation: Would catch if summary strings are altered
        [Theory]
        [InlineData("The Recovery", "+2 Interest if success")]
        [InlineData("The Triple", "+1 to ALL rolls next turn")]
        [InlineData("The Setup", "+1 Interest if success")]
        [InlineData("UnknownCombo", "+1 Interest if success")]
        [InlineData(null, "+1 Interest if success")]
        public void GetComboRewardSummary_ReturnsExpectedString(string comboName, string expected)
        {
            var result = PlaytestFormatter.GetComboRewardSummary(comboName);
            Assert.Equal(expected, result);
        }

        // What: Verifies Program.cs implements the required option list prefix (AC 1)
        // Mutation: Would catch if Program.cs omits the blockquote/italic formatting
        [Fact]
        public void ProgramCs_OptionList_UsesRequiredPrefix()
        {
            string programPath = FindProgramCs();
            string content = File.ReadAllText(programPath);

            // Verify it uses the blockquote and italics format for combo name and description
            Assert.Contains("> *{opt.ComboName}: {PlaytestFormatter.GetComboSequenceDescription(opt.ComboName)}*", content);
        }

        // What: Verifies Program.cs calls GetComboSequenceDescription (AC 2 & 3)
        // Mutation: Would catch if Program.cs hardcodes descriptions instead of using PlaytestFormatter
        [Fact]
        public void ProgramCs_CallsGetComboSequenceDescription()
        {
            string programPath = FindProgramCs();
            string content = File.ReadAllText(programPath);

            Assert.Contains("PlaytestFormatter.GetComboSequenceDescription", content);
            
            // Verify combo trigger is formatted with blockquote per AC 2
            Assert.Contains("> *{PlaytestFormatter.GetComboSequenceDescription(result.ComboTriggered)}*", content);
        }

        private static string FindProgramCs()
        {
            string dir = System.AppContext.BaseDirectory;
            while (dir != null)
            {
                string candidate = Path.Combine(dir, "session-runner", "Program.cs");
                if (File.Exists(candidate))
                    return candidate;
                string? parent = Path.GetDirectoryName(dir);
                if (parent == dir) break;
                dir = parent!;
            }
            throw new FileNotFoundException("Could not find session-runner/Program.cs");
        }
    }
}