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
        public static string BuildSystemPrompt(
            string displayName,
            string genderIdentity,
            string? bioOneLiner,
            FragmentCollection fragments,
            TrapState activeTraps)
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
            sb.AppendLine("TEXTING STYLE");
            sb.AppendLine(string.Join(" | ", fragments.TextingStyleFragments));
            sb.AppendLine();

            // ARCHETYPES
            sb.AppendLine("ARCHETYPES (tendency order — most to least dominant)");
            for (int i = 0; i < fragments.RankedArchetypes.Count; i++)
            {
                var (archetype, count) = fragments.RankedArchetypes[i];
                sb.AppendLine($"{i + 1}. {archetype} (x{count})");
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
