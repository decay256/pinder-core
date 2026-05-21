using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Audit gate: no file in Pinder.Core/Rolls may contain a hand-rolled miss-margin ladder
    /// (i.e. a <c>missMargin &lt;= N</c> comparison) EXCEPT <see cref="Pinder.Core.Rolls.FailureTierLadder"/>.
    /// #901: single source of truth invariant.
    /// </summary>
    public class TierLadderAuditTest
    {
        private static readonly Regex LadderPattern =
            new Regex(@"miss(Margin|)\s*<=\s*\d", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex NumericComparisonPattern =
            new Regex(@"<=\s*(\d+)\b", RegexOptions.Compiled);

        private static bool IsBareNumericLadder(string content, out int lineCount)
        {
            var lines = content.Split(new[] { "\r\n", "\r", "\n" }, System.StringSplitOptions.None);
            var matchedNumbers = new System.Collections.Generic.HashSet<string>();
            int matchedLineCount = 0;

            foreach (var line in lines)
            {
                var match = NumericComparisonPattern.Match(line);
                if (match.Success)
                {
                    matchedLineCount++;
                    matchedNumbers.Add(match.Groups[1].Value);
                }
            }

            lineCount = matchedLineCount;
            return matchedLineCount >= 3 && matchedNumbers.Count >= 2;
        }

        [Fact]
        public void NoDuplicateMissMarginLadders_InRollsFolder_OutsideFailureTierLadder()
        {
            // Resolve the path to the Pinder.Core source. We walk up from the test assembly.
            var testAssemblyDir = Path.GetDirectoryName(typeof(TierLadderAuditTest).Assembly.Location)!;
            // Walk up to find the repo root (contains src/ directory).
            var dir = new DirectoryInfo(testAssemblyDir);
            string? srcDir = null;
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "src", "Pinder.Core", "Rolls");
                if (Directory.Exists(candidate))
                {
                    srcDir = candidate;
                    break;
                }
                dir = dir.Parent;
            }

            Assert.True(srcDir != null, "Could not locate src/Pinder.Core/Rolls from test assembly path");

            var violations = new System.Collections.Generic.List<string>();
            foreach (var file in Directory.EnumerateFiles(srcDir!, "*.cs", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(file) == "FailureTierLadder.cs")
                    continue; // the one allowed source

                var content = File.ReadAllText(file);
                if (LadderPattern.IsMatch(content))
                    violations.Add(Path.GetFileName(file));
            }

            Assert.True(violations.Count == 0,
                $"Duplicate miss-margin ladder detected in: {string.Join(", ", violations)}. " +
                "Use FailureTierLadder.FromMissMargin instead.");
        }

        [Fact]
        public void NoBareNumericLadders_InRollsFolder_OutsideFailureTierLadder()
        {
            var testAssemblyDir = Path.GetDirectoryName(typeof(TierLadderAuditTest).Assembly.Location)!;
            var dir = new DirectoryInfo(testAssemblyDir);
            string? srcDir = null;
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "src", "Pinder.Core", "Rolls");
                if (Directory.Exists(candidate))
                {
                    srcDir = candidate;
                    break;
                }
                dir = dir.Parent;
            }

            Assert.True(srcDir != null, "Could not locate src/Pinder.Core/Rolls from test assembly path");

            var violations = new System.Collections.Generic.List<string>();
            foreach (var file in Directory.EnumerateFiles(srcDir!, "*.cs", SearchOption.AllDirectories))
            {
                var fileName = Path.GetFileName(file);
                if (fileName == "FailureTierLadder.cs" || fileName == "RollResult.cs")
                    continue;

                var content = File.ReadAllText(file);
                if (IsBareNumericLadder(content, out int lineCount))
                {
                    violations.Add($"{fileName} ({lineCount} matches)");
                }
            }

            Assert.True(violations.Count == 0,
                $"Bare numeric ladder detected in: {string.Join(", ", violations)}. " +
                "Use FailureTierLadder.FromMissMargin instead.");
        }

        [Fact]
        public void NoDuplicateMissMarginLadders_InConversationFolder()
        {
            var testAssemblyDir = Path.GetDirectoryName(typeof(TierLadderAuditTest).Assembly.Location)!;
            var dir = new DirectoryInfo(testAssemblyDir);
            string? srcDir = null;
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "src", "Pinder.Core", "Conversation");
                if (Directory.Exists(candidate))
                {
                    srcDir = candidate;
                    break;
                }
                dir = dir.Parent;
            }

            Assert.True(srcDir != null, "Could not locate src/Pinder.Core/Conversation from test assembly path");

            var violations = new System.Collections.Generic.List<string>();
            foreach (var file in Directory.EnumerateFiles(srcDir!, "*.cs", SearchOption.AllDirectories))
            {
                var content = File.ReadAllText(file);
                if (LadderPattern.IsMatch(content))
                    violations.Add(Path.GetFileName(file));
            }

            Assert.True(violations.Count == 0,
                $"Duplicate miss-margin ladder detected in: {string.Join(", ", violations)}. " +
                "Use FailureTierLadder.FromMissMargin instead.");
        }

        [Fact]
        public void NoBareNumericLadders_InConversationFolder()
        {
            var testAssemblyDir = Path.GetDirectoryName(typeof(TierLadderAuditTest).Assembly.Location)!;
            var dir = new DirectoryInfo(testAssemblyDir);
            string? srcDir = null;
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "src", "Pinder.Core", "Conversation");
                if (Directory.Exists(candidate))
                {
                    srcDir = candidate;
                    break;
                }
                dir = dir.Parent;
            }

            Assert.True(srcDir != null, "Could not locate src/Pinder.Core/Conversation from test assembly path");

            var violations = new System.Collections.Generic.List<string>();
            foreach (var file in Directory.EnumerateFiles(srcDir!, "*.cs", SearchOption.AllDirectories))
            {
                var fileName = Path.GetFileName(file);
                if (fileName == "GameClock.cs" || fileName == "InterestMeter.cs" || fileName == "SessionXpRecorder.cs")
                    continue;

                var content = File.ReadAllText(file);
                if (IsBareNumericLadder(content, out int lineCount))
                {
                    violations.Add($"{fileName} ({lineCount} matches)");
                }
            }

            Assert.True(violations.Count == 0,
                $"Bare numeric ladder detected in: {string.Join(", ", violations)}. " +
                "Use FailureTierLadder.FromMissMargin instead.");
        }
    }
}
