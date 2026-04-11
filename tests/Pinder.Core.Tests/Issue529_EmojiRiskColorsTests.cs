using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Pinder.Core.Tests
{
    [Trait("Category", "SessionRunner")]
    public class Issue529_EmojiRiskColorsTests
    {
        private static string InvokeRiskLabel(int need)
        {
            var methods = typeof(Program).GetMethods(BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
            var method = methods.FirstOrDefault(m => m.Name.Contains("RiskLabel") || m.Name.Contains("<Main>$>g__RiskLabel"));
            
            if (method == null)
            {
                throw new InvalidOperationException("Could not find RiskLabel method in Program class");
            }
            
            return (string)method.Invoke(null, new object[] { need })!;
        }

        // What: 🟢 emoji is shown for Safe risk tier
        // Mutation: would catch if RiskLabel returned "[Safe]" or omitted the 🟢 emoji
        [Fact]
        public void RiskLabel_Need4_ReturnsGreenSafe()
        {
            var result = InvokeRiskLabel(4);
            Assert.Equal("🟢 Safe", result);
        }

        // What: 🟡 emoji is shown for Medium risk tier
        // Mutation: would catch if RiskLabel returned "[Medium]" or omitted the 🟡 emoji
        [Fact]
        public void RiskLabel_Need9_ReturnsYellowMedium()
        {
            var result = InvokeRiskLabel(9);
            Assert.Equal("🟡 Medium", result);
        }

        // What: 🟠 emoji is shown for Hard risk tier
        // Mutation: would catch if RiskLabel returned "[Hard]" or omitted the 🟠 emoji
        [Fact]
        public void RiskLabel_Need15_ReturnsOrangeHard()
        {
            var result = InvokeRiskLabel(15);
            Assert.Equal("🟠 Hard", result);
        }

        // What: 🔴 emoji is shown for Bold risk tier
        // Mutation: would catch if RiskLabel returned "[Bold]" or omitted the 🔴 emoji
        [Fact]
        public void RiskLabel_Need17_ReturnsRedBold()
        {
            var result = InvokeRiskLabel(17);
            Assert.Equal("🔴 Bold", result);
        }

        // What: Edge case where need drops below 0 due to high stat mod / low DC
        // Mutation: would catch if RiskLabel threw an exception or didn't map <= 5 to Safe
        [Fact]
        public void RiskLabel_NegativeNeed_ReturnsGreenSafe()
        {
            var result = InvokeRiskLabel(-2);
            Assert.Equal("🟢 Safe", result);
        }

        // What: Edge case where need is extremely large
        // Mutation: would catch if RiskLabel didn't handle large numbers and failed to fallback to Reckless
        [Fact]
        public void RiskLabel_LargeNeed_ReturnsRedBold()
        {
            var result = InvokeRiskLabel(99);
            Assert.Equal("☠️ Reckless", result);
        }
        
        // What: Tests that the exact format includes the word text, ensuring accessibility fallback
        // Mutation: would catch if RiskLabel just returned "🟢" without the text "Safe"
        [Fact]
        public void RiskLabel_ContainsTextForAccessibility()
        {
            var resultSafe = InvokeRiskLabel(5);
            var resultMedium = InvokeRiskLabel(10);
            var resultHard = InvokeRiskLabel(15);
            var resultBold = InvokeRiskLabel(16);
            
            Assert.Contains("Safe", resultSafe);
            Assert.Contains("Medium", resultMedium);
            Assert.Contains("Hard", resultHard);
            Assert.Contains("Bold", resultBold);
        }

        // What: Program.cs uses the RiskLabel helper method instead of the inline ternary operator
        // Mutation: would catch if a developer hardcoded the old ternary string assignment back into Program.cs
        [Fact]
        public void ProgramCs_UsesRiskLabelHelper_InsteadOfInlineTernary()
        {
            string programPath = FindProgramCs();
            string content = File.ReadAllText(programPath);

            // The old hardcoded string should NOT appear
            Assert.DoesNotContain("need <= 5 ? \"[Safe]\" : need <= 10 ? \"[Medium]\" : need <= 15 ? \"[Hard]\" : \"[Bold]\"", content);

            // It should be calling the helper method for riskColor
            Assert.Contains("string riskColor = RiskLabel(need);", content);
        }

        private static string FindProgramCs()
        {
            // Walk up from test output dir to find session-runner/Program.cs
            string dir = AppContext.BaseDirectory;
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
