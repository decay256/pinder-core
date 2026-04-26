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

        // Player-facing copy (#255)
        // Title-Case display name (e.g. "Cringe"). Falls back to Id when the
        // data file does not provide one, so legacy data continues to work.
        public string DisplayName { get; }

        // Short one-sentence player-facing flavour. Distinct from
        // LlmInstruction (which is internal). Empty string when absent.
        public string Summary     { get; }

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
            string nat1Bonus,
            string? displayName = null,
            string? summary = null)
        {
            Id             = id;
            Stat           = stat;
            Effect         = effect;
            EffectValue    = effectValue;
            DurationTurns  = durationTurns;
            LlmInstruction = llmInstruction;
            ClearMethod    = clearMethod;
            Nat1Bonus      = nat1Bonus;
            DisplayName    = string.IsNullOrEmpty(displayName) ? id : displayName!;
            Summary        = summary ?? "";
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
