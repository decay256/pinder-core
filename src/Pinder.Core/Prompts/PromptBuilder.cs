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
    /// <remarks>
    /// <para>
    /// Issue #871 Phase 5 (#875): all const-string prompt content has been
    /// deleted. Structural prompt fragments are now sourced exclusively from
    /// the yaml catalog via the <see cref="StructuralFragmentLookup"/>
    /// delegate, which MUST be wired at startup (typically by
    /// <c>PromptWiring.Wire()</c>).
    /// </para>
    /// <para>
    /// Because PromptBuilder lives in <c>Pinder.Core</c> (which cannot
    /// reference <c>Pinder.LlmAdapters</c> without creating a circular
    /// dependency), the lookup is a delegate rather than a typed
    /// <c>PromptCatalog</c> reference.
    /// </para>
    /// </remarks>
    public static class PromptBuilder
    {
        /// <summary>
        /// Resolves structural prompt fragments from the yaml catalog.
        /// Keys are kebab-case (e.g. <c>"structural-lead-in"</c>,
        /// <c>"structural-identity"</c>). Set at startup (typically by
        /// <c>PromptWiring.Wire()</c>). After Phase 5 of #871, null
        /// or missing keys cause <see cref="BuildSystemPrompt"/> to throw
        /// — there are no embedded const fallbacks.
        /// </summary>
        public static Func<string, string?>? StructuralFragmentLookup { get; set; }

        /// <summary>
        /// Resolve a structural fragment by name. Throws when the lookup
        /// delegate is not wired or the key is missing — after Phase 5
        /// there are no const fallbacks.
        /// </summary>
        private static string GetHeader(string key)
        {
            var lookup = StructuralFragmentLookup
                ?? throw new InvalidOperationException(
                    $"PromptBuilder.StructuralFragmentLookup is not wired. " +
                    $"Call PromptWiring.Wire() at startup. (key: '{key}')");
            var value = lookup(key);
            return !string.IsNullOrWhiteSpace(value)
                ? value!
                : throw new InvalidOperationException(
                    $"prompt-catalog: missing required key '{key}'. " +
                    $"Check data/prompts/structural.yaml.");
        }

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

            // Header — sourced from structural.yaml, no fallback.
            string leadInTemplate = GetHeader("structural-lead-in");
            sb.AppendLine(leadInTemplate.Replace("{name}", displayName));
            sb.AppendLine();

            // IDENTITY
            sb.AppendLine(GetHeader("structural-identity"));
            sb.AppendLine($"- Gender identity: {genderIdentity}");
            sb.AppendLine($"- Bio: {(string.IsNullOrWhiteSpace(bioOneLiner) ? "none" : bioOneLiner)}");
            sb.AppendLine();

            // PERSONALITY
            sb.AppendLine(GetHeader("structural-personality"));
            AppendBulletList(sb, fragments.PersonalityFragments);
            sb.AppendLine();

            // BACKSTORY
            sb.AppendLine(GetHeader("structural-backstory"));
            AppendBulletList(sb, fragments.BackstoryFragments);
            sb.AppendLine();

            // TEXTING STYLE
            sb.AppendLine(GetHeader("structural-texting-style"));
            AppendBulletList(sb, TextingStyleAggregator.AggregateAsList(
                fragments.TextingStyleSources, characterIdSeed));
            sb.AppendLine();

            // ACTIVE ARCHETYPE
            sb.AppendLine(GetHeader("structural-active-archetype"));
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
                    sb.AppendLine(GetHeader("structural-active-trap-instructions"));
                    hasActiveHeader = true;
                }
                sb.AppendLine(trap.Definition.LlmInstruction);
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// #833: helper that emits a list of fragments as a bullet list,
        /// one fragment per line with a leading <c>- </c> marker. Empty
        /// or null entries are skipped (rather than emitted as an empty
        /// bullet). When the input list itself is empty, no output is
        /// written — callers can decide whether the section header alone
        /// is meaningful for the empty case (e.g. PERSONALITY with no
        /// items still emits the header for downstream parser stability).
        /// </summary>
        private static void AppendBulletList(StringBuilder sb, System.Collections.Generic.IReadOnlyList<string> fragments)
        {
            if (fragments == null) return;
            for (int i = 0; i < fragments.Count; i++)
            {
                string fragment = fragments[i];
                if (string.IsNullOrWhiteSpace(fragment)) continue;
                sb.Append("- ");
                sb.AppendLine(fragment);
            }
        }
    }
}
