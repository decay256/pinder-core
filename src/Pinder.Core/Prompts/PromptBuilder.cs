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
    /// Issue #874 Phase 3: structural prompt fragments (section headers +
    /// lead-in line) have been lifted into
    /// <c>data/prompts/structural.yaml</c>. When
    /// <see cref="StructuralFragmentLookup"/> is set, fragments are sourced
    /// from the yaml catalog; otherwise the embedded const strings serve as
    /// the default. Phase 5 (#875) removes the const fallbacks once every
    /// call-site is wired.
    /// </para>
    /// <para>
    /// Because PromptBuilder lives in <c>Pinder.Core</c> (which cannot
    /// reference <c>Pinder.LlmAdapters</c> without creating a circular
    /// dependency), the lookup is a delegate rather than a typed
    /// <c>PromptCatalog</c> reference. The startup code in
    /// <c>Pinder.SessionSetup</c> (which has access to the catalog) wires the
    /// delegate at bootstrap time.
    /// </para>
    /// </remarks>
    public static class PromptBuilder
    {
        /// <summary>
        /// Optional catalog lookup for structural prompt fragments. Keys are
        /// kebab-case (e.g. <c>"structural-lead-in"</c>,
        /// <c>"structural-identity"</c>). Set this from startup code that has
        /// access to <c>PromptCatalog</c>. When null or returns null/empty,
        /// the embedded const strings in this class are used as fallbacks.
        /// Phase 5 (#875) removes the const fallbacks.
        /// </summary>
        /// <remarks>
        /// Typical wiring from Pinder.SessionSetup startup:
        /// <c>PromptBuilder.StructuralFragmentLookup = key => catalog?.TryGet(key)?.SystemPrompt;</c>
        /// </remarks>
        public static Func<string, string?>? StructuralFragmentLookup { get; set; }

        /// <summary>
        /// Resolve a structural fragment by name. Returns the yaml-sourced
        /// string when the lookup delegate is set and the key exists;
        /// otherwise returns <paramref name="constFallback"/>.
        /// </summary>
        private static string TryGetHeader(string key, string constFallback)
        {
            var fromCatalog = StructuralFragmentLookup?.Invoke(key);
            return !string.IsNullOrWhiteSpace(fromCatalog) ? fromCatalog! : constFallback;
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

            // Header
            const string fallbackLeadIn =
                "You are playing the role of {name}, a sentient penis on the dating app Pinder.";
            string leadInTemplate = TryGetHeader("structural-lead-in", fallbackLeadIn);
            sb.AppendLine(leadInTemplate.Replace("{name}", displayName));
            sb.AppendLine();

            // IDENTITY
            sb.AppendLine(TryGetHeader("structural-identity", "IDENTITY"));
            sb.AppendLine($"- Gender identity: {genderIdentity}");
            sb.AppendLine($"- Bio: {(string.IsNullOrWhiteSpace(bioOneLiner) ? "none" : bioOneLiner)}");
            sb.AppendLine();

            // PERSONALITY
            // #833: emit as a bullet list (one fragment per line) instead
            // of a `" | "`-joined prose blob. Easier for the LLM to scan,
            // easier to provenance back to the originating item / anatomy
            // when a fragment surfaces verbatim in delivered text.
            sb.AppendLine(TryGetHeader("structural-personality", "PERSONALITY"));
            AppendBulletList(sb, fragments.PersonalityFragments);
            sb.AppendLine();

            // BACKSTORY
            // #833: emit as a bullet list. Was newline-joined (closer to
            // bulleted than the other sections) but make it explicit so
            // the leading `- ` marker is consistent across every
            // multi-fragment section.
            sb.AppendLine(TryGetHeader("structural-backstory", "BACKSTORY"));
            AppendBulletList(sb, fragments.BackstoryFragments);
            sb.AppendLine();

            // TEXTING STYLE
            // #836 placeholder aggregation: anatomy is excluded from this
            // channel and only up to 2 item fragments are kept (deterministic
            // pick keyed on characterIdSeed). The full per-source list on
            // FragmentCollection.TextingStyleSources is unaffected — the
            // Character Sheet UI still renders every fragment.
            //
            // #833: AggregateAsList returns the picked fragments as an
            // ordered list so we can bullet-format them here, consistent
            // with PERSONALITY / BACKSTORY. The legacy `Aggregate` (joined
            // string) is still available for delivery context
            // (CharacterProfile.TextingStyleFragment).
            sb.AppendLine(TryGetHeader("structural-texting-style", "TEXTING STYLE"));
            AppendBulletList(sb, TextingStyleAggregator.AggregateAsList(
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
            sb.AppendLine(TryGetHeader("structural-active-archetype", "ACTIVE ARCHETYPE"));
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
                    sb.AppendLine(TryGetHeader("structural-active-trap-instructions", "ACTIVE TRAP INSTRUCTIONS"));
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
