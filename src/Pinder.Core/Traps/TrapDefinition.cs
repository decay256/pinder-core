using Pinder.Core.Stats;

namespace Pinder.Core.Traps
{
    /// <summary>
    /// Data model for a trap definition. Maps to entries in data/traps/traps.json.
    /// </summary>
    public sealed class TrapDefinition
    {
        public string Id          { get; }
        public StatType Stat      { get; }

        // Mechanical effect
        public TrapEffect Effect  { get; }
        public int EffectValue    { get; }   // magnitude of penalty / DC increase
        public int DurationTurns  { get; }   // how many turns the trap is active

        // Prompt taint: passed to LLM layer as instruction
        public string LlmInstruction { get; }

        // How to clear early (besides waiting out duration)
        public string ClearMethod { get; }

        // Extra punishment on Nat 1 while trap is active
        public string Nat1Bonus   { get; }

        public TrapDefinition(
            string id,
            StatType stat,
            TrapEffect effect,
            int effectValue,
            int durationTurns,
            string llmInstruction,
            string clearMethod,
            string nat1Bonus)
        {
            Id             = id;
            Stat           = stat;
            Effect         = effect;
            EffectValue    = effectValue;
            DurationTurns  = durationTurns;
            LlmInstruction = llmInstruction;
            ClearMethod    = clearMethod;
            Nat1Bonus      = nat1Bonus;
        }
    }

    public enum TrapEffect
    {
        /// <summary>Roll stat at disadvantage while trap is active.</summary>
        Disadvantage,

        /// <summary>Flat penalty to the stat modifier.</summary>
        StatPenalty,

        /// <summary>Increases opponent's DC by raising their defending stat.</summary>
        OpponentDCIncrease
    }
}
