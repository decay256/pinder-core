using System;
using System.Collections.Generic;
using Pinder.Core.Characters;
using YamlDotNet.RepresentationModel;

namespace Pinder.LlmAdapters
{
    /// <summary>
    /// Loads archetype behavior text from yaml and registers it with
    /// <see cref="ArchetypeCatalog"/> so the LLM directive
    /// ("ACTIVE ARCHETYPE: ...") is sourced from the canonical yaml rather
    /// than from hand-copied literals in <see cref="ArchetypeCatalog"/>'s
    /// static initialiser.
    ///
    /// <para>
    /// Consumes a <see cref="PromptCatalog"/> loaded from
    /// <c>data/prompts/archetypes.yaml</c> (Issue #873 Phase 4). Each prompt
    /// entry's key is the archetype name (e.g. <c>"The Hey Opener"</c>) and
    /// its <c>system_prompt</c> is the behavioural instruction text. This
    /// consolidates the inline const strings from
    /// <see cref="ArchetypeCatalog"/>'s static initialiser into the same
    /// <c>PromptCatalog</c>-format yaml family used by Phases 1–3 of #871.
    /// </para>
    ///
    /// <para>
    /// The hardcoded behaviour strings in <see cref="ArchetypeCatalog"/>'s
    /// static initialiser remain as a degraded-mode fallback. If the yaml
    /// file is missing, fails to parse, or lacks an entry for a given
    /// archetype, the catalog falls back to the hand-copied literal.
    /// </para>
    /// </summary>
    public static class ArchetypeYamlLoader
    {


        /// <summary>
        /// Wire <see cref="ArchetypeCatalog.BehaviorResolver"/> so
        /// <see cref="ArchetypeCatalog.GetBehavior"/> prefers the yaml
        /// catalog over the embedded const strings.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Issue #873 Phase 4: the catalog is loaded from
        /// <c>data/prompts/archetypes.yaml</c> by
        /// <c>PromptCatalog.LoadFromDirectory("data/prompts")</c> at startup.
        /// Each prompt key is the archetype name (e.g. <c>"The Hey Opener"</c>)
        /// and its <c>system_prompt</c> is the behavior text. Entries that are
        /// already in the catalog (from other yaml files like
        /// <c>templates.yaml</c>) are silently skipped since they won't match
        /// any archetype name.
        /// </para>
        /// <para>
        /// The resolver delegate is the single source of truth — there is no
        /// longer a bulk <c>RegisterBehavior</c> loop because the resolver
        /// provides the same outcome with fewer mutation points. Callers that
        /// need to reset state after testing should save and restore
        /// <see cref="ArchetypeCatalog.BehaviorResolver"/>.
        /// </para>
        /// </remarks>
        /// <param name="catalog">A fully-loaded <see cref="PromptCatalog"/>.</param>
        public static void LoadFromPromptCatalog(PromptCatalog catalog)
        {
            if (catalog is null) throw new ArgumentNullException(nameof(catalog));

            // Wire the resolver so GetBehavior prefers the yaml catalog
            // (Issue #873 Phase 4 — delegate pattern crosses the assembly
            // boundary between Pinder.Core and Pinder.LlmAdapters).
            ArchetypeCatalog.BehaviorResolver = name =>
                catalog.TryGet(name)?.SystemPrompt;
        }


    }
}
