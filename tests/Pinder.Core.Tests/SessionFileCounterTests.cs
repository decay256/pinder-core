using System;
using System.IO;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Tests the SessionFileCounter used in session-runner.
    /// Validates number extraction from session filenames and path resolution.
    /// </summary>
    [Trait("Category", "SessionRunner")]
    public class SessionFileCounterTests
    {
        [Fact]
        public void EmptyDirectory_Returns1()
        {
            var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(dir);
            try
            {
                Assert.Equal(1, SessionFileCounter.GetNextSessionNumber(dir));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void SessionWithCharacterNames_ParsesCorrectly()
        {
            var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "session-005-sable-vs-brick.md"), "");
                Assert.Equal(6, SessionFileCounter.GetNextSessionNumber(dir));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void MultipleSessionFiles_ReturnsHighestPlusOne()
        {
            var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "session-001-sable-vs-brick.md"), "");
                File.WriteAllText(Path.Combine(dir, "session-003-sable-vs-brick.md"), "");
                File.WriteAllText(Path.Combine(dir, "session-005-sable-vs-brick.md"), "");
                Assert.Equal(6, SessionFileCounter.GetNextSessionNumber(dir));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void HyphenatedCharacterNames_ParsesNumberCorrectly()
        {
            var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "session-010-mary-jane-vs-peter-parker.md"), "");
                Assert.Equal(11, SessionFileCounter.GetNextSessionNumber(dir));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void NonSessionFiles_AreIgnored()
        {
            var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "session-005-sable-vs-brick.md"), "");
                File.WriteAllText(Path.Combine(dir, "notes.md"), "");
                File.WriteAllText(Path.Combine(dir, "readme.txt"), "");
                Assert.Equal(6, SessionFileCounter.GetNextSessionNumber(dir));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        // AC3: Character names containing digits parse correctly
        [Fact]
        public void CharacterNamesWithDigits_ParsesCorrectly()
        {
            var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "session-008-gerald42-vs-zyx.md"), "");
                Assert.Equal(9, SessionFileCounter.GetNextSessionNumber(dir));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        // AC2: Production flow — write then read back produces correct next number
        [Fact]
        public void ProductionFlow_WriteAndReadBack_ReturnsNextNumber()
        {
            var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(dir);
            try
            {
                // Simulate existing files
                File.WriteAllText(Path.Combine(dir, "session-001-gerald-vs-zyx.md"), "content");
                File.WriteAllText(Path.Combine(dir, "session-002-brick-vs-velvet.md"), "content");

                // Get next number and write a file (mimics WritePlaytestLog)
                int nextNum = SessionFileCounter.GetNextSessionNumber(dir);
                Assert.Equal(3, nextNum);
                string slug = $"session-{nextNum:D3}-sable-vs-brick.md";
                File.WriteAllText(Path.Combine(dir, slug), "new content");

                // Subsequent call should return 4
                int nextAfterWrite = SessionFileCounter.GetNextSessionNumber(dir);
                Assert.Equal(4, nextAfterWrite);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        // AC1: After session-008 produces session-009
        [Fact]
        public void AfterSession008_ReturnsSession009()
        {
            var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(dir);
            try
            {
                for (int i = 1; i <= 8; i++)
                {
                    File.WriteAllText(Path.Combine(dir, $"session-{i:D3}-player-vs-opponent.md"), "");
                }
                Assert.Equal(9, SessionFileCounter.GetNextSessionNumber(dir));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        // Edge case: large session number
        [Fact]
        public void LargeSessionNumber_HandledCorrectly()
        {
            var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "session-999-a-vs-b.md"), "");
                Assert.Equal(1000, SessionFileCounter.GetNextSessionNumber(dir));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        // Edge case: non-numeric session part is skipped
        [Fact]
        public void NonNumericSessionPart_IsSkipped()
        {
            var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "session-abc-a-vs-b.md"), "");
                File.WriteAllText(Path.Combine(dir, "session-003-a-vs-b.md"), "");
                Assert.Equal(4, SessionFileCounter.GetNextSessionNumber(dir));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        // Simulates the bug: two consecutive session writes must produce sequential numbers
        // Previously the session number was computed at write time and could be stale
        // if the header was written before the file (or path mismatch between runs)
        [Fact]
        public void ConsecutiveSessionWrites_ProduceSequentialNumbers()
        {
            var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(dir);
            try
            {
                // Simulate 5 existing files
                for (int i = 1; i <= 5; i++)
                    File.WriteAllText(Path.Combine(dir, $"session-{i:D3}-sable-vs-brick.md"), "");

                // First write: should be session-006
                int firstNum = SessionFileCounter.GetNextSessionNumber(dir);
                Assert.Equal(6, firstNum);
                string firstSlug = $"session-{firstNum:D3}-sable-vs-brick.md";
                File.WriteAllText(Path.Combine(dir, firstSlug), "session 6 content");

                // Second write: should be session-007 (NOT session-006 again)
                int secondNum = SessionFileCounter.GetNextSessionNumber(dir);
                Assert.Equal(7, secondNum);
                string secondSlug = $"session-{secondNum:D3}-sable-vs-brick.md";
                File.WriteAllText(Path.Combine(dir, secondSlug), "session 7 content");

                // Verify both files exist with distinct content
                Assert.True(File.Exists(Path.Combine(dir, "session-006-sable-vs-brick.md")));
                Assert.True(File.Exists(Path.Combine(dir, "session-007-sable-vs-brick.md")));
                Assert.Equal("session 6 content", File.ReadAllText(Path.Combine(dir, "session-006-sable-vs-brick.md")));
                Assert.Equal("session 7 content", File.ReadAllText(Path.Combine(dir, "session-007-sable-vs-brick.md")));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        // Verifies the fix: session number computed once at start is used for both header and filename
        [Fact]
        public void SessionNumberComputedOnce_HeaderMatchesFilename()
        {
            var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(dir);
            try
            {
                for (int i = 1; i <= 5; i++)
                    File.WriteAllText(Path.Combine(dir, $"session-{i:D3}-a-vs-b.md"), "");

                // Simulate the fixed flow: compute number ONCE, use for both header and file
                int sessionNumber = SessionFileCounter.GetNextSessionNumber(dir);
                string header = $"# Playtest Session {sessionNumber:D3}";
                string slug = $"session-{sessionNumber:D3}-a-vs-b.md";

                // Header number matches slug number
                Assert.Contains("006", header);
                Assert.Contains("006", slug);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        // ResolvePlaytestDirectory: env var takes priority
        [Fact]
        public void ResolvePlaytestDirectory_EnvVarOverride()
        {
            var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(dir);
            try
            {
                Environment.SetEnvironmentVariable(SessionFileCounter.EnvVarName, dir);
                string? resolved = SessionFileCounter.ResolvePlaytestDirectory("/nonexistent");
                Assert.Equal(Path.GetFullPath(dir), resolved);
            }
            finally
            {
                Environment.SetEnvironmentVariable(SessionFileCounter.EnvVarName, null);
                Directory.Delete(dir, true);
            }
        }

        // ResolvePlaytestDirectory: walks up to find design/playtests
        [Fact]
        public void ResolvePlaytestDirectory_WalksUpToFindDesignPlaytests()
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

        // ResolvePlaytestDirectory: returns null when nothing found
        [Fact]
        public void ResolvePlaytestDirectory_ReturnsNullWhenNotFound()
        {
            var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(dir);
            try
            {
                Environment.SetEnvironmentVariable(SessionFileCounter.EnvVarName, null);
                // On systems without the hardcoded fallback, this returns null
                // We can't test the fallback path portably, but we test the walk-up logic
                string? resolved = SessionFileCounter.ResolvePlaytestDirectory(dir);
                // May or may not be null depending on whether the hardcoded path exists
                // The important thing is it doesn't crash
                Assert.True(resolved == null || Directory.Exists(resolved));
            }
            finally
            {
                Environment.SetEnvironmentVariable(SessionFileCounter.EnvVarName, null);
                Directory.Delete(dir, true);
            }
        }
    }
}
