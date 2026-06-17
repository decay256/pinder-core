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
        /// The single yaml key (since #1154) holding the collapsed
        /// constant character-card framing. Its value is the 7 section
        /// labels, one per line, in emission order:
        /// RULES / IDENTITY / PERSONALITY / BACKSTORY / TEXTING STYLE /
        /// ACTIVE ARCHETYPE / ACTIVE TRAP INSTRUCTIONS.
        /// </summary>
        public const string CharacterCardFramingKey = "character_card_framing";

        /// <summary>
        /// Resolves structural prompt fragments from the yaml catalog.
        /// Since #1154 the only key consulted is
        /// <see cref="CharacterCardFramingKey"/> (<c>"character_card_framing"</c>).
        /// Set at startup (typically by <c>PromptWiring.Wire()</c>). After
        /// Phase 5 of #871, null or missing keys cause
        /// <see cref="BuildSystemPrompt"/> to throw — there are no embedded
        /// const fallbacks.
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
        /// #1154: the 7 constant section labels recovered, in order, from
        /// the single <see cref="CharacterCardFramingKey"/> field.
        /// </summary>
        private readonly struct CardFraming
        {
            public readonly string LeadIn;
            public readonly string Identity;
            public readonly string Personality;
            public readonly string Backstory;
            public readonly string TextingStyle;
            public readonly string ActiveArchetype;
            public readonly string ActiveTrapInstructions;
            public readonly string SourceFile;

            public CardFraming(string[] labels, string sourceFile)
            {
                LeadIn                 = labels[0];
                Identity               = labels[1];
                Personality            = labels[2];
                Backstory              = labels[3];
                TextingStyle           = labels[4];
                ActiveArchetype        = labels[5];
                ActiveTrapInstructions = labels[6];
                SourceFile             = sourceFile;
            }
        }

        /// <summary>
        /// Load the collapsed framing field and split it back into the 7
        /// section labels (one per line, in emission order). The split is
        /// byte-preserving: each recovered label is emitted in the exact
        /// same position as the legacy per-key headers.
        /// </summary>
        private static CardFraming GetCardFraming()
        {
            var framing = GetHeaderEx(CharacterCardFramingKey);
            // Split on LF, tolerating CRLF, and drop a single trailing
            // blank line if the yaml block scalar produced one.
            var lines = framing.Content.Replace("\r\n", "\n").Split('\n');
            int count = lines.Length;
            while (count > 0 && lines[count - 1].Length == 0) count--;

            if (count != 7)
            {
                throw new InvalidOperationException(
                    $"prompt-catalog: '{CharacterCardFramingKey}' must declare exactly " +
                    $"7 section labels (one per line); found {count}. " +
                    $"Check data/prompts/structural.yaml.");
            }

            var labels = new string[7];
            for (int i = 0; i < 7; i++) labels[i] = lines[i];
            return new CardFraming(labels, framing.SourceFile);
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
            string? characterIdSeed = null,
            bool archetypesEnabled = false)
        {
            return BuildSystemPromptEx(displayName, genderIdentity, bioOneLiner, fragments, activeTraps, characterIdSeed, archetypesEnabled).Text;
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
            string? characterIdSeed = null,
            bool archetypesEnabled = false)
        {
            if (displayName  == null) throw new ArgumentNullException(nameof(displayName));
            if (genderIdentity == null) throw new ArgumentNullException(nameof(genderIdentity));
            if (fragments    == null) throw new ArgumentNullException(nameof(fragments));
            if (activeTraps  == null) throw new ArgumentNullException(nameof(activeTraps));

            var sb = new AnnotatedStringBuilder();

            // #1154: the constant section framing now lives in ONE collapsed
            // field (character_card_framing); split it back into the 7 labels
            // and emit them in the EXACT same byte positions as before.
            var framing = GetCardFraming();
            string srcFile = framing.SourceFile;
            const string srcKey = CharacterCardFramingKey;

            // --- CONSTANT PREFIX BLOCK ---
            // Emitted without variable data to form a stable cacheable prefix.
            sb.AppendLine(framing.LeadIn, srcFile, srcKey); // No Replace("{name}", displayName) in constant prefix
            sb.AppendLine(framing.Identity, srcFile, srcKey);
            sb.AppendLine(framing.Personality, srcFile, srcKey);
            sb.AppendLine(framing.Backstory, srcFile, srcKey);
            sb.AppendLine(framing.TextingStyle, srcFile, srcKey);
            if (archetypesEnabled)
            {
                sb.AppendLine(framing.ActiveArchetype, srcFile, srcKey);
            }
            sb.AppendLine("EFFECTIVE STATS");
            sb.AppendLine(framing.ActiveTrapInstructions, srcFile, srcKey);

            // --- SEPARATOR ---
            sb.AppendLine();
            sb.AppendLine("=== CHARACTER DATA ===");

            // --- VARIABLE BLOCK ---
            sb.AppendLine(framing.Identity, srcFile, srcKey);
            sb.AppendLine($"- Gender identity: {genderIdentity}");
            sb.AppendLine($"- Bio: {(string.IsNullOrWhiteSpace(bioOneLiner) ? "none" : bioOneLiner)}");
            sb.AppendLine();

            sb.AppendLine(framing.Personality, srcFile, srcKey);
            AppendBulletList(sb, fragments.PersonalityFragments);
            sb.AppendLine();

            sb.AppendLine(framing.Backstory, srcFile, srcKey);
            AppendBulletList(sb, fragments.BackstoryFragments);
            sb.AppendLine();

            sb.AppendLine(framing.TextingStyle, srcFile, srcKey);
            AppendBulletList(sb, TextingStyleAggregator.AggregateAsList(
                fragments.TextingStyleSources, characterIdSeed));
            sb.AppendLine();

            if (archetypesEnabled)
            {
                sb.AppendLine(framing.ActiveArchetype, srcFile, srcKey);
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
            }

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
                    sb.AppendLine(framing.ActiveTrapInstructions, srcFile, srcKey);
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
