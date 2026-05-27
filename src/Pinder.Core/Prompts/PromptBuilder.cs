using System;
using System.Text;
using Pinder.Core.Characters;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Pinder.Core.Text;

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
        /// Enhanced structural fragment resolver that also yields the source file path.
        /// </summary>
        public static Func<string, StructuralPromptResult?>? StructuralFragmentLookupEx { get; set; }

        /// <summary>
        /// Resolve a structural fragment by name. Throws when the lookup
        /// delegate is not wired or the key is missing — after Phase 5
        /// there are no const fallbacks.
        /// </summary>
        private static string GetHeader(string key)
        {
            return GetHeaderEx(key).Content;
        }

        private static (string Content, string SourceFile) GetHeaderEx(string key)
        {
            var lookup = StructuralFragmentLookup
                ?? throw new InvalidOperationException(
                    $"PromptBuilder.StructuralFragmentLookup is not wired. " +
                    $"Call PromptWiring.Wire() at startup. (key: '{key}')");
            var value = lookup(key);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    $"prompt-catalog: missing required key '{key}'. " +
                    $"Check data/prompts/structural.yaml.");
            }

            string sourceFile = "data/prompts/structural.yaml";
            if (StructuralFragmentLookupEx != null)
            {
                var result = StructuralFragmentLookupEx(key);
                if (result != null && !string.IsNullOrWhiteSpace(result.SourceFile))
                {
                    sourceFile = result.SourceFile;
                }
            }

            return (value, sourceFile);
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
        /// <summary>
        /// Assemble the full system prompt for a character.
        /// </summary>
        public static string BuildSystemPrompt(
            string displayName,
            string genderIdentity,
            string? bioOneLiner,
            FragmentCollection fragments,
            TrapState activeTraps,
            string? characterIdSeed = null)
        {
            return BuildSystemPromptEx(displayName, genderIdentity, bioOneLiner, fragments, activeTraps, characterIdSeed).Text;
        }

        /// <summary>
        /// Assemble the full system prompt for a character, returning trace data.
        /// </summary>
        public static PromptTraceResult BuildSystemPromptEx(
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

            var sb = new AnnotatedStringBuilder();

            // Header — sourced from structural.yaml, no fallback.
            var leadIn = GetHeaderEx("structural-lead-in");
            sb.AppendLine(leadIn.Content.Replace("{name}", displayName), leadIn.SourceFile, "structural-lead-in");
            sb.AppendLine();

            // IDENTITY
            var identity = GetHeaderEx("structural-identity");
            sb.AppendLine(identity.Content, identity.SourceFile, "structural-identity");
            sb.AppendLine($"- Gender identity: {genderIdentity}");
            sb.AppendLine($"- Bio: {(string.IsNullOrWhiteSpace(bioOneLiner) ? "none" : bioOneLiner)}");
            sb.AppendLine();

            // PERSONALITY
            var personality = GetHeaderEx("structural-personality");
            sb.AppendLine(personality.Content, personality.SourceFile, "structural-personality");
            AppendBulletList(sb, fragments.PersonalityFragments);
            sb.AppendLine();

            // BACKSTORY
            var backstory = GetHeaderEx("structural-backstory");
            sb.AppendLine(backstory.Content, backstory.SourceFile, "structural-backstory");
            AppendBulletList(sb, fragments.BackstoryFragments);
            sb.AppendLine();

            // TEXTING STYLE
            var textingStyle = GetHeaderEx("structural-texting-style");
            sb.AppendLine(textingStyle.Content, textingStyle.SourceFile, "structural-texting-style");
            AppendBulletList(sb, TextingStyleAggregator.AggregateAsList(
                fragments.TextingStyleSources, characterIdSeed));
            sb.AppendLine();

            // ACTIVE ARCHETYPE
            var activeArchetype = GetHeaderEx("structural-active-archetype");
            sb.AppendLine(activeArchetype.Content, activeArchetype.SourceFile, "structural-active-archetype");
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
                    var activeTrapHeader = GetHeaderEx("structural-active-trap-instructions");
                    sb.AppendLine(activeTrapHeader.Content, activeTrapHeader.SourceFile, "structural-active-trap-instructions");
                    hasActiveHeader = true;
                }
                sb.AppendLine(trap.Definition.LlmInstruction);
            }

            return new PromptTraceResult(sb.ToString().TrimEnd(), sb.Spans);
        }

        private static void AppendBulletList(AnnotatedStringBuilder sb, System.Collections.Generic.IReadOnlyList<string> fragments)
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
