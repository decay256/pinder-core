using System;
using System.IO;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Tests the SessionFileCounter.GetNextSessionNumber method used in session-runner.
    /// Validates the glob pattern and number extraction from session filenames.
    /// </summary>
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
    }
}
