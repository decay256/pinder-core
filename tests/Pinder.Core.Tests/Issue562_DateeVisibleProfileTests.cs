using System.Collections.Generic;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Issue #562: datee context bleed fix. The dialogue-options call
    /// now sees a structured Tinder-card-equivalent visible profile
    /// (display name + gender identity + bio + outfit description), NOT
    /// the raw equipped-items list nor the full system prompt.
    /// </summary>
    [Trait("Category", "Characters")]
    public class Issue562_DateeVisibleProfileTests
    {
        // ── Render: outfit-description path ──────────────────────────────

        [Fact]
        public void Render_WithOutfitDescription_OmitsItemsList()
        {
            var profile = new DateeVisibleProfile(
                displayName: "Sable_xo",
                genderIdentity: "she/her",
                bio: "looking for my person.",
                outfitDescription: "Sable in a velvet jumpsuit, leaning in a doorway.",
                equippedItemDisplayNamesFallback: new[] { "vintage band tee", "rubber duck", "work lanyard" });

            string rendered = profile.Render();

            Assert.Contains("Sable_xo", rendered);
            Assert.Contains("she/her", rendered);
            Assert.Contains("looking for my person.", rendered);
            Assert.Contains("Outfit:", rendered);
            Assert.Contains("velvet jumpsuit", rendered);
            // The raw items list MUST NOT appear when an outfit description is present.
            Assert.DoesNotContain("vintage band tee", rendered);
            Assert.DoesNotContain("rubber duck", rendered);
            Assert.DoesNotContain("work lanyard", rendered);
            Assert.DoesNotContain("Wearing:", rendered);
        }

        // ── Render: items-fallback path ──────────────────────────────────

        [Fact]
        public void Render_WithoutOutfitDescription_FallsBackToItemsList()
        {
            var profile = new DateeVisibleProfile(
                displayName: "Brick_haus",
                genderIdentity: "he/him",
                bio: "yacht club enjoyer",
                outfitDescription: "",
                equippedItemDisplayNamesFallback: new[] { "polo shirt", "deck shoes" });

            string rendered = profile.Render();

            Assert.Contains("Brick_haus", rendered);
            Assert.Contains("he/him", rendered);
            Assert.Contains("yacht club enjoyer", rendered);
            Assert.Contains("Wearing: polo shirt, deck shoes", rendered);
            Assert.DoesNotContain("Outfit:", rendered);
        }

        // ── Render: empty fields gracefully omitted ──────────────────────

        [Fact]
        public void Render_EmptyGenderIdentity_OmitsParenthesisedSegment()
        {
            var profile = new DateeVisibleProfile(
                displayName: "TestChar",
                genderIdentity: "",
                bio: "bio",
                outfitDescription: "",
                equippedItemDisplayNamesFallback: new string[0]);

            string rendered = profile.Render();

            Assert.StartsWith("TestChar:", rendered);
            Assert.DoesNotContain("()", rendered);
        }

        [Fact]
        public void Render_EmptyBio_OmitsBioSegment()
        {
            var profile = new DateeVisibleProfile(
                displayName: "TestChar",
                genderIdentity: "they/them",
                bio: "",
                outfitDescription: "",
                equippedItemDisplayNamesFallback: new string[0]);

            string rendered = profile.Render();

            Assert.Contains("TestChar (they/them)", rendered);
            Assert.DoesNotContain("\"\"", rendered);
        }

        [Fact]
        public void Render_AllFieldsEmpty_StillEmitsDisplayName()
        {
            var profile = new DateeVisibleProfile(
                displayName: "Anon",
                genderIdentity: "",
                bio: "",
                outfitDescription: "",
                equippedItemDisplayNamesFallback: new string[0]);

            string rendered = profile.Render();

            Assert.Equal("Anon", rendered);
        }

        // ── BuildDateeVisibleProfile factory ──────────────────────────

        [Fact]
        public void BuildDateeVisibleProfile_PopulatesAllFieldsFromCharacterProfile()
        {
            var profile = BuildProfile("Sable_xo", "she/her", "bio text",
                items: new[] { "rolex", "blazer" });

            var dto = GameSessionHelpers.BuildDateeVisibleProfile(profile,
                outfitDescription: "scene description");

            Assert.Equal("Sable_xo", dto.DisplayName);
            Assert.Equal("she/her", dto.GenderIdentity);
            Assert.Equal("bio text", dto.Bio);
            Assert.Equal("scene description", dto.OutfitDescription);
            Assert.Equal(new[] { "rolex", "blazer" }, dto.EquippedItemDisplayNamesFallback);
        }

        [Fact]
        public void BuildDateeVisibleProfile_DefaultOutfitDescription_IsEmpty()
        {
            var profile = BuildProfile("X", "they/them", "b", items: new[] { "i" });

            var dto = GameSessionHelpers.BuildDateeVisibleProfile(profile);

            Assert.Equal("", dto.OutfitDescription);
        }

        [Fact]
        public void BuildDateeVisibleProfile_NullDatee_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() =>
                GameSessionHelpers.BuildDateeVisibleProfile(null!));
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private static CharacterProfile BuildProfile(
            string name,
            string gender,
            string bio,
            IReadOnlyList<string>? items = null)
        {
            var stats = new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, 0 }, { StatType.Rizz, 0 },
                    { StatType.Honesty, 0 }, { StatType.Chaos, 0 },
                    { StatType.Wit, 0 }, { StatType.SelfAwareness, 0 },
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 }, { ShadowStatType.Despair, 0 },
                    { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 0 },
                });

            return TestHelpers.MakeCharacterProfile(
                stats, "system prompt", name,
                new TimingProfile(5, 1.0f, 0.0f, "neutral"), level: 1,
                bio: bio,
                equippedItemDisplayNames: items ?? new List<string>(),
                genderIdentity: gender);
        }
    }
}
