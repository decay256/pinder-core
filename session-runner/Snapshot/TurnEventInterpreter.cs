using System.Collections.Generic;
using Pinder.LlmAdapters;

namespace Pinder.SessionRunner.Snapshot
{
    /// <summary>
    /// Issue #474: thin wrapper around <see cref="I18nCatalog.TVariant"/>
    /// that catches the missing-kind case (the catalog throws on
    /// unknown kinds; we want graceful fallback for forward compat
    /// with new event kinds that the engine emits before the yaml
    /// catches up).
    /// </summary>
    internal static class TurnEventInterpreter
    {
        /// <summary>
        /// Build the per-event interpretation block. <paramref name="catalog"/>
        /// may be null \u2014 the simulator runs in environments where the
        /// data/i18n directory isn't available (CI fixtures, ad-hoc
        /// repros); we degrade to empty interpretations rather than
        /// crashing the snapshot write.
        /// </summary>
        public static List<EventSnapshot> Build(
            IReadOnlyList<string> kinds, int turnNumber, I18nCatalog? catalog)
        {
            var events = new List<EventSnapshot>(kinds.Count);
            foreach (string kind in kinds)
            {
                string interpretation = string.Empty;
                if (catalog != null)
                {
                    try
                    {
                        interpretation = catalog.TVariant(kind, turnNumber);
                    }
                    catch (KeyNotFoundException)
                    {
                        // Unknown kind in this locale \u2014 leave empty.
                        // Engine + yaml are intentionally allowed to
                        // drift in either direction during sprints; the
                        // snapshot still records the kind so a later
                        // pass can re-render with a fresh catalog.
                    }
                }
                events.Add(new EventSnapshot
                {
                    Kind = kind,
                    TurnNumber = turnNumber,
                    EventInterpretation = interpretation,
                });
            }
            return events;
        }
    }
}
