using System;
using Pinder.Core.Stats;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Issue #1218: structured event payload for the
    /// <c>GameSessionConfig.OnShadowFilterTrace</c> callback.
    /// Fired when shadow filtering changes the option/stat pool.
    /// </summary>
    public sealed class ShadowFilterTraceEvent
    {
        /// <summary>The shadow stat that triggered the filter.</summary>
        public ShadowStatType ShadowStat { get; }

        /// <summary>The raw value of the shadow stat.</summary>
        public int RawValue { get; }

        /// <summary>The computed tier of the shadow stat.</summary>
        public int ComputedTier { get; }

        /// <summary>The stats that were removed due to filtering.</summary>
        public StatType[] RemovedStats { get; }

        /// <summary>Where the filter was applied: 'pre_llm_stat_draw' or 'post_llm_filter'.</summary>
        public string SourcePath { get; }

        public ShadowFilterTraceEvent(
            ShadowStatType shadowStat,
            int rawValue,
            int computedTier,
            StatType[] removedStats,
            string sourcePath)
        {
            ShadowStat = shadowStat;
            RawValue = rawValue;
            ComputedTier = computedTier;
            RemovedStats = removedStats ?? Array.Empty<StatType>();
            SourcePath = sourcePath ?? string.Empty;
        }
    }
}
