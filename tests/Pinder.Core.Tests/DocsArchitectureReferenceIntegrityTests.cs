using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Pinder.Core.Tests
{
    [Trait("Category", "Core")]
    public class DocsArchitectureReferenceIntegrityTests
    {
        private static string RepoRoot
        {
            get
            {
                string? dir = AppContext.BaseDirectory;
                while (dir != null)
                {
                    if (Directory.Exists(Path.Combine(dir, "data")) &&
                        Directory.Exists(Path.Combine(dir, "src")))
                        return dir;
                    dir = Directory.GetParent(dir)?.FullName;
                }
                throw new InvalidOperationException("Cannot find repo root from " + AppContext.BaseDirectory);
            }
        }

        /// <summary>
        /// Explicit allowlist for intentionally-historical references in ARCHITECTURE.md.
        /// Each entry must have a comment/justification here.
        /// MUST START EMPTY.
        /// </summary>
        private static readonly string[] HistoricalAllowlist = Array.Empty<string>();

        [Fact]
        public void Verify_DocsArchitecture_References_Exist()
        {
            // Arrange
            string docPath = Path.Combine(RepoRoot, "docs", "ARCHITECTURE.md");
            Assert.True(File.Exists(docPath), $"ARCHITECTURE.md does not exist at {docPath}");

            string content = File.ReadAllText(docPath);

            // Extract tokens ending with .cs
            var matches = Regex.Matches(content, @"[A-Za-z0-9_./]+\.cs");
            var tokens = matches
                .Cast<Match>()
                .Select(m => m.Value)
                .Distinct()
                .Where(t => !HistoricalAllowlist.Contains(t, StringComparer.OrdinalIgnoreCase))
                .ToList();

            // Find all C# files under src/, session-runner/, and tests/
            var searchDirs = new[] { "src", "session-runner", "tests" };
            var allCsFiles = searchDirs
                .Select(sub => Path.Combine(RepoRoot, sub))
                .Where(Directory.Exists)
                .SelectMany(dir => Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
                .ToArray();

            var relativeCsFiles = allCsFiles
                .Select(f => Path.GetRelativePath(RepoRoot, f).Replace('\\', '/'))
                .ToArray();

            var missingFiles = new List<string>();

            // Act
            foreach (var token in tokens)
            {
                var normalizedToken = token.Replace('\\', '/');
                var lastSegment = normalizedToken.Split('/').Last();

                bool resolved = false;
                foreach (var relPath in relativeCsFiles)
                {
                    // 1. Path ends with that token
                    bool endsWithToken = relPath.EndsWith(normalizedToken, StringComparison.OrdinalIgnoreCase);
                    if (endsWithToken)
                    {
                        // Ensure it's a boundary match (either exact match, or prefixed by a slash)
                        if (relPath.Length == normalizedToken.Length || relPath[relPath.Length - normalizedToken.Length - 1] == '/')
                        {
                            resolved = true;
                            break;
                        }
                    }

                    // 2. Filename matches the last path segment
                    var fileName = relPath.Split('/').Last();
                    if (fileName.Equals(lastSegment, StringComparison.OrdinalIgnoreCase))
                    {
                        resolved = true;
                        break;
                    }
                }

                if (!resolved)
                {
                    missingFiles.Add(token);
                }
            }

            // Assert
            Assert.True(
                missingFiles.Count == 0,
                $"The following referenced .cs file(s) are missing from disk:\n{string.Join("\n", missingFiles)}"
            );
        }
    }
}
