using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;

namespace Pinder.SessionRunner.Snapshot
{
    /// <summary>
    /// Issue #474: derive the canonical per-turn event kinds from a
    /// <see cref="TurnResult"/>. Each detected kind matches an entry in
    /// <c>data/i18n/&lt;locale&gt;/events.yaml</c> so the snapshot
    /// writer can populate <see cref="EventSnapshot.EventInterpretation"/>
    /// via <c>I18nCatalog.TVariant(kind, turnNumber)</c>.
    ///
    /// <para>
    /// This detector is the source of truth for which engine signals
    /// rise to "event". Adding a new kind requires three coordinated
    /// changes (mirroring the i18n Phase 1.5 / Phase 2 contract):
    /// <list type="number">
    ///   <item><description>Yaml entry under <c>events:</c> with N
    ///   <c>summary_variants</c> (locked convention: 5).</description></item>
    ///   <item><description>Detection rule here \u2014 an additional
    ///   <c>events.Add(...)</c> call gated on the engine signal.</description></item>
    ///   <item><description>Frontend renderer mapping in
    ///   <c>frontend/src/i18n/useText.ts</c> (the picker is a port of
    ///   <see cref="Pinder.Core.I18n.VariantPicker"/>).</description></item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// Pure: no IO, no allocation beyond the result list. Safe to call
    /// inside hot paths.
    /// </para>
    /// </summary>
    internal static class TurnEventDetector
    {
        /// <summary>
        /// Derive the ordered list of event kinds fired on this turn.
        /// Order is the engine's emission order \u2014 see
        /// <see cref="TurnSnapshot.Events"/> for the canonical order.
        /// Returns an empty list when no event-class condition was met.
        /// </summary>
        public static List<string> DetectEventKinds(TurnResult result)
        {
            if (result is null) throw new ArgumentNullException(nameof(result));

            var kinds = new List<string>();

            // 1. Roll-class events. nat_20 / nat_1 are exclusive of each
            //    other and of the miss_by_N family (a nat_1 always
            //    misses but is its own category and the variant copy
            //    leans on the "automatic worst-case" framing).
            if (result.Roll.IsNatTwenty)
            {
                kinds.Add("nat_20");
            }
            else if (result.Roll.IsNatOne)
            {
                kinds.Add("nat_1");
            }
            else if (!result.Roll.IsSuccess && result.Roll.MissMargin > 0)
            {
                // miss_by_<exact margin>. The events.yaml ships
                // catalog entries for miss_by_1, miss_by_3, miss_by_5;
                // other margins emit the kind anyway and the snapshot
                // writer falls back to an empty interpretation for
                // forward compatibility (see TurnEventInterpreter).
                kinds.Add("miss_by_" + result.Roll.MissMargin);
            }

            // 2. Combo / tell / callback bonuses. Independent of roll
            //    class \u2014 you can land a nat_20 that also triggered a
            //    combo, and both events emit.
            if (!string.IsNullOrEmpty(result.ComboTriggered))
            {
                kinds.Add("combo_hit");
            }
            if (result.TellReadBonus > 0)
            {
                kinds.Add("tell_read");
            }
            if (result.CallbackBonusApplied > 0)
            {
                kinds.Add("callback_hit");
            }

            // 3. Horniness corruption. Emit when the per-turn check
            //    missed AND the overlay actually fired \u2014 a missed check
            //    that produced no overlay (e.g. no shadows present) is
            //    not a player-visible event from the events-yaml
            //    framing's perspective.
            if (result.HorninessCheck.IsMiss && result.HorninessCheck.OverlayApplied)
            {
                kinds.Add("horniness_fail");
            }

            // 4. Shadow ticks. ShadowGrowthEvents is a list of strings
            //    of the form "<ShadowStatType> +N (<reason>)" emitted
            //    by SessionShadowTracker.ApplyGrowth. We parse the
            //    leading shadow name and route to
            //    shadow_tick_<lowercase_name>. Multiple shadows can
            //    grow on a single turn (one entry per growth).
            foreach (string growth in result.ShadowGrowthEvents)
            {
                string? name = ParseShadowName(growth);
                if (name is null) continue;
                kinds.Add("shadow_tick_" + name.ToLowerInvariant());
            }

            // 5. Trap activation. The roll's ActivatedTrap is set when
            //    the player stepped into a trope-trap THIS turn (not
            //    when one was already active and ticking down).
            if (result.Roll.ActivatedTrap != null)
            {
                kinds.Add("trap_activated");
            }

            // crit_save is a class of event the engine doesn't surface
            // distinctly today \u2014 it would require pre-roll DC vs.
            // post-bonus comparison the TurnResult doesn't carry. Add
            // here when the engine-side signal lands; the events-yaml
            // entry is already in place.

            return kinds;
        }

        /// <summary>
        /// Parse the leading shadow stat name from a growth-event
        /// string. Returns null when the prefix doesn't match the
        /// "<c>&lt;Name&gt; +&lt;digits&gt;</c>" shape \u2014 defence in
        /// depth against future growth-event format drift.
        /// </summary>
        internal static string? ParseShadowName(string growth)
        {
            if (string.IsNullOrEmpty(growth)) return null;
            int sp = growth.IndexOf(' ');
            if (sp <= 0) return null;
            string head = growth.Substring(0, sp);
            // Be defensive: only accept all-letter names. Stops trash
            // strings (e.g. "+5 something") from forming bad kind ids.
            for (int i = 0; i < head.Length; i++)
            {
                if (!char.IsLetter(head[i])) return null;
            }
            // The next non-space char must be '+' for the tracker's
            // canonical shape; if not, treat as drift and skip.
            if (sp + 1 >= growth.Length || growth[sp + 1] != '+') return null;
            return head;
        }
    }
}
