using System.Collections.Generic;
using Pinder.Core.Text;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Issue #1041 (Tier C): Unit tests for <see cref="MarkdownSanitizer"/>.
    /// </summary>
    public class MarkdownSanitizerTests
    {
        // ── Bold ──────────────────────────────────────────────────────────────

        [Fact]
        public void Strip_Bold_DoubleStar_RemovesMarkers()
        {
            Assert.Equal("hello world", MarkdownSanitizer.Strip("**hello world**"));
        }

        [Fact]
        public void Strip_Bold_DoubleUnderscore_RemovesMarkers()
        {
            Assert.Equal("hello world", MarkdownSanitizer.Strip("__hello world__"));
        }

        // ── Italic ────────────────────────────────────────────────────────────

        [Fact]
        public void Strip_Italic_SingleStar_RemovesMarkers()
        {
            Assert.Equal("hello", MarkdownSanitizer.Strip("*hello*"));
        }

        [Fact]
        public void Strip_Italic_SingleUnderscore_RemovesMarkers()
        {
            Assert.Equal("hello", MarkdownSanitizer.Strip("_hello_"));
        }

        // ── Bold-italic ───────────────────────────────────────────────────────

        [Fact]
        public void Strip_BoldItalic_TripleStar_RemovesMarkers()
        {
            Assert.Equal("hello", MarkdownSanitizer.Strip("***hello***"));
        }

        // ── Headings ─────────────────────────────────────────────────────────

        [Fact]
        public void Strip_H1_RemovesSigil()
        {
            Assert.Equal("Heading", MarkdownSanitizer.Strip("# Heading"));
        }

        [Fact]
        public void Strip_H3_RemovesSigil()
        {
            Assert.Equal("Heading", MarkdownSanitizer.Strip("### Heading"));
        }

        // ── Inline code ───────────────────────────────────────────────────────

        [Fact]
        public void Strip_InlineCode_RemovesBackticks()
        {
            Assert.Equal("hello code", MarkdownSanitizer.Strip("hello `code`"));
        }

        // ── Code fences ───────────────────────────────────────────────────────

        [Fact]
        public void Strip_CodeFence_KeepsBody()
        {
            Assert.Equal("body", MarkdownSanitizer.Strip("```\nbody\n```"));
        }

        [Fact]
        public void Strip_CodeFence_WithLanguage_KeepsBody()
        {
            Assert.Equal("int x = 0;", MarkdownSanitizer.Strip("```csharp\nint x = 0;\n```"));
        }

        // ── Links ─────────────────────────────────────────────────────────────

        [Fact]
        public void Strip_Link_KeepsLinkText()
        {
            Assert.Equal("click here", MarkdownSanitizer.Strip("[click here](https://example.com)"));
        }

        [Fact]
        public void Strip_Image_KeepsAltText()
        {
            Assert.Equal("alt text", MarkdownSanitizer.Strip("![alt text](https://example.com/img.png)"));
        }

        // ── Blockquote ────────────────────────────────────────────────────────

        [Fact]
        public void Strip_Blockquote_RemovesSigil()
        {
            Assert.Equal("quoted text", MarkdownSanitizer.Strip("> quoted text"));
        }

        // ── Horizontal rule ───────────────────────────────────────────────────

        [Fact]
        public void Strip_HorizontalRule_RemovesLine()
        {
            string input = "before\n---\nafter";
            string result = MarkdownSanitizer.Strip(input);
            Assert.DoesNotContain("---", result);
            Assert.Contains("before", result);
            Assert.Contains("after", result);
        }

        // ── Strikethrough ─────────────────────────────────────────────────────

        [Fact]
        public void Strip_Strikethrough_RemovesMarkers()
        {
            Assert.Equal("deleted text", MarkdownSanitizer.Strip("~~deleted text~~"));
        }

        // ── Bullet preservation ───────────────────────────────────────────────

        [Fact]
        public void Strip_BulletList_PreservesDashBullets()
        {
            string input = "- item one\n- item two";
            string result = MarkdownSanitizer.Strip(input);
            Assert.Contains("- item one", result);
            Assert.Contains("- item two", result);
        }

        [Fact]
        public void Strip_NumberedList_PreservesNumbers()
        {
            string input = "1. first\n2. second";
            string result = MarkdownSanitizer.Strip(input);
            Assert.Contains("1. first", result);
            Assert.Contains("2. second", result);
        }

        // ── Null / empty guards ───────────────────────────────────────────────

        [Fact]
        public void Strip_NullInput_ReturnsEmpty()
        {
            Assert.Equal(string.Empty, MarkdownSanitizer.Strip(null));
        }

        [Fact]
        public void Strip_EmptyString_ReturnsEmpty()
        {
            Assert.Equal(string.Empty, MarkdownSanitizer.Strip(string.Empty));
        }

        [Fact]
        public void Strip_PlainText_ReturnsUnchanged()
        {
            const string plain = "Just a normal sentence.";
            Assert.Equal(plain, MarkdownSanitizer.Strip(plain));
        }

        // ── WouldStrip ────────────────────────────────────────────────────────

        [Fact]
        public void WouldStrip_MarkdownInput_ReturnsTrue()
        {
            Assert.True(MarkdownSanitizer.WouldStrip("**bold**"));
        }

        [Fact]
        public void WouldStrip_PlainInput_ReturnsFalse()
        {
            Assert.False(MarkdownSanitizer.WouldStrip("plain text"));
        }

        // ── TextSanitizer integration ─────────────────────────────────────────

        [Fact]
        public void TextSanitizer_MarkdownLayer_StripsAndAddsDiff()
        {
            var diffs = new List<TextDiff>();
            string raw = "**bold** text";
            string result = TextSanitizer.Sanitize(raw, MarkdownSanitizer.LayerName, diffs);

            Assert.Equal("bold text", result);
            Assert.Single(diffs);
            Assert.Equal(MarkdownSanitizer.LayerName, diffs[0].LayerName);
            Assert.Equal(raw, diffs[0].Before);
        }

        [Fact]
        public void TextSanitizer_MarkdownLayer_NoChange_DoesNotAddDiff()
        {
            var diffs = new List<TextDiff>();
            string raw = "plain text";
            string result = TextSanitizer.Sanitize(raw, MarkdownSanitizer.LayerName, diffs);

            Assert.Equal(raw, result);
            Assert.Empty(diffs);
        }
    }
}
