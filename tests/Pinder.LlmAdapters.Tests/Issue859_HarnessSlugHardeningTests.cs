using System;
using Pinder.Tools.NarrativeHarness;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// Item 2 of decay256/pinder-web#859: HarnessCharacterLoader.Load must reject
    /// structurally-unsafe / malicious character slugs with ArgumentException
    /// BEFORE constructing any filesystem path.
    ///
    /// Reverse-verification note: these tests are guard-dependent. If the
    /// ValidateSlug guard is deleted from HarnessCharacterLoader.Load, these
    /// cases would no longer throw ArgumentException — instead they would throw
    /// FileNotFoundException (or attempt actual path traversal), so the asserts
    /// below would FAIL. That is the point: the tests prove the guard is present
    /// and effective.
    /// </summary>
    public class Issue859_HarnessSlugHardeningTests
    {
        [Theory]
        [InlineData("../../etc/passwd")] // classic traversal
        [InlineData("..")]               // bare parent-dir
        [InlineData("foo/bar")]          // forward-slash separator
        [InlineData("foo\\bar")]         // backslash separator
        [InlineData("/abs/path")]        // leading separator / absolute
        [InlineData("")]                 // empty
        [InlineData("  ")]               // whitespace-only
        [InlineData("a/../b")]           // embedded traversal
        public void Load_RejectsMaliciousOrInvalidSlugs_WithArgumentException(string slug)
        {
            Assert.Throws<ArgumentException>(() => HarnessCharacterLoader.Load(slug));
        }

        [Fact]
        public void Load_RejectsNullSlug_WithArgumentException()
        {
            Assert.Throws<ArgumentException>(() => HarnessCharacterLoader.Load(null!));
        }

        /// <summary>
        /// Positive control: a structurally-valid slug that simply doesn't exist
        /// on disk must NOT be rejected by the guard. It passes ValidateSlug and
        /// fails later with FileNotFoundException — proving the allowlist guard
        /// does not over-reject valid-shaped slugs.
        /// </summary>
        [Fact]
        public void Load_ValidShapedButMissingSlug_ThrowsFileNotFound_NotArgumentException()
        {
            var ex = Record.Exception(
                () => HarnessCharacterLoader.Load("definitely-not-a-real-character"));
            Assert.NotNull(ex);
            Assert.IsNotType<ArgumentException>(ex);
            Assert.IsType<System.IO.FileNotFoundException>(ex);
        }
    }
}
