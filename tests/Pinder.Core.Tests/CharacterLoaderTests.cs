using System;
using System.Collections.Generic;
using System.IO;
using Pinder.Core.Stats;
using Pinder.SessionRunner;
using Xunit;

namespace Pinder.Core.Tests
{
    public class CharacterLoaderTests
    {
        private const string SablePromptContent = @"# Sable — Assembled System Prompt

> **Inputs:** name=Sable · she/her · bio=""Scorpio. Living my best life. Send memes.""

---

```
You are playing the role of Sable, a sentient penis on the dating app Pinder.

LEVEL
- Level: 3 (Getting Somewhere) | Level bonus: +1

EFFECTIVE STATS
- Charm: +7
- Rizz: +7
- Honesty: +8
- Chaos: +4
- Wit: +1
- Self-Awareness: +4
```

---

## Assembly Notes

Some notes here.

**Shadow state (estimated after 3 levels of play):**
- Denial: ~3 (she's got the date without honesty a few times)
- Fixation: ~2 (same opener energy more than once)
";

        private const string ZyxPromptContent = @"# Zyx — Assembled System Prompt

> **Inputs:** name=Zyx · they/them

---

```
You are playing the role of Zyx, a sentient penis on the dating app Pinder.

LEVEL
- Level: 1 (Fresh Meat) | Level bonus: +0

EFFECTIVE STATS
- Charm: 0
- Rizz: -1
- Honesty: +12
- Chaos: +4
- Wit: +5
- Self-Awareness: +8
```

---

**Shadow state (Level 1, fresh):**
All shadows at 0. Zyx arrived clean.
";

        [Fact]
        public void Parse_ExtractsDisplayName()
        {
            var profile = CharacterLoader.Parse(SablePromptContent, "sable");
            Assert.Equal("Sable", profile.DisplayName);
        }

        [Fact]
        public void Parse_ExtractsLevel()
        {
            var profile = CharacterLoader.Parse(SablePromptContent, "sable");
            Assert.Equal(3, profile.Level);
        }

        [Fact]
        public void Parse_ExtractsEffectiveStats()
        {
            // Prompt values are stored as base stats in StatBlock.
            // GetEffective applies shadow penalty: base - shadow/3
            // Sable has Denial:3, Fixation:2 → Honesty penalty=1, Chaos penalty=0
            var profile = CharacterLoader.Parse(SablePromptContent, "sable");
            var stats = profile.Stats;
            // Base values match prompt file
            Assert.Equal(7, stats.GetBase(StatType.Charm));
            Assert.Equal(7, stats.GetBase(StatType.Rizz));
            Assert.Equal(8, stats.GetBase(StatType.Honesty));
            Assert.Equal(4, stats.GetBase(StatType.Chaos));
            Assert.Equal(1, stats.GetBase(StatType.Wit));
            Assert.Equal(4, stats.GetBase(StatType.SelfAwareness));
        }

        [Fact]
        public void Parse_ExtractsShadowValues()
        {
            var profile = CharacterLoader.Parse(SablePromptContent, "sable");
            var stats = profile.Stats;
            Assert.Equal(3, stats.GetShadow(ShadowStatType.Denial));
            Assert.Equal(2, stats.GetShadow(ShadowStatType.Fixation));
            Assert.Equal(0, stats.GetShadow(ShadowStatType.Madness));
            Assert.Equal(0, stats.GetShadow(ShadowStatType.Horniness));
            Assert.Equal(0, stats.GetShadow(ShadowStatType.Dread));
            Assert.Equal(0, stats.GetShadow(ShadowStatType.Overthinking));
        }

        [Fact]
        public void Parse_ExtractsSystemPrompt()
        {
            var profile = CharacterLoader.Parse(SablePromptContent, "sable");
            Assert.Contains("You are playing the role of Sable", profile.AssembledSystemPrompt);
            Assert.Contains("EFFECTIVE STATS", profile.AssembledSystemPrompt);
        }

        [Fact]
        public void Parse_ZyxAllShadowsZero()
        {
            var profile = CharacterLoader.Parse(ZyxPromptContent, "zyx");
            Assert.Equal("Zyx", profile.DisplayName);
            Assert.Equal(1, profile.Level);
            Assert.Equal(0, profile.Stats.GetEffective(StatType.Charm));
            Assert.Equal(-1, profile.Stats.GetEffective(StatType.Rizz));
            Assert.Equal(12, profile.Stats.GetEffective(StatType.Honesty));
            // All shadows zero
            foreach (ShadowStatType shadow in Enum.GetValues(typeof(ShadowStatType)))
                Assert.Equal(0, profile.Stats.GetShadow(shadow));
        }

        [Fact]
        public void Parse_FallbackNameWhenNoHeader()
        {
            string content = @"```
LEVEL
- Level: 2 (Newbie) | Level bonus: +0

EFFECTIVE STATS
- Charm: +5
- Rizz: +5
- Honesty: +5
- Chaos: +5
- Wit: +5
- Self-Awareness: +5
```
";
            var profile = CharacterLoader.Parse(content, "testchar");
            Assert.Equal("Testchar", profile.DisplayName);
        }

        [Fact]
        public void Parse_DisplayNameFromInputsNameField()
        {
            // name=Gerald_42 should yield "Gerald_42", not "Gerald" from the header
            string content = @"# Gerald — Assembled System Prompt

> **Inputs:** name=Gerald_42 · he/him · bio=""Just a normal guy""

---

```
You are playing the role of Gerald_42, a sentient penis on the dating app Pinder.

LEVEL
- Level: 5 (Smooth-ish) | Level bonus: +2

EFFECTIVE STATS
- Charm: +13
- Rizz: +11
- Honesty: +5
- Chaos: +9
- Wit: +5
- Self-Awareness: +4
```
";
            var profile = CharacterLoader.Parse(content, "gerald");
            Assert.Equal("Gerald_42", profile.DisplayName);
        }

        [Fact]
        public void Parse_ThrowsFormatException_MissingEffectiveStats()
        {
            string content = @"# Test — Assembled System Prompt

```
You are playing the role of Test.

LEVEL
- Level: 1

Some text but no EFFECTIVE STATS section here.
```
";
            var ex = Assert.Throws<FormatException>(() =>
                CharacterLoader.Parse(content, "test"));
            Assert.Contains("EFFECTIVE STATS", ex.Message);
        }

        [Fact]
        public void Parse_ThrowsFormatException_MissingIndividualStats()
        {
            string content = @"```
LEVEL
- Level: 1

EFFECTIVE STATS
- Charm: +5
- Rizz: +5
- Honesty: +5
```
";
            var ex = Assert.Throws<FormatException>(() =>
                CharacterLoader.Parse(content, "test"));
            Assert.Contains("missing", ex.Message.ToLowerInvariant());
            // Should mention the missing stats
            Assert.Contains("Chaos", ex.Message);
            Assert.Contains("Wit", ex.Message);
            Assert.Contains("SelfAwareness", ex.Message);
        }

        [Fact]
        public void Load_ThrowsForMissingCharacter()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "charloader-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var ex = Assert.Throws<FileNotFoundException>(() =>
                    CharacterLoader.Load("nonexistent", tempDir));
                Assert.Contains("nonexistent", ex.Message);
                Assert.Contains("Available characters", ex.Message);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void Load_SucceedsFromDisk()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "charloader-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                File.WriteAllText(Path.Combine(tempDir, "sable-prompt.md"), SablePromptContent);
                var profile = CharacterLoader.Load("sable", tempDir);
                Assert.Equal("Sable", profile.DisplayName);
                Assert.Equal(3, profile.Level);
                Assert.Equal(7, profile.Stats.GetEffective(StatType.Charm));
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void Load_CaseInsensitive()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "charloader-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                File.WriteAllText(Path.Combine(tempDir, "sable-prompt.md"), SablePromptContent);
                var profile = CharacterLoader.Load("Sable", tempDir);
                Assert.Equal("Sable", profile.DisplayName);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ListAvailable_ReturnsCharacterNames()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "charloader-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                File.WriteAllText(Path.Combine(tempDir, "gerald-prompt.md"), "test");
                File.WriteAllText(Path.Combine(tempDir, "sable-prompt.md"), "test");
                string available = CharacterLoader.ListAvailable(tempDir);
                Assert.Contains("gerald", available);
                Assert.Contains("sable", available);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ListAvailable_NonexistentDir()
        {
            string result = CharacterLoader.ListAvailable("/nonexistent/path");
            Assert.Contains("not found", result);
        }

        [Fact]
        public void Load_AllFiveStarterCharactersFromRepo()
        {
            // Load from design/examples/ in the repo
            string repoRoot = FindRepoRoot();
            string promptDir = Path.Combine(repoRoot, "design", "examples");
            if (!Directory.Exists(promptDir))
            {
                // Explicit skip: prompt directory not available in this environment
                Assert.True(false, "SKIPPED: design/examples/ directory not found — cannot run repo integration test");
                return;
            }

            var expectedNames = new[] { "gerald", "brick", "sable", "velvet", "zyx" };
            foreach (var name in expectedNames)
            {
                var profile = CharacterLoader.Load(name, promptDir);
                Assert.False(string.IsNullOrEmpty(profile.DisplayName), $"{name} should have a display name");
                Assert.True(profile.Level >= 1, $"{name} should have level >= 1");
                Assert.False(string.IsNullOrEmpty(profile.AssembledSystemPrompt), $"{name} should have a system prompt");

                // Verify all 6 stats are present
                foreach (StatType stat in Enum.GetValues(typeof(StatType)))
                {
                    // Just verify no exception is thrown — value can be 0 or negative
                    _ = profile.Stats.GetEffective(stat);
                }
            }
        }

        private static string FindRepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir, ".git")))
                    return dir;
                var parent = Directory.GetParent(dir);
                if (parent == null) break;
                dir = parent.FullName;
            }
            return AppContext.BaseDirectory;
        }
    }
}
