using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Pinder.Core.Characters;
using Pinder.Core.Stats;
using Pinder.SessionSetup;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Round-trip determinism tests for <see cref="CharacterDefinitionWriter"/>
    /// (issue #815).
    /// </summary>
    [Trait("Category", "Characters")]
    public class CharacterDefinitionWriterTests
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

        public static IEnumerable<object[]> StarterFiles =>
            new[] { "brick", "gerald", "reuben", "sable", "velvet", "zyx" }
                .Select(s => new object[] { s });

        [Theory]
        [MemberData(nameof(StarterFiles))]
        public void Write_ParsedStarterFile_RoundTripsByteEqual(string slug)
        {
            string path = Path.Combine(RepoRoot, "data", "characters", $"{slug}.json");
            string original = File.ReadAllText(path);

            // Pin LF line endings on the on-disk reference; the writer
            // promises LF and starter files are committed with LF (verified
            // by the no-CRLF guard test below). If the working copy was
            // somehow checked out with CRLF (autocrlf=true on Windows), the
            // sanitisation here keeps the contract assertion meaningful.
            string originalLf = original.Replace("\r\n", "\n");

            var def = CharacterDefinitionLoader.ParseDefinition(originalLf);
            string written = CharacterDefinitionWriter.Write(def);

            Assert.Equal(originalLf, written);
        }

        [Theory]
        [MemberData(nameof(StarterFiles))]
        public void Write_OutputIsItselfStable(string slug)
        {
            // Idempotence: Write(Parse(Write(Parse(json)))) == Write(Parse(json)).
            string path = Path.Combine(RepoRoot, "data", "characters", $"{slug}.json");
            string original = File.ReadAllText(path).Replace("\r\n", "\n");

            var def1 = CharacterDefinitionLoader.ParseDefinition(original);
            string write1 = CharacterDefinitionWriter.Write(def1);
            var def2 = CharacterDefinitionLoader.ParseDefinition(write1);
            string write2 = CharacterDefinitionWriter.Write(def2);

            Assert.Equal(write1, write2);
        }

        [Theory]
        [MemberData(nameof(StarterFiles))]
        public void Write_OutputEndsWithSingleNewline(string slug)
        {
            string path = Path.Combine(RepoRoot, "data", "characters", $"{slug}.json");
            string original = File.ReadAllText(path).Replace("\r\n", "\n");

            var def = CharacterDefinitionLoader.ParseDefinition(original);
            string written = CharacterDefinitionWriter.Write(def);

            Assert.EndsWith("}\n", written);
            Assert.False(written.EndsWith("\n\n", StringComparison.Ordinal),
                "Writer must emit exactly one trailing newline.");
        }

        [Theory]
        [MemberData(nameof(StarterFiles))]
        public void Write_UsesTwoSpaceIndent(string slug)
        {
            string path = Path.Combine(RepoRoot, "data", "characters", $"{slug}.json");
            string original = File.ReadAllText(path).Replace("\r\n", "\n");

            var def = CharacterDefinitionLoader.ParseDefinition(original);
            string written = CharacterDefinitionWriter.Write(def);

            // First indented line must start with exactly 2 spaces.
            string[] lines = written.Split('\n');
            Assert.True(lines.Length > 1, "Writer must emit multi-line indented output.");
            Assert.StartsWith("  \"", lines[1]);
            Assert.False(lines[1].StartsWith("    \""),
                "Writer must use 2-space (not 4-space) indentation.");
        }

        [Theory]
        [MemberData(nameof(StarterFiles))]
        public void Write_OutputIsUtf8WithoutBom(string slug)
        {
            string path = Path.Combine(RepoRoot, "data", "characters", $"{slug}.json");
            var def = CharacterDefinitionLoader.ParseDefinition(
                File.ReadAllText(path).Replace("\r\n", "\n"));

            string written = CharacterDefinitionWriter.Write(def);
            byte[] bytes = Encoding.UTF8.GetBytes(written);

            // No BOM (EF BB BF) at start.
            Assert.False(
                bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                "Writer output must not contain a UTF-8 BOM.");
        }

        [Theory]
        [MemberData(nameof(StarterFiles))]
        public void StarterFile_OnDisk_HasNoCrlfLineEndings(string slug)
        {
            // Starter files are committed with LF (the .gitattributes /
            // schema spec require LF). If a contributor's editor inserted
            // CRLF, the round-trip test would still pass thanks to
            // .Replace, but the on-disk file would no longer match the
            // spec. Catch that here.
            string path = Path.Combine(RepoRoot, "data", "characters", $"{slug}.json");
            byte[] bytes = File.ReadAllBytes(path);

            // Look for any 0x0D 0x0A pair.
            for (int i = 0; i + 1 < bytes.Length; i++)
            {
                Assert.False(bytes[i] == 0x0D && bytes[i + 1] == 0x0A,
                    $"{slug}.json contains a CRLF line ending at byte offset {i}; starter files must be LF-only.");
            }
        }

        [Fact]
        public void Write_PropertyOrderMatchesSchema()
        {
            // Hard-pin the on-disk top-level property order. This locks the
            // contract that the writer respects schema-declaration order
            // independent of dictionary insertion ordering.
            string path = Path.Combine(RepoRoot, "data", "characters", "gerald.json");
            var def = CharacterDefinitionLoader.ParseDefinition(
                File.ReadAllText(path).Replace("\r\n", "\n"));

            string written = CharacterDefinitionWriter.Write(def);

            int posSchema   = written.IndexOf("\"schema_version\"", StringComparison.Ordinal);
            int posId       = written.IndexOf("\"character_id\"", StringComparison.Ordinal);
            int posName     = written.IndexOf("\"name\"", StringComparison.Ordinal);
            int posGender   = written.IndexOf("\"gender_identity\"", StringComparison.Ordinal);
            int posBio      = written.IndexOf("\"bio\"", StringComparison.Ordinal);
            int posLevel    = written.IndexOf("\"level\"", StringComparison.Ordinal);
            int posItems    = written.IndexOf("\"items\"", StringComparison.Ordinal);
            int posAnatomy  = written.IndexOf("\"anatomy\"", StringComparison.Ordinal);
            int posAlloc    = written.IndexOf("\"allocation\"", StringComparison.Ordinal);

            Assert.True(posSchema  < posId,      "schema_version before character_id");
            Assert.True(posId      < posName,    "character_id before name");
            Assert.True(posName    < posGender,  "name before gender_identity");
            Assert.True(posGender  < posBio,     "gender_identity before bio");
            Assert.True(posBio     < posLevel,   "bio before level");
            Assert.True(posLevel   < posItems,   "level before items");
            Assert.True(posItems   < posAnatomy, "items before anatomy");
            Assert.True(posAnatomy < posAlloc,   "anatomy before allocation");
        }

        [Fact]
        public void Write_AllocationSubblockOrder_SpentThenPoolThenShadows()
        {
            string path = Path.Combine(RepoRoot, "data", "characters", "gerald.json");
            var def = CharacterDefinitionLoader.ParseDefinition(
                File.ReadAllText(path).Replace("\r\n", "\n"));

            string written = CharacterDefinitionWriter.Write(def);

            int posSpent  = written.IndexOf("\"spent\"", StringComparison.Ordinal);
            int posPool   = written.IndexOf("\"unspent_pool\"", StringComparison.Ordinal);
            int posShads  = written.IndexOf("\"shadows\"", StringComparison.Ordinal);

            Assert.True(posSpent < posPool, "spent before unspent_pool");
            Assert.True(posPool  < posShads, "unspent_pool before shadows");
        }

        [Fact]
        public void Write_StatOrderInsideSpentMatchesEnum()
        {
            string path = Path.Combine(RepoRoot, "data", "characters", "gerald.json");
            var def = CharacterDefinitionLoader.ParseDefinition(
                File.ReadAllText(path).Replace("\r\n", "\n"));

            string written = CharacterDefinitionWriter.Write(def);

            int posCharm   = written.IndexOf("\"charm\"", StringComparison.Ordinal);
            int posRizz    = written.IndexOf("\"rizz\"", StringComparison.Ordinal);
            int posHonesty = written.IndexOf("\"honesty\"", StringComparison.Ordinal);
            int posChaos   = written.IndexOf("\"chaos\"", StringComparison.Ordinal);
            int posWit     = written.IndexOf("\"wit\"", StringComparison.Ordinal);
            int posSelf    = written.IndexOf("\"self_awareness\"", StringComparison.Ordinal);

            Assert.True(posCharm < posRizz);
            Assert.True(posRizz < posHonesty);
            Assert.True(posHonesty < posChaos);
            Assert.True(posChaos < posWit);
            Assert.True(posWit < posSelf);
        }

        [Fact]
        public void Write_NullDef_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                CharacterDefinitionWriter.Write(null!));
        }
    }
}
