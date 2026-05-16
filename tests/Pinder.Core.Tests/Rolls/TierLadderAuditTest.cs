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
    }
}
