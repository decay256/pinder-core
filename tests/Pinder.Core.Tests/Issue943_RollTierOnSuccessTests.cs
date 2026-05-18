using Xunit;
using Pinder.Core.Rolls;
using System.Text.Json;

namespace Pinder.Core.Tests
{
    public class Issue943_RollTierOnSuccessTests
    {
        [Fact]
        public void SuccessfulRoll_HasSuccessTier()
        {
            // Arrange
            int dc = 10;
            int roll = 15; // Success
            
            // Act
            // We can't easily instantiate RollResult without RollEngine or mocking dependencies, 
            // so we use a simple mock-like constructor call if possible, 
            // but the real test is verifying the logic in RollResult constructor.
            
            // Using the public constructor of RollResult:
            // RollResult(dieRoll, secondDieRoll, usedDieRoll, stat, statModifier, levelBonus, dc, tier, activatedTrap, externalBonus, check, defendingStat)
            
            var check = new RollCheckResult(
                RollCheckKind.OptionRoll, 15, null, 15, 
                new System.Collections.Generic.List<Pinder.Core.Rolls.NamedModifier>(), 0, 15, dc, true, false, false, 
                FailureTier.Success, 0);

            var result = new RollResult(
                15, null, 15, Pinder.Core.Stats.StatType.Charm, 0, 0, dc, 
                FailureTier.Success, null, 0, check, Pinder.Core.Stats.StatType.Charm);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal(FailureTier.Success, result.Tier);
        }

        [Fact]
        public void FailedRoll_HasCorrectFailureTier()
        {
            // Arrange
            int dc = 10;
            int roll = 5; // Miss by 5 -> Misfire
            
            var check = new RollCheckResult(
                RollCheckKind.OptionRoll, 5, null, 5, 
                new System.Collections.Generic.List<Pinder.Core.Rolls.NamedModifier>(), 0, 5, dc, false, false, false, 
                FailureTier.Misfire, 5);

            var result = new RollResult(
                5, null, 5, Pinder.Core.Stats.StatType.Charm, 0, 0, dc, 
                FailureTier.Misfire, null, 0, check, Pinder.Core.Stats.StatType.Charm);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal(FailureTier.Misfire, result.Tier);
        }

        [Fact]
        public void Serialization_SuccessfulRoll_EmitsTier()
        {
            // Arrange
            var check = new RollCheckResult(
                RollCheckKind.OptionRoll, 15, null, 15, 
                new System.Collections.Generic.List<Pinder.Core.Rolls.NamedModifier>(), 0, 15, 10, true, false, false, 
                FailureTier.Success, 0);

            var result = new RollResult(
                15, null, 15, Pinder.Core.Stats.StatType.Charm, 0, 0, 10, 
                FailureTier.Success, null, 0, check, Pinder.Core.Stats.StatType.Charm);

            // Act
            string json = JsonSerializer.Serialize(result);

            // Assert
            // RollResult doesn't have [JsonPropertyName("tier")] on the property, but it's a public property.
            // In the DTOs (pinder-web), it's explicitly "tier". Here we just check the property is serialized.
            Assert.Contains("\"Tier\":0", json); // FailureTier.Success is 0
        }
    }
}
