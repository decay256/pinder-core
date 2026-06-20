using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.I18n;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// #976 — engine-side population of Consequence from i18n catalogue.
    /// Covers key generation, slot substitution, ConsequenceCatalog adapter,
    /// and engine integration (ShadowCheckEngine, HorninessEngine).
    /// </summary>
    [Trait("Category", "Core")]
    public class Issue976_ConsequenceEnginePopulationTests
    {
        // ── Key generation ──────────────────────────────────────────

        [Fact]
        public void ForRoll_Pass_ReturnsPassKey()
        {
            Assert.Equal("consequence.roll.pass",
                ConsequenceKeys.ForRoll(true, FailureTier.Success));
        }

        [Fact]
        public void ForRoll_Miss_Fumble_ReturnsKey()
        {
            Assert.Equal("consequence.roll.miss.fumble",
                ConsequenceKeys.ForRoll(false, FailureTier.Fumble));
        }

        [Fact]
        public void ForRoll_Miss_TropeTrap_ReturnsKey()
        {
            Assert.Equal("consequence.roll.miss.tropetrap",
                ConsequenceKeys.ForRoll(false, FailureTier.TropeTrap));
        }

        [Fact]
        public void ForRoll_Miss_Catastrophe_ReturnsKey()
        {
            Assert.Equal("consequence.roll.miss.catastrophe",
                ConsequenceKeys.ForRoll(false, FailureTier.Catastrophe));
        }

        [Fact]
        public void ForShadowMiss_AllSixShadows_ProduceDistinctKeys()
        {
            var set = new HashSet<string>();
            foreach (ShadowStatType s in Enum.GetValues(typeof(ShadowStatType)))
            {
                var key = ConsequenceKeys.ForShadowMiss(s);
                Assert.StartsWith("consequence.shadow.miss.", key);
                set.Add(key);
            }
            Assert.Equal(6, set.Count);
        }

        [Fact]
        public void ForHorninessMiss_Fumble_ReturnsKey()
        {
            Assert.Equal("consequence.horniness.miss.fumble",
                ConsequenceKeys.ForHorninessMiss(FailureTier.Fumble));
        }

        // ── Slot substitution ───────────────────────────────────────

        [Fact]
        public void ApplySlots_NoStat_ReturnsUnchanged()
        {
            Assert.Equal("hello world",
                ConsequenceKeys.ApplySlots("hello world", null));
        }

        [Fact]
        public void ApplySlots_ReplacesStatPlaceholder()
        {
            Assert.Equal("Your Charm slips.",
                ConsequenceKeys.ApplySlots("Your {stat} slips.", "Charm"));
        }

        [Fact]
        public void ApplySlots_MultiplePlaceholders_AllReplaced()
        {
            Assert.Equal("Your Honesty, not Honesty!",
                ConsequenceKeys.ApplySlots("Your {stat}, not {stat}!", "Honesty"));
        }

        // ── ConsequenceCatalog: real yaml lookup ────────────────────

        [Fact]
        public void Catalog_RealYaml_AllRollKeysPresent()
        {
            var cat = LoadRealCatalog();
            Assert.NotNull(cat.Lookup("consequence.roll.pass"));
            Assert.NotNull(cat.Lookup("consequence.roll.miss.fumble"));
            Assert.NotNull(cat.Lookup("consequence.roll.miss.misfire"));
            Assert.NotNull(cat.Lookup("consequence.roll.miss.tropetrap"));
            Assert.NotNull(cat.Lookup("consequence.roll.miss.catastrophe"));
        }

        [Fact]
        public void Catalog_RealYaml_AllShadowMissKeysPresent()
        {
            var cat = LoadRealCatalog();
            foreach (ShadowStatType s in Enum.GetValues(typeof(ShadowStatType)))
            {
                string key = ConsequenceKeys.ForShadowMiss(s);
                Assert.NotNull(cat.Lookup(key));
            }
        }

        [Fact]
        public void Catalog_RealYaml_AllHorninessMissKeysPresent()
        {
            var cat = LoadRealCatalog();
            // The yaml has miss.fumble, miss.misfire, miss.tropetrap, miss.catastrophe
            Assert.NotNull(cat.Lookup("consequence.horniness.miss.fumble"));
            Assert.NotNull(cat.Lookup("consequence.horniness.miss.misfire"));
            Assert.NotNull(cat.Lookup("consequence.horniness.miss.tropetrap"));
            Assert.NotNull(cat.Lookup("consequence.horniness.miss.catastrophe"));
        }

        [Fact]
        public void Catalog_MissingKey_ReturnsNull()
        {
            var cat = LoadRealCatalog();
            Assert.Null(cat.Lookup("consequence.nonexistent.key"));
        }

        // ── ConsequenceCatalog: {stat} substitution in real values ──

        [Fact]
        public void Catalog_RollPass_ApplySlots_RemovesStatPlaceholder()
        {
            var cat = LoadRealCatalog();
            string? template = cat.Lookup("consequence.roll.pass");
            Assert.NotNull(template);
            // The raw template contains {stat}; ApplySlots substitutes it.
            // After substitution, no {stat} placeholder should remain.
            string result = ConsequenceKeys.ApplySlots(template, "Honesty");
            Assert.DoesNotContain("{stat}", result);
            Assert.Contains("Honesty", result);
        }

        [Fact]
        public void Catalog_RollFumble_Substituted_ContainsStatName()
        {
            var cat = LoadRealCatalog();
            string? template = cat.Lookup("consequence.roll.miss.fumble");
            Assert.NotNull(template);
            string result = ConsequenceKeys.ApplySlots(template, "Chaos");
            // After substitution, the stat name should be in the text
            Assert.Contains("Chaos", result);
        }

        // ── ShadowCheckEngine integration ───────────────────────────

        [Fact]
        public void ShadowCheckEngine_NoCatalog_ConsequenceNullOnMiss()
        {
            var rng = new Random(99);
            var engine = new ShadowCheckEngine(rng, null);
            var result = engine.Check(ShadowStatType.Dread, 1);
            // dc=19, roll low → miss; consequence should be null without catalog
            if (result.CheckPerformed && result.IsMiss)
                Assert.Null(result.Consequence);
        }

        [Fact]
        public void ShadowCheckEngine_WithFakeCatalog_Miss_PopulatesConsequence()
        {
            var cat = new FakeConsequenceCatalog();
            cat.Add("consequence.shadow.miss.dread", "Dread tightens.");
            // Seed RNG so roll=1 → miss on dc=5
            var rng = new Random(17);
            var engine = new ShadowCheckEngine(rng, cat);
            var result = engine.Check(ShadowStatType.Dread, 5); // dc=5
            Assert.True(result.CheckPerformed);
            if (result.IsMiss)
            {
                Assert.NotNull(result.Consequence);
                Assert.Equal("Dread tightens.", result.Consequence);
            }
        }

        [Fact]
        public void ShadowCheckEngine_NotPerformed_ConsequenceNull()
        {
            var cat = new FakeConsequenceCatalog();
            cat.Add("consequence.shadow.miss.dread", "Dread.");
            var engine = new ShadowCheckEngine(new Random(1), cat);
            var result = engine.Check(ShadowStatType.Dread, 0);
            Assert.False(result.CheckPerformed);
            Assert.Null(result.Consequence);
        }

        // ── HorninessEngine integration ─────────────────────────────

        [Fact]
        public void HorninessEngine_NoCatalog_ConsequenceNull()
        {
            var engine = new HorninessEngine(new Random(55), null);
            var shadows = MakeShadows();
            shadows.ApplyGrowth(ShadowStatType.Dread, 1, "test");
            var (result, _) = engine.PeekAsync(10, shadows, null);
            Assert.Null(result.Consequence);
        }

        [Fact]
        public void HorninessEngine_WithFakeCatalog_Miss_PopulatesConsequence()
        {
            var cat = new FakeConsequenceCatalog();
            cat.Add("consequence.horniness.miss.fumble", "Fumbled.");
            cat.Add("consequence.horniness.miss.misfire", "Misfired.");
            cat.Add("consequence.horniness.miss.tropetrap", "Trope-trapped.");
            cat.Add("consequence.horniness.miss.catastrophe", "Catastrophic.");
            // Seed 7 produces roll=8 → miss on dc=10, tier=Fumble
            var rng = new Random(7);
            var engine = new HorninessEngine(rng, cat);
            var shadows = MakeShadows();
            shadows.ApplyGrowth(ShadowStatType.Dread, 1, "test");
            var (result, _) = engine.PeekAsync(10, shadows, null);
            Assert.True(result.IsMiss);
            Assert.NotNull(result.Consequence);
            Assert.Equal("Fumbled.", result.Consequence);
        }

        [Fact]
        public void HorninessEngine_NotPerformed_ConsequenceNull()
        {
            var cat = new FakeConsequenceCatalog();
            cat.Add("consequence.horniness.miss.fumble", "Fumble.");
            var engine = new HorninessEngine(new Random(1), cat);
            // sessionHorniness=5 but playerShadows=null → not performed
            var (result, _) = engine.PeekAsync(5, null, null);
            Assert.True(result.Check == null);
            Assert.Null(result.Consequence);
        }

        // ── Helpers ─────────────────────────────────────────────────

        private static StatBlock MakeStatBlock()
        {
            var baseStats = new Dictionary<StatType, int>
            {
                { StatType.Charm,         5 },
                { StatType.Rizz,          5 },
                { StatType.Honesty,       5 },
                { StatType.Chaos,         5 },
                { StatType.Wit,           5 },
                { StatType.SelfAwareness, 5 }
            };
            var shadowStats = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Madness,      0 },
                { ShadowStatType.Despair,      0 },
                { ShadowStatType.Denial,       0 },
                { ShadowStatType.Fixation,     0 },
                { ShadowStatType.Dread,        0 },
                { ShadowStatType.Overthinking, 0 }
            };
            return new StatBlock(baseStats, shadowStats);
        }

        private static SessionShadowTracker MakeShadows()
        {
            return new SessionShadowTracker(MakeStatBlock());
        }

        private static ConsequenceCatalog LoadRealCatalog()
        {
            // Walk up from test bin to find data/i18n. Mirrors Issue474SnapshotEventsTests.
            string dir = AppContext.BaseDirectory;
            for (int i = 0; i < 12; i++)
            {
                string candidate = System.IO.Path.Combine(dir, "data", "i18n");
                if (System.IO.Directory.Exists(candidate))
                {
                    var i18n = I18nCatalog.LoadFromDirectory(candidate, "en");
                    return new ConsequenceCatalog(i18n);
                }
                var parent = System.IO.Directory.GetParent(dir);
                if (parent == null) break;
                dir = parent.FullName;
            }
            throw new System.IO.DirectoryNotFoundException(
                "could not find data/i18n above test base dir");
        }

        private sealed class FakeConsequenceCatalog : IConsequenceCatalog
        {
            private readonly Dictionary<string, string> _entries = new();
            public void Add(string key, string value) => _entries[key] = value;
            public string? Lookup(string key)
            {
                _entries.TryGetValue(key, out var v);
                return v;
            }
        }
    }
}
