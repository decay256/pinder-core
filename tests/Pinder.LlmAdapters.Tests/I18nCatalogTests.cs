using System.IO;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// Sprint: i18n string extraction (pinder-web issue #436 / Phase 1.5).
    /// Loads the real seeded yaml under <c>data/i18n/en/</c> via a
    /// repo-relative path. The yaml ships in the same repo as this
    /// test, so the path is stable.
    /// </summary>
    public class I18nCatalogTests
    {
        // Resolve relative to the project working directory at test
        // run time. Test runs cd into bin/Debug/net8.0; walk up to the
        // repo root.
        private static string I18nRoot()
        {
            var dir = Directory.GetCurrentDirectory();
            // From .../tests/Pinder.LlmAdapters.Tests/bin/Debug/net8.0 walk
            // up until we find data/i18n.
            for (int i = 0; i < 12; i++)
            {
                var candidate = Path.Combine(dir, "data", "i18n");
                if (Directory.Exists(candidate)) return candidate;
                var parent = Directory.GetParent(dir);
                if (parent is null) break;
                dir = parent.FullName;
            }
            throw new DirectoryNotFoundException(
                $"could not find data/i18n above {Directory.GetCurrentDirectory()}");
        }

        [Fact]
        public void Loads_seeded_strings_from_ui_yaml()
        {
            var cat = I18nCatalog.LoadFromDirectory(I18nRoot(), "en");
            Assert.Equal("en", cat.Locale);
            Assert.Equal("Open player sheet", cat.T("topnav.open_player_sheet"));
            Assert.Equal("Open opponent sheet", cat.T("topnav.open_opponent_sheet"));
        }

        [Fact]
        public void Loads_seeded_combo_hit_event()
        {
            var cat = I18nCatalog.LoadFromDirectory(I18nRoot(), "en");
            Assert.True(cat.Events.ContainsKey("combo_hit"));
            var entry = cat.Events["combo_hit"];
            Assert.Equal("Combo!", entry.Title);
            Assert.Equal(5, entry.SummaryVariants.Count);
        }

        [Fact]
        public void TVariant_is_deterministic_and_in_range()
        {
            var cat = I18nCatalog.LoadFromDirectory(I18nRoot(), "en");
            var a = cat.TVariant("combo_hit", 7);
            var b = cat.TVariant("combo_hit", 7);
            Assert.Equal(a, b);
            Assert.Contains(a, cat.Events["combo_hit"].SummaryVariants);
        }

        [Fact]
        public void Throws_on_missing_string_key()
        {
            var cat = I18nCatalog.LoadFromDirectory(I18nRoot(), "en");
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(
                () => cat.T("topnav.does_not_exist"));
        }

        [Fact]
        public void Throws_on_missing_event_kind()
        {
            var cat = I18nCatalog.LoadFromDirectory(I18nRoot(), "en");
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(
                () => cat.TVariant("does_not_exist", 0));
        }
    }
}
