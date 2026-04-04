using System;
using System.IO;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Spec-driven tests for SessionFileCounter (issue #418).
    /// Verifies acceptance criteria from docs/specs/issue-418-spec.md.
    /// </summary>
    public class SessionFileCounterSpecTests : IDisposable
    {
        private readonly string _tempDir;

        public SessionFileCounterSpecTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
            // Clean env var in case a test failed before finally
            Environment.SetEnvironmentVariable(SessionFileCounter.EnvVarName, null);
        }

        #region AC1: Counter returns correct next number when session files exist

        // Mutation: would catch if implementation returns count instead of max+1
        [Fact]
        public void AC1_SequentialFiles_ReturnsMaxPlusOne()
        {
            File.WriteAllText(Path.Combine(_tempDir, "session-001-gerald-vs-zyx.md"), "");
            File.WriteAllText(Path.Combine(_tempDir, "session-002-brick-vs-velvet.md"), "");
            File.WriteAllText(Path.Combine(_tempDir, "session-003-sable-vs-zyx.md"), "");

            Assert.Equal(4, SessionFileCounter.GetNextSessionNumber(_tempDir));
        }

        // Mutation: would catch if implementation fills gaps instead of returning max+1
        [Fact]
        public void AC1_GapsInSequence_ReturnsMaxPlusOneNotFillGap()
        {
            File.WriteAllText(Path.Combine(_tempDir, "session-001-sable-vs-brick.md"), "");
            File.WriteAllText(Path.Combine(_tempDir, "session-005-sable-vs-brick.md"), "");

            Assert.Equal(6, SessionFileCounter.GetNextSessionNumber(_tempDir));
        }

        // Mutation: would catch if implementation returns 8 instead of 9 (off-by-one)
        [Fact]
        public void AC1_EightFiles_Returns9()
        {
            for (int i = 1; i <= 8; i++)
            {
                File.WriteAllText(Path.Combine(_tempDir, $"session-{i:D3}-player-vs-opponent.md"), "");
            }

            Assert.Equal(9, SessionFileCounter.GetNextSessionNumber(_tempDir));
        }

        #endregion

        #region AC2: Path resolution matches between write and read

        // Mutation: would catch if write path and counter path resolve to different locations
        [Fact]
        public void AC2_WriteAndReadBackFlow_NextNumberIncrements()
        {
            // Write a file using the same pattern as WritePlaytestLog
            File.WriteAllText(Path.Combine(_tempDir, "session-001-gerald-vs-zyx.md"), "content");
            File.WriteAllText(Path.Combine(_tempDir, "session-002-brick-vs-velvet.md"), "content");

            int nextNum = SessionFileCounter.GetNextSessionNumber(_tempDir);
            Assert.Equal(3, nextNum);

            // Simulate WritePlaytestLog writing a new file
            string slug = $"session-{nextNum:D3}-sable-vs-brick.md";
            File.WriteAllText(Path.Combine(_tempDir, slug), "new content");

            // Counter must see the new file
            Assert.Equal(4, SessionFileCounter.GetNextSessionNumber(_tempDir));
        }

        // Mutation: would catch if trailing slash causes path mismatch
        [Fact]
        public void AC2_TrailingSlash_StillFindsFiles()
        {
            File.WriteAllText(Path.Combine(_tempDir, "session-003-a-vs-b.md"), "");

            string dirWithSlash = _tempDir + Path.DirectorySeparatorChar;
            Assert.Equal(4, SessionFileCounter.GetNextSessionNumber(dirWithSlash));
        }

        // Mutation: would catch if .. segments break path resolution
        [Fact]
        public void AC2_DotDotSegments_StillFindsFiles()
        {
            File.WriteAllText(Path.Combine(_tempDir, "session-007-a-vs-b.md"), "");

            string subDir = Path.Combine(_tempDir, "sub");
            Directory.CreateDirectory(subDir);
            string pathWithDots = Path.Combine(subDir, "..");

            Assert.Equal(8, SessionFileCounter.GetNextSessionNumber(pathWithDots));
        }

        #endregion

        #region AC3: Character names with digits/hyphens parse correctly

        // Mutation: would catch if digits in character name are mistakenly parsed as session number
        [Fact]
        public void AC3_DigitsInCharacterName_ParsesSessionNumberCorrectly()
        {
            File.WriteAllText(Path.Combine(_tempDir, "session-008-gerald42-vs-zyx.md"), "");

            Assert.Equal(9, SessionFileCounter.GetNextSessionNumber(_tempDir));
        }

        // Mutation: would catch if Split('-') takes wrong index for hyphenated names
        [Fact]
        public void AC3_HyphenatedMultiWordNames_ParsesSessionNumberCorrectly()
        {
            File.WriteAllText(Path.Combine(_tempDir, "session-010-mary-jane-vs-peter-parker.md"), "");

            Assert.Equal(11, SessionFileCounter.GetNextSessionNumber(_tempDir));
        }

        // Mutation: would catch if parser confuses character digits with session number
        [Fact]
        public void AC3_MixedDigitNamesAndGaps_ReturnsMaxPlusOne()
        {
            File.WriteAllText(Path.Combine(_tempDir, "session-002-abc123-vs-def456.md"), "");
            File.WriteAllText(Path.Combine(_tempDir, "session-010-gerald42-vs-zyx.md"), "");

            Assert.Equal(11, SessionFileCounter.GetNextSessionNumber(_tempDir));
        }

        #endregion

        #region Edge cases from spec

        // Mutation: would catch if empty directory returns 0 instead of 1
        [Fact]
        public void Edge_EmptyDirectory_Returns1()
        {
            Assert.Equal(1, SessionFileCounter.GetNextSessionNumber(_tempDir));
        }

        // Mutation: would catch if non-md files are counted
        [Fact]
        public void Edge_OnlyNonMdFiles_Returns1()
        {
            File.WriteAllText(Path.Combine(_tempDir, "notes.md"), "");
            File.WriteAllText(Path.Combine(_tempDir, "readme.txt"), "");

            Assert.Equal(1, SessionFileCounter.GetNextSessionNumber(_tempDir));
        }

        // Mutation: would catch if .md files not matching session-* pattern are counted
        [Fact]
        public void Edge_NonSessionMdFiles_Returns1()
        {
            File.WriteAllText(Path.Combine(_tempDir, "notes.md"), "");
            File.WriteAllText(Path.Combine(_tempDir, "summary-001.md"), "");

            Assert.Equal(1, SessionFileCounter.GetNextSessionNumber(_tempDir));
        }

        // Mutation: would catch if large numbers overflow or are capped
        [Fact]
        public void Edge_SingleFile999_Returns1000()
        {
            File.WriteAllText(Path.Combine(_tempDir, "session-999-a-vs-b.md"), "");

            Assert.Equal(1000, SessionFileCounter.GetNextSessionNumber(_tempDir));
        }

        // Mutation: would catch if very large session numbers are truncated
        [Fact]
        public void Edge_VeryLargeNumber99999_Returns100000()
        {
            File.WriteAllText(Path.Combine(_tempDir, "session-99999-a-vs-b.md"), "");

            Assert.Equal(100000, SessionFileCounter.GetNextSessionNumber(_tempDir));
        }

        // Mutation: would catch if non-numeric parts crash instead of being skipped
        [Fact]
        public void Edge_NonNumericSessionPart_Skipped()
        {
            File.WriteAllText(Path.Combine(_tempDir, "session-abc-a-vs-b.md"), "");

            Assert.Equal(1, SessionFileCounter.GetNextSessionNumber(_tempDir));
        }

        // Mutation: would catch if non-numeric file is included in max calculation
        [Fact]
        public void Edge_NonNumericMixedWithNumeric_OnlyNumericCounted()
        {
            File.WriteAllText(Path.Combine(_tempDir, "session-abc-a-vs-b.md"), "");
            File.WriteAllText(Path.Combine(_tempDir, "session-003-a-vs-b.md"), "");

            Assert.Equal(4, SessionFileCounter.GetNextSessionNumber(_tempDir));
        }

        // Mutation: would catch if session-0 is treated as invalid
        [Fact]
        public void Edge_SessionZero_Returns1()
        {
            File.WriteAllText(Path.Combine(_tempDir, "session-0-a-vs-b.md"), "");

            // 0 + 1 = 1
            Assert.Equal(1, SessionFileCounter.GetNextSessionNumber(_tempDir));
        }

        #endregion

        #region ResolvePlaytestDirectory — 3-tier resolution

        // Mutation: would catch if env var is ignored or not prioritized
        [Fact]
        public void Resolve_EnvVarOverride_TakesPriority()
        {
            var envDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(envDir);
            try
            {
                Environment.SetEnvironmentVariable(SessionFileCounter.EnvVarName, envDir);

                string? resolved = SessionFileCounter.ResolvePlaytestDirectory("/nonexistent");

                Assert.Equal(Path.GetFullPath(envDir), resolved);
            }
            finally
            {
                Environment.SetEnvironmentVariable(SessionFileCounter.EnvVarName, null);
                Directory.Delete(envDir, true);
            }
        }

        // Mutation: would catch if non-existent env var path is returned instead of falling through
        [Fact]
        public void Resolve_EnvVarNonExistentPath_FallsThrough()
        {
            var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            var playtests = Path.Combine(root, "design", "playtests");
            var nested = Path.Combine(root, "a", "b");
            Directory.CreateDirectory(playtests);
            Directory.CreateDirectory(nested);
            try
            {
                Environment.SetEnvironmentVariable(SessionFileCounter.EnvVarName, "/nonexistent/path/that/doesnt/exist");

                string? resolved = SessionFileCounter.ResolvePlaytestDirectory(nested);

                // Should fall through to walk-up and find design/playtests
                Assert.Equal(Path.GetFullPath(playtests), resolved);
            }
            finally
            {
                Environment.SetEnvironmentVariable(SessionFileCounter.EnvVarName, null);
                Directory.Delete(root, true);
            }
        }

        // Mutation: would catch if walk-up doesn't check parent directories
        [Fact]
        public void Resolve_WalksUpFromNestedDirectory()
        {
            var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            var playtests = Path.Combine(root, "design", "playtests");
            var nested = Path.Combine(root, "a", "b", "c");
            Directory.CreateDirectory(playtests);
            Directory.CreateDirectory(nested);
            try
            {
                Environment.SetEnvironmentVariable(SessionFileCounter.EnvVarName, null);

                string? resolved = SessionFileCounter.ResolvePlaytestDirectory(nested);

                Assert.Equal(Path.GetFullPath(playtests), resolved);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        // Mutation: would catch if method returns non-null for truly non-existent paths
        [Fact]
        public void Resolve_NothingFound_ReturnsNullOrFallback()
        {
            var isolated = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(isolated);
            try
            {
                Environment.SetEnvironmentVariable(SessionFileCounter.EnvVarName, null);

                string? resolved = SessionFileCounter.ResolvePlaytestDirectory(isolated);

                // May return hardcoded fallback if it exists on this system, or null
                Assert.True(resolved == null || Directory.Exists(resolved));
            }
            finally
            {
                Directory.Delete(isolated, true);
            }
        }

        // Mutation: would catch if empty env var string is treated as a valid path
        [Fact]
        public void Resolve_EmptyEnvVar_FallsThrough()
        {
            var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            var playtests = Path.Combine(root, "design", "playtests");
            Directory.CreateDirectory(playtests);
            try
            {
                Environment.SetEnvironmentVariable(SessionFileCounter.EnvVarName, "");

                string? resolved = SessionFileCounter.ResolvePlaytestDirectory(root);

                Assert.Equal(Path.GetFullPath(playtests), resolved);
            }
            finally
            {
                Environment.SetEnvironmentVariable(SessionFileCounter.EnvVarName, null);
                Directory.Delete(root, true);
            }
        }

        #endregion

        #region Integration: ResolvePlaytestDirectory + GetNextSessionNumber

        // Mutation: would catch if resolved path doesn't work with GetNextSessionNumber
        [Fact]
        public void Integration_ResolvedPath_WorksWithGetNextSessionNumber()
        {
            var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            var playtests = Path.Combine(root, "design", "playtests");
            Directory.CreateDirectory(playtests);
            File.WriteAllText(Path.Combine(playtests, "session-005-sable-vs-brick.md"), "");
            try
            {
                Environment.SetEnvironmentVariable(SessionFileCounter.EnvVarName, null);

                string? resolved = SessionFileCounter.ResolvePlaytestDirectory(root);
                Assert.NotNull(resolved);

                int nextNum = SessionFileCounter.GetNextSessionNumber(resolved!);
                Assert.Equal(6, nextNum);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        #endregion
    }
}
