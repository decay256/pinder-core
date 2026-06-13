using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public partial class Issue544_EngineInjectionSpecTests
    {
        // ═══════════════════════════════════════════════════════════════
        // AC4: Interest narratives — 6 bands with correct boundaries
        // ═══════════════════════════════════════════════════════════════

        // Mutation: would catch if band boundary at 0 used wrong narrative
        [Fact]
        public void AC4_InterestNarrative_Band0_Unmatched()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(interestBefore: 1, interestAfter: 0));
            Assert.Contains("Unmatched", result);
        }

        // Mutation: would catch if band 1-4 lower boundary used Unmatched text
        [Fact]
        public void AC4_InterestNarrative_Band1_LowerBoundary()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(interestAfter: 1));
            Assert.Contains("Reconsidering", result);
        }

        // Mutation: would catch if band 1-4 upper boundary used next band's text
        [Fact]
        public void AC4_InterestNarrative_Band4_UpperBoundary()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(interestAfter: 4));
            Assert.Contains("Reconsidering", result);
        }

        // Mutation: would catch if boundary 5 was in band 1-4 instead of 5-9
        [Fact]
        public void AC4_InterestNarrative_Band5_LowerBoundary()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(interestAfter: 5));
            Assert.Contains("Skeptical", result);
        }

        // Mutation: would catch if boundary 9 was in band 10-14
        [Fact]
        public void AC4_InterestNarrative_Band9_UpperBoundary()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(interestAfter: 9));
            Assert.Contains("Skeptical", result);
        }

        // Mutation: would catch if boundary 10 was in band 5-9
        [Fact]
        public void AC4_InterestNarrative_Band10_LowerBoundary()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(interestAfter: 10));
            Assert.Contains("Engaged but not sold", result);
        }

        // Mutation: would catch if boundary 14 was in band 15-20
        [Fact]
        public void AC4_InterestNarrative_Band14_UpperBoundary()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(interestAfter: 14));
            Assert.Contains("Engaged but not sold", result);
        }

        // Mutation: would catch if boundary 15 was in band 10-14
        [Fact]
        public void AC4_InterestNarrative_Band15_LowerBoundary()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(interestAfter: 15));
            Assert.Contains("Interested but holding back", result);
        }

        // Mutation: would catch if boundary 20 was in band 21-24
        [Fact]
        public void AC4_InterestNarrative_Band20_UpperBoundary()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(interestAfter: 20));
            Assert.Contains("Interested but holding back", result);
        }

        // Mutation: would catch if boundary 21 was in band 15-20
        [Fact]
        public void AC4_InterestNarrative_Band21_LowerBoundary()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(interestAfter: 21));
            Assert.Contains("Basically sold", result);
        }

        // Mutation: would catch if boundary 24 was in band 25
        [Fact]
        public void AC4_InterestNarrative_Band24_UpperBoundary()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(interestAfter: 24));
            Assert.Contains("Basically sold", result);
        }

        // Mutation: would catch if boundary 25 was in band 21-24
        [Fact]
        public void AC4_InterestNarrative_Band25_DateSecured()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(interestAfter: 25));
            Assert.Contains("resistance dissolved", result);
        }

        // Mutation: would catch if all 6 bands returned the same text
        [Fact]
        public void AC4_InterestNarrative_AllBandsDistinct()
        {
            var narratives = new HashSet<string>();
            int[] representatives = { 0, 2, 7, 12, 18, 22, 25 };
            foreach (int i in representatives)
            {
                var result = SessionDocumentBuilder.BuildDateePrompt(
                    MakeDateeContext(interestBefore: i, interestAfter: i));
                // Extract the narrative portion - each should be unique
                narratives.Add(result);
            }
            Assert.Equal(representatives.Length, narratives.Count);
        }

        // ═══════════════════════════════════════════════════════════════
        // AC5: RollContextBuilder — YAML flavor sourcing + fallbacks
        // ═══════════════════════════════════════════════════════════════

        // Mutation: would catch if clean success used strong success text
        [Fact]
        public void AC5_RollContext_CleanSuccess_1To4()
        {
            var builder = new RollContextBuilder();
            var result = builder.GetSuccessContext(1, false);
            Assert.Equal(RollContextBuilder.FallbackCleanSuccess, result);

            var result4 = builder.GetSuccessContext(4, false);
            Assert.Equal(RollContextBuilder.FallbackCleanSuccess, result4);
        }

        // Mutation: would catch if boundary 5 used clean instead of strong
        [Fact]
        public void AC5_RollContext_StrongSuccess_BeatBy5()
        {
            var builder = new RollContextBuilder();
            var result = builder.GetSuccessContext(5, false);
            Assert.Equal(RollContextBuilder.FallbackStrongSuccess, result);
        }

        // Mutation: would catch if boundary 9 used critical instead of strong
        [Fact]
        public void AC5_RollContext_StrongSuccess_BeatBy9()
        {
            var builder = new RollContextBuilder();
            var result = builder.GetSuccessContext(9, false);
            Assert.Equal(RollContextBuilder.FallbackStrongSuccess, result);
        }

        // Mutation: would catch if boundary 10 used strong instead of critical
        [Fact]
        public void AC5_RollContext_CriticalSuccess_BeatBy10()
        {
            var builder = new RollContextBuilder();
            var result = builder.GetSuccessContext(10, false);
            Assert.Equal(RollContextBuilder.FallbackCriticalSuccess, result);
        }

        // Mutation: would catch if Nat20 flag was ignored (returned critical instead)
        [Fact]
        public void AC5_RollContext_Nat20_OverridesBeatDcBy()
        {
            var builder = new RollContextBuilder();
            // Even with beatDcBy of 2 (clean range), Nat20 should use Nat20 text
            var result = builder.GetSuccessContext(2, true);
            Assert.Equal(RollContextBuilder.FallbackNat20, result);
        }

        // Mutation: would catch if Nat20 was treated as critical success
        [Fact]
        public void AC5_RollContext_Nat20_NotSameAsCritical()
        {
            var builder = new RollContextBuilder();
            Assert.NotEqual(
                builder.GetSuccessContext(12, false),
                builder.GetSuccessContext(12, true));
        }

        // Mutation: would catch if all failure tiers returned the same text
        [Fact]
        public void AC5_RollContext_AllFailureTiersDistinct()
        {
            var builder = new RollContextBuilder();
            var results = new HashSet<string>
            {
                builder.GetFailureContext(FailureTier.Fumble),
                builder.GetFailureContext(FailureTier.Misfire),
                builder.GetFailureContext(FailureTier.TropeTrap),
                builder.GetFailureContext(FailureTier.Catastrophe),
                builder.GetFailureContext(FailureTier.Legendary)
            };
            Assert.Equal(5, results.Count);
        }

        // Mutation: would catch if specific failure tier returned wrong text
        [Fact]
        public void AC5_RollContext_Fumble_HasFumbleText()
        {
            var builder = new RollContextBuilder();
            Assert.Equal(RollContextBuilder.FallbackFumble, builder.GetFailureContext(FailureTier.Fumble));
        }

        [Fact]
        public void AC5_RollContext_Misfire_HasMisfireText()
        {
            var builder = new RollContextBuilder();
            Assert.Equal(RollContextBuilder.FallbackMisfire, builder.GetFailureContext(FailureTier.Misfire));
        }

        [Fact]
        public void AC5_RollContext_TropeTrap_HasTrapText()
        {
            var builder = new RollContextBuilder();
            Assert.Equal(RollContextBuilder.FallbackTropeTrap, builder.GetFailureContext(FailureTier.TropeTrap));
        }

        [Fact]
        public void AC5_RollContext_Catastrophe_HasCatastropheText()
        {
            var builder = new RollContextBuilder();
            Assert.Equal(RollContextBuilder.FallbackCatastrophe, builder.GetFailureContext(FailureTier.Catastrophe));
        }

        [Fact]
        public void AC5_RollContext_Legendary_HasLegendaryText()
        {
            var builder = new RollContextBuilder();
            Assert.Equal(RollContextBuilder.FallbackLegendary, builder.GetFailureContext(FailureTier.Legendary));
        }

        // Mutation: would catch if YAML override didn't replace fallback
        [Fact]
        public void AC5_RollContext_YamlOverride_AllSuccessTiers()
        {
            var flavors = new Dictionary<string, string>
            {
                { "§7.success-scale.1-4", "yaml clean" },
                { "§7.success-scale.5-9", "yaml strong" },
                { "§7.success-scale.10plus", "yaml critical" },
                { "§7.success-scale.nat-20", "yaml nat20" }
            };
            var builder = new RollContextBuilder(flavors);

            Assert.Equal("yaml clean", builder.GetSuccessContext(3, false));
            Assert.Equal("yaml strong", builder.GetSuccessContext(7, false));
            Assert.Equal("yaml critical", builder.GetSuccessContext(12, false));
            Assert.Equal("yaml nat20", builder.GetSuccessContext(12, true));
        }

        // Mutation: would catch if YAML override didn't replace failure fallbacks
        [Fact]
        public void AC5_RollContext_YamlOverride_AllFailureTiers()
        {
            var flavors = new Dictionary<string, string>
            {
                { "§7.fail-tier.fumble", "yaml fumble" },
                { "§7.fail-tier.misfire", "yaml misfire" },
                { "§7.fail-tier.trope-trap", "yaml trap" },
                { "§7.fail-tier.catastrophe", "yaml catastrophe" },
                { "§7.fail-tier.legendary-fail", "yaml legendary" }
            };
            var builder = new RollContextBuilder(flavors);

            Assert.Equal("yaml fumble", builder.GetFailureContext(FailureTier.Fumble));
            Assert.Equal("yaml misfire", builder.GetFailureContext(FailureTier.Misfire));
            Assert.Equal("yaml trap", builder.GetFailureContext(FailureTier.TropeTrap));
            Assert.Equal("yaml catastrophe", builder.GetFailureContext(FailureTier.Catastrophe));
            Assert.Equal("yaml legendary", builder.GetFailureContext(FailureTier.Legendary));
        }

        // Mutation: would catch if partial YAML overrode ALL tiers instead of just matching ones
        [Fact]
        public void AC5_RollContext_PartialYaml_OnlyOverridesMatching()
        {
            var flavors = new Dictionary<string, string>
            {
                { "§7.fail-tier.fumble", "custom fumble" }
            };
            var builder = new RollContextBuilder(flavors);

            Assert.Equal("custom fumble", builder.GetFailureContext(FailureTier.Fumble));
            // Others should still be fallback
            Assert.Equal(RollContextBuilder.FallbackMisfire, builder.GetFailureContext(FailureTier.Misfire));
            Assert.Equal(RollContextBuilder.FallbackCatastrophe, builder.GetFailureContext(FailureTier.Catastrophe));
        }

        // Mutation: would catch if YAML key lookup was case-sensitive
        [Fact]
        public void AC5_RollContext_YamlLookup_CaseInsensitive()
        {
            var flavors = new Dictionary<string, string>
            {
                { "§7.FAIL-TIER.FUMBLE", "upper case fumble" }
            };
            var builder = new RollContextBuilder(flavors);
            Assert.Equal("upper case fumble", builder.GetFailureContext(FailureTier.Fumble));
        }

        // Mutation: would catch if FromRuleBook threw on null instead of ArgumentNullException
        [Fact]
        public void AC5_RollContext_FromRuleBook_NullThrows()
        {
            Assert.Throws<ArgumentNullException>(() => RollContextBuilder.FromRuleBook(null!));
        }

        // Mutation: would catch if empty constructor didn't produce valid fallback results
        [Fact]
        public void AC5_RollContext_EmptyConstructor_AllFallbacksNonEmpty()
        {
            var builder = new RollContextBuilder();

            // All success tiers
            Assert.False(string.IsNullOrEmpty(builder.GetSuccessContext(1, false)));
            Assert.False(string.IsNullOrEmpty(builder.GetSuccessContext(5, false)));
            Assert.False(string.IsNullOrEmpty(builder.GetSuccessContext(10, false)));
            Assert.False(string.IsNullOrEmpty(builder.GetSuccessContext(1, true)));

            // All failure tiers
            Assert.False(string.IsNullOrEmpty(builder.GetFailureContext(FailureTier.Fumble)));
            Assert.False(string.IsNullOrEmpty(builder.GetFailureContext(FailureTier.Misfire)));
            Assert.False(string.IsNullOrEmpty(builder.GetFailureContext(FailureTier.TropeTrap)));
            Assert.False(string.IsNullOrEmpty(builder.GetFailureContext(FailureTier.Catastrophe)));
            Assert.False(string.IsNullOrEmpty(builder.GetFailureContext(FailureTier.Legendary)));
        }
    }
}
