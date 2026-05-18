using System.Text.Json;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.Core.Tests.Issue924
{
    /// <summary>
    /// Issue #924: direct <see cref="JsonSerializer.Serialize"/> of a
    /// <see cref="RollResult"/> must emit a consistently snake_case + string-enum
    /// shape across every enum property (no mixed int/string, no mixed casing).
    ///
    /// NOT a wire-DTO test — the production wire path goes through
    /// <c>RollResultDto.From()</c> in pinder-web. This test pins the latent
    /// direct-serialization shape so test/debug/refactor callers see a
    /// predictable JSON.
    /// </summary>
    [Trait("Category", "Core")]
    public class Issue924_EnumSerializationShapeTests
    {
        private static RollResult MakeResult()
        {
            // Hit-success path; tier ends up Success on success but we
            // verify the property still serializes as a string.
            return new RollResult(
                dieRoll: 14,
                secondDieRoll: null,
                usedDieRoll: 14,
                stat: StatType.Wit,
                statModifier: 3,
                levelBonus: 1,
                dc: 12,
                tier: FailureTier.Success,
                activatedTrap: null,
                externalBonus: 0,
                defendingStat: StatType.Charm);
        }

        private static RollResult MakeMissResult()
        {
            // Force a real failure tier so we pin the string form of a
            // non-default FailureTier value.
            return new RollResult(
                dieRoll: 2,
                secondDieRoll: null,
                usedDieRoll: 2,
                stat: StatType.Chaos,
                statModifier: 0,
                levelBonus: 0,
                dc: 25,
                tier: FailureTier.Catastrophe,
                activatedTrap: null,
                externalBonus: 0,
                defendingStat: StatType.Honesty);
        }

        [Fact]
        public void Stat_serialises_as_snake_case_string_enum()
        {
            var json = JsonSerializer.Serialize(MakeResult());
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.True(root.TryGetProperty("stat", out var stat),
                "expected snake_case 'stat' property");
            Assert.Equal(JsonValueKind.String, stat.ValueKind);
            Assert.Equal("Wit", stat.GetString());
            // The old PascalCase, int-valued shape must be gone.
            Assert.False(root.TryGetProperty("Stat", out _),
                "did not expect legacy PascalCase 'Stat' property");
        }

        [Fact]
        public void DefendingStat_serialises_as_snake_case_string_enum()
        {
            var json = JsonSerializer.Serialize(MakeResult());
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.True(root.TryGetProperty("defending_stat", out var ds));
            Assert.Equal(JsonValueKind.String, ds.ValueKind);
            Assert.Equal("Charm", ds.GetString());
        }

        [Fact]
        public void Tier_serialises_as_snake_case_string_enum()
        {
            var json = JsonSerializer.Serialize(MakeMissResult());
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.True(root.TryGetProperty("tier", out var tier),
                "expected snake_case 'tier' property");
            Assert.Equal(JsonValueKind.String, tier.ValueKind);
            Assert.Equal("Catastrophe", tier.GetString());
            Assert.False(root.TryGetProperty("Tier", out _),
                "did not expect legacy PascalCase 'Tier' property");
        }

        [Fact]
        public void RiskTier_serialises_as_snake_case_string_enum()
        {
            var json = JsonSerializer.Serialize(MakeMissResult());
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.True(root.TryGetProperty("risk_tier", out var rt),
                "expected snake_case 'risk_tier' property");
            Assert.Equal(JsonValueKind.String, rt.ValueKind);
            // dc=25, statMod=0, levelBonus=0 → need=25 → Reckless.
            Assert.Equal("Reckless", rt.GetString());
            Assert.False(root.TryGetProperty("RiskTier", out _),
                "did not expect legacy PascalCase 'RiskTier' property");
        }

        [Fact]
        public void All_four_enum_props_share_one_consistent_shape()
        {
            // Pin field-by-field that every enum property on RollResult uses
            // the same snake_case + string-enum convention. This is the
            // regression guard against the #924 mixed-shape footgun.
            var json = JsonSerializer.Serialize(MakeMissResult());
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            foreach (var name in new[] { "stat", "defending_stat", "tier", "risk_tier" })
            {
                Assert.True(root.TryGetProperty(name, out var prop),
                    $"missing snake_case property '{name}'");
                Assert.Equal(JsonValueKind.String, prop.ValueKind);
            }

            // And the legacy PascalCase forms must all be absent.
            foreach (var legacy in new[] { "Stat", "DefendingStat", "Tier", "RiskTier" })
            {
                Assert.False(root.TryGetProperty(legacy, out _),
                    $"unexpected legacy PascalCase property '{legacy}'");
            }
        }
    }
}
