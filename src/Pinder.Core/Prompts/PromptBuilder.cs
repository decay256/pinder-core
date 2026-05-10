using System;
using System.Text;
using Pinder.Core.Characters;
using Pinder.Core.Stats;
using Pinder.Core.Traps;

namespace Pinder.Core.Prompts
{
    /// <summary>
    /// Builds the §3.1 LLM system prompt from an assembled FragmentCollection.
    /// No LLM call is made here — this is pure string construction.
    /// </summary>
    public static class PromptBuilder
    {
        /// <summary>
        /// Assemble the full system prompt for a character.
        /// </summary>
        /// <param name="displayName">Character display name.</param>
        /// <param name="genderIdentity">e.g. "she/her", "they/them"</param>
        /// <param name="bioOneLiner">Optional player-written bio line. Null → "none".</param>
        /// <param name="fragments">Assembled fragment collection from CharacterAssembler.</param>
        /// <param name="activeTraps">Current trap state. May have zero active traps.</param>
        /// <param name="characterIdSeed">
        /// Optional stable per-character seed used by the placeholder
        /// texting-style aggregator (#836) to pick a deterministic subset
        /// of item fragments. Pass the character UUID when available. When
        /// null/empty, the aggregator falls back to a stable hash of the
        /// fragment content itself, which is still deterministic for a
        /// given configuration but not stable across reconfigurations.
        /// </param>
        public static string BuildSystemPrompt(
            string displayName,
            string genderIdentity,
            string? bioOneLiner,
            FragmentCollection fragments,
            TrapState activeTraps,
            string? characterIdSeed = null)
        {
            if (displayName  == null) throw new ArgumentNullException(nameof(displayName));
            if (genderIdentity == null) throw new ArgumentNullException(nameof(genderIdentity));
            if (fragments    == null) throw new ArgumentNullException(nameof(fragments));
            if (activeTraps  == null) throw new ArgumentNullException(nameof(activeTraps));

            var sb = new StringBuilder();

            // Header
            sb.AppendLine($"You are playing the role of {displayName}, a sentient penis on the dating app Pinder.");
            sb.AppendLine();

            // IDENTITY
            sb.AppendLine("IDENTITY");
            sb.AppendLine($"- Gender identity: {genderIdentity}");
            sb.AppendLine($"- Bio: {(string.IsNullOrWhiteSpace(bioOneLiner) ? "none" : bioOneLiner)}");
            sb.AppendLine();

            // PERSONALITY
            sb.AppendLine("PERSONALITY");
            sb.AppendLine(string.Join(" | ", fragments.PersonalityFragments));
            sb.AppendLine();

            // BACKSTORY
            sb.AppendLine("BACKSTORY");
            sb.AppendLine(string.Join(Environment.NewLine, fragments.BackstoryFragments));
            sb.AppendLine();

            // TEXTING STYLE
            // #836 placeholder aggregation: anatomy is excluded from this
            // channel and only up to 2 item fragments are kept (deterministic
            // pick keyed on characterIdSeed). The full per-source list on
            // FragmentCollection.TextingStyleSources is unaffected — the
            // Character Sheet UI still renders every fragment.
            sb.AppendLine("TEXTING STYLE");
            sb.AppendLine(TextingStyleAggregator.Aggregate(
                fragments.TextingStyleSources, characterIdSeed));
            sb.AppendLine();

            // ACTIVE ARCHETYPE (#832): emit only the level-eligible top-ranked
            // archetype with its full behavior text. The ranked-list of every
            // archetype the character has any tendency vote for was noise —
            // the LLM had no way to tell which were level-eligible vs.
            // pre-cap or post-cap, and the bare names without behavior text
            // forced the model to use training-data priors. The active
            // archetype's behavior block (sourced from
            // ArchetypeCatalog._behaviors / archetypes-enriched.yaml §2) is
            // rich, load-bearing, and includes sample lines — exactly the
            // texture the dialogue model needs.
            //
            // Fallback: when ResolveActiveArchetype returns null (legacy /
            // under-leveled / no archetype votes), emit a single-line
            // marker so the prompt stays well-formed and downstream parsers
            // (e.g. system-prompt-shape tests) still find the section.
            sb.AppendLine("ACTIVE ARCHETYPE");
            if (fragments.ActiveArchetype != null)
            {
                var aa = fragments.ActiveArchetype;
                sb.AppendLine($"- {aa.Name} ({aa.InterferenceLevel})");
                if (!string.IsNullOrWhiteSpace(aa.Behavior))
                    sb.AppendLine(aa.Behavior);
            }
            else
            {
                sb.AppendLine("(none resolved)");
            }
            sb.AppendLine();

            // EFFECTIVE STATS
            sb.AppendLine("EFFECTIVE STATS");
            sb.AppendLine($"- Charm: {fragments.Stats.GetEffective(StatType.Charm)}");
            sb.AppendLine($"- Rizz: {fragments.Stats.GetEffective(StatType.Rizz)}");
            sb.AppendLine($"- Honesty: {fragments.Stats.GetEffective(StatType.Honesty)}");
            sb.AppendLine($"- Chaos: {fragments.Stats.GetEffective(StatType.Chaos)}");
            sb.AppendLine($"- Wit: {fragments.Stats.GetEffective(StatType.Wit)}");
            sb.AppendLine($"- Self-Awareness: {fragments.Stats.GetEffective(StatType.SelfAwareness)}");

            // ACTIVE TRAP INSTRUCTIONS (only if traps are active)
            bool hasActiveHeader = false;
            foreach (var trap in activeTraps.AllActive)
            {
                if (!hasActiveHeader)
                {
                    sb.AppendLine();
                    sb.AppendLine("ACTIVE TRAP INSTRUCTIONS");
                    hasActiveHeader = true;
                }
                sb.AppendLine(trap.Definition.LlmInstruction);
            }

            return sb.ToString().TrimEnd();
        }
    }
}
