namespace Pinder.Rules
{
    /// <summary>
    /// Callback interface for outcome effects dispatched by OutcomeDispatcher.
    /// Implemented by GameSession or test doubles.
    /// </summary>
    public interface IEffectHandler
    {
        void ApplyInterestDelta(int delta);
        void ActivateTrap(string trapId);
        void ApplyShadowGrowth(string shadowName, int delta, string reason);
        void SetRollModifier(string modifier);
        void SetRiskTier(string tier);
        void SetXpMultiplier(double multiplier);
    }
}
