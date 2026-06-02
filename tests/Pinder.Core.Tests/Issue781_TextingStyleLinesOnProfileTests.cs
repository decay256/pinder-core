using System;
using System.Collections.Generic;
using System.IO;
using Pinder.Core.Characters;
using Pinder.Core.Data;
using Pinder.Core.Interfaces;
using Pinder.Core.Prompts;
using Pinder.Core.Stats;
using Pinder.Core.Conversation;
using Pinder.SessionSetup;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Issue #781 — <c>CharacterProfile.TextingStyleLines</c> must carry
    /// the final aggregated axis lines produced by
    /// <c>TextingStyleAggregator.AggregateWithAudit</c> so the admin-facing
    /// character sheet can surface them without re-running the aggregator.
    /// </summary>
    [Trait("Category", "Characters")]
    public class Issue781_TextingStyleLinesOnProfileTests
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
                throw new InvalidOperationException(
                    "Cannot find repo root from " + AppContext.BaseDirectory);
            }
        }

        private static IItemRepository LoadItemRepo()
        {
            string json = File.ReadAllText(
                Path.Combine(RepoRoot, "data", "items", "starter-items.json"));
            return new JsonItemRepository(json);
        }

        private static IAnatomyRepository LoadAnatomyRepo()
        {
            string json = File.ReadAllText(
                Path.Combine(RepoRoot, "data", "anatomy", "anatomy-parameters.json"));
            return new JsonAnatomyRepository(json);
        }

        // ── TextingStyleLines on CharacterProfile defaults to empty list ──────

        [Fact]
        public void CharacterProfile_DefaultConstructor_TextingStyleLinesIsEmpty()
        {
            // Existing callers that omit textingStyleLines must not break.
            var stats = new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, 0 }, { StatType.Rizz, 0 }, { StatType.Honesty, 0 },
                    { StatType.Chaos, 0 }, { StatType.Wit, 0 }, { StatType.SelfAwareness, 0 },
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 }, { ShadowStatType.Despair, 0 },
                    { ShadowStatType.Denial, 0 },  { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 },   { ShadowStatType.Overthinking, 0 },
                });
            var timing = new TimingProfile(500, 0.1f, 3000f, "neutral");
            var profile = new CharacterProfile(
                stats,
                "prompt",
                "TestChar",
                timing,
                level: 1);

            Assert.NotNull(profile.TextingStyleLines);
            Assert.Empty(profile.TextingStyleLines);
        }

        // ── CharacterDefinitionLoader: TextingStyleLines populated on load ────

        [Fact]
        public void Load_GeraldDefinition_TextingStyleLinesIsNotNull()
        {
            var profile = CharacterDefinitionLoader.Load(
                Path.Combine(RepoRoot, "data", "characters", "gerald.json"),
                LoadItemRepo(), LoadAnatomyRepo());

            Assert.NotNull(profile.TextingStyleLines);
        }

        [Fact]
        public void Load_GeraldDefinition_TextingStyleLinesConsistentWithFragment()
        {
            // The joined fragment must equal the Lines joined with " | "
            // (same guarantee as AggregateWithAudit → Aggregate).
            var profile = CharacterDefinitionLoader.Load(
                Path.Combine(RepoRoot, "data", "characters", "gerald.json"),
                LoadItemRepo(), LoadAnatomyRepo());

            string expectedJoined = profile.TextingStyleLines.Count == 0
                ? string.Empty
                : string.Join(" | ", profile.TextingStyleLines);

            Assert.Equal(expectedJoined, profile.TextingStyleFragment);
        }

        [Fact]
        public void Load_AllStarterCharacters_TextingStyleLinesConsistentWithFragment()
        {
            // Consistency check across every bundled character: Lines joined
            // with " | " must always equal the Fragment string that feeds the
            // LLM prompt. This guards against the two being computed independently
            // and drifting apart.
            var itemRepo   = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();
            var slugs = new[] { "brick", "gerald", "reuben", "sable", "velvet", "zyx" };

            foreach (var slug in slugs)
            {
                string path = Path.Combine(RepoRoot, "data", "characters", $"{slug}.json");
                if (!File.Exists(path)) continue;

                var profile = CharacterDefinitionLoader.Load(path, itemRepo, anatomyRepo);

                string expectedJoined = profile.TextingStyleLines.Count == 0
                    ? string.Empty
                    : string.Join(" | ", profile.TextingStyleLines);

                Assert.True(
                    expectedJoined == profile.TextingStyleFragment,
                    $"TextingStyleLines/Fragment mismatch for character '{slug}': " +
                    $"expected='{expectedJoined}' actual='{profile.TextingStyleFragment}'");
            }
        }

        [Fact]
        public void Load_GeraldDefinition_TextingStyleLines_EachLineHasAxisColonFormat()
        {
            // Every emitted line from AggregateWithAudit has the shape "axis: rule".
            var profile = CharacterDefinitionLoader.Load(
                Path.Combine(RepoRoot, "data", "characters", "gerald.json"),
                LoadItemRepo(), LoadAnatomyRepo());

            foreach (var line in profile.TextingStyleLines)
            {
                Assert.True(line.Contains(':'),
                    $"TextingStyleLines entry '{line}' should have 'axis: rule' format");
                Assert.False(string.IsNullOrWhiteSpace(line.Substring(line.IndexOf(':') + 1).Trim()),
                    $"TextingStyleLines entry '{line}' should have a non-empty rule after the colon");
            }
        }

        // ── Minimal fixture: empty sources → empty Lines ──────────────────────

        [Fact]
        public void Parse_NoItems_NoAnatomy_TextingStyleLinesIsEmpty()
        {
            const string json = @"{
                ""schema_version"": 1,
                ""character_id"": ""550e8400-e29b-41d4-a716-446655440000"",
                ""name"": ""TestChar"",
                ""gender_identity"": ""they/them"",
                ""bio"": ""test bio"",
                ""level"": 1,
                ""items"": [],
                ""anatomy"": {},
                ""allocation"": {
                    ""spent"": {
                        ""charm"": 1, ""rizz"": 1, ""honesty"": 1,
                        ""chaos"": 1, ""wit"": 1, ""self_awareness"": 1
                    },
                    ""unspent_pool"": 0,
                    ""shadows"": {
                        ""madness"": 0, ""despair"": 0, ""denial"": 0,
                        ""fixation"": 0, ""dread"": 0, ""overthinking"": 0
                    }
                }
            }";

            var itemRepo    = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();
            var profile = CharacterDefinitionLoader.Parse(json, itemRepo, anatomyRepo);

            Assert.NotNull(profile.TextingStyleLines);
            Assert.Empty(profile.TextingStyleLines);
            Assert.Equal(string.Empty, profile.TextingStyleFragment);
        }
    }
}
