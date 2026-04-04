using System.Collections.Generic;
using Pinder.Rules;

namespace Pinder.Rules.Tests
{
    /// <summary>
    /// Test double that records all dispatched effects for assertion.
    /// </summary>
    public sealed class TestEffectHandler : IEffectHandler
    {
        public List<int> InterestDeltas { get; } = new List<int>();
        public List<string> ActivatedTraps { get; } = new List<string>();
        public List<(string Shadow, int Delta, string Reason)> ShadowGrowths { get; }
            = new List<(string, int, string)>();
        public List<string> RollModifiers { get; } = new List<string>();
        public List<string> RiskTiers { get; } = new List<string>();
        public List<double> XpMultipliers { get; } = new List<double>();

        public void ApplyInterestDelta(int delta) => InterestDeltas.Add(delta);
        public void ActivateTrap(string trapId) => ActivatedTraps.Add(trapId);
        public void ApplyShadowGrowth(string shadowName, int delta, string reason)
            => ShadowGrowths.Add((shadowName, delta, reason));
        public void SetRollModifier(string modifier) => RollModifiers.Add(modifier);
        public void SetRiskTier(string tier) => RiskTiers.Add(tier);
        public void SetXpMultiplier(double multiplier) => XpMultipliers.Add(multiplier);
    }
}
