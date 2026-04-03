using System;
using System.IO;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Tests the session file counter logic used in session-runner/Program.cs.
    /// Validates the glob pattern and number extraction from session filenames.
    /// </summary>
    public class SessionFileCounterTests
    {
        /// <summary>
        /// Extracts the next session number from a directory of session files.
        /// This mirrors the logic in session-runner/Program.cs WritePlaytestLog.
        /// </summary>
        static int GetNextSessionNumber(string dir)
        {
            int nextNum = 1;
            foreach (var f in Directory.GetFiles(dir, "session-*.md"))
            {
                var n = Path.GetFileNameWithoutExtension(f);
                var parts = n.Split('-');
                if (parts.Length >= 2 && int.TryParse(parts[1], out int num))
                    nextNum = Math.Max(nextNum, num + 1);
            }
            return nextNum;
        }

        [Fact]
        public void EmptyDirectory_Returns1()
        {
            var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(dir);
            try
            {
                Assert.Equal(1, GetNextSessionNumber(dir));
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
                Assert.Equal(6, GetNextSessionNumber(dir));
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
                Assert.Equal(6, GetNextSessionNumber(dir));
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
                // Character names with hyphens should not confuse the parser
                File.WriteAllText(Path.Combine(dir, "session-010-mary-jane-vs-peter-parker.md"), "");
                Assert.Equal(11, GetNextSessionNumber(dir));
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
                Assert.Equal(6, GetNextSessionNumber(dir));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
